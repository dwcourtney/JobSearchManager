namespace JobSearchManager;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public sealed class AppStateStore
{
    private const int SchemaVersion = 5;
    private const int CacheSchemaVersion = 6;
    private readonly ILogger<AppStateStore> _logger;
    private readonly CompanyCatalog _companyCatalog;
    private readonly JobConceptCatalog _jobConceptCatalog;
    private readonly IWorkspaceDataStore _dataStore;
    private readonly SemaphoreSlim _cacheMigrationGate = new(1, 1);
    private bool _cacheMigrationChecked;

    public AppStateStore(
        ILogger<AppStateStore> logger,
        CompanyCatalog companyCatalog,
        IWorkspaceDataStore dataStore,
        JobConceptCatalog? jobConceptCatalog = null)
    {
        _logger = logger;
        _companyCatalog = companyCatalog;
        _dataStore = dataStore;
        _jobConceptCatalog = jobConceptCatalog ?? JobConceptCatalog.LoadDefault();
    }

    public string DataDirectory => _dataStore.Description;
    public string SettingsPath => _dataStore.Describe(WorkspaceDataFile.Settings);
    public string JobsCachePath => _dataStore.Describe(WorkspaceDataFile.JobsCache);
    public string JobHistoryPath => _dataStore.Describe(WorkspaceDataFile.JobHistory);
    internal WorkspaceStorageDiagnostics StorageDiagnostics => _dataStore.Diagnostics;

    public async Task<ViewerSettings> LoadSettingsAsync()
    {
        var stored = await _dataStore.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings) ??
            ViewerSettings.Default;
        var hadUnsupportedActiveCompany = stored.HasConfiguredSource != false &&
            !_companyCatalog.TryGet(stored.CompanyId, out _);
        var hadUnsupportedCompanySource = (stored.CompanySources ??
            new Dictionary<string, CompanySourceSettings>())
            .Keys.Any(companyId => !_companyCatalog.TryGet(companyId, out _));
        var normalized = NormalizeSettings(stored);

        if (hadUnsupportedActiveCompany || hadUnsupportedCompanySource)
        {
            await _dataStore.WriteJsonAsync(WorkspaceDataFile.Settings, normalized);
            _logger.LogInformation(
                "Migrated unsupported job-source company state in {SettingsPath} to supported company {CompanyId}; unsupported per-company source selections were removed.",
                SettingsPath,
                normalized.CompanyId);
        }

        return normalized;
    }

    public Task SaveSettingsAsync(ViewerSettings settings) =>
        _dataStore.WriteJsonAsync(WorkspaceDataFile.Settings, NormalizeSettings(settings));

    public async Task EnsureSettingsFileAsync()
    {
        if (!await _dataStore.ExistsAsync(WorkspaceDataFile.Settings))
        {
            await SaveSettingsAsync(ViewerSettings.Default);
        }
    }

    internal string QueryFingerprint(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        var canonical = string.Join('\n',
            company.Id,
            query.CountryId?.Trim() ?? "",
            query.IncludeAllLocations ? "all" : "selected",
            query.IncludeRemote ? "remote" : "physical",
            string.Join('\n', (query.PhysicalLocations ?? [])
                .Select(location => location.Id?.Trim() ?? "")
                .Where(id => id.Length > 0)
                .OrderBy(id => id, StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    internal string JobsCachePathFor(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        return _dataStore.DescribeCompanyCache(company.Id, QueryFingerprint(query));
    }

    internal async Task<JobsCacheDocument?> LoadJobsCacheAsync(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        await EnsureSplitCacheMigrationAsync(query);
        var fingerprint = QueryFingerprint(query);
        var cache = await _dataStore.ReadCompanyCacheJsonAsync<JobsCacheDocument>(
            company.Id, fingerprint);
        if (cache?.Jobs is null)
        {
            return cache;
        }

        var (jobs, migrationRequired) = InflateAndMigrateJobs(cache.Jobs);
        var migrated = cache with
        {
            SchemaVersion = CacheSchemaVersion,
            Jobs = jobs,
            Query = query
        };
        if (migrationRequired || cache.SchemaVersion != CacheSchemaVersion)
        {
            await SaveCacheDocumentAsync(migrated);
            _logger.LogInformation(
                "Migrated cached job data in {JobsCachePath} to schema {SchemaVersion}.",
                JobsCachePathFor(query),
                CacheSchemaVersion);
        }
        return migrated;
    }

    private (JobRecord[] Jobs, bool MigrationRequired) InflateAndMigrateJobs(
        IReadOnlyList<JobRecord> cachedJobs)
    {
        var migrationRequired = false;
        var jobs = cachedJobs.Select(job =>
        {
            if (!string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
                string.IsNullOrWhiteSpace(job.CompressedDescriptionHtml))
            {
                migrationRequired = true;
            }
            if (string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
                !string.IsNullOrWhiteSpace(job.CompressedDescriptionHtml))
            {
                try
                {
                    job = job with
                    {
                        DescriptionHtml = Decompress(job.CompressedDescriptionHtml),
                        CompressedDescriptionHtml = null
                    };
                }
                catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException)
                {
                    migrationRequired = true;
                    _logger.LogWarning(ex,
                        "A cached compressed job description was invalid and will be retrieved again when needed.");
                    job = job with
                    {
                        DescriptionHtml = "",
                        CompressedDescriptionHtml = null,
                        DetailError = "The cached description was invalid and must be retrieved again."
                    };
                }
            }
            if (string.IsNullOrWhiteSpace(job.SourceUrl) &&
                !string.IsNullOrWhiteSpace(job.LegacySourceUrl))
            {
                migrationRequired = true;
                return job with { SourceUrl = job.LegacySourceUrl, LegacySourceUrl = null };
            }

            if (job.LegacySourceUrl is not null)
            {
                migrationRequired = true;
                return job with { LegacySourceUrl = null };
            }

            return job;
        }).ToArray();

        return (jobs, migrationRequired);
    }

    internal async Task SaveJobsCacheAsync(
        IReadOnlyList<JobRecord> jobs,
        DateTimeOffset lastRefreshedUtc,
        int detailFailureCount,
        JobSourceQuery query)
    {
        await SaveCacheDocumentAsync(new JobsCacheDocument(
            CacheSchemaVersion,
            DateTimeOffset.UtcNow,
            lastRefreshedUtc,
            detailFailureCount,
            jobs,
            query));
    }

    private async Task SaveCacheDocumentAsync(JobsCacheDocument cache)
    {
        if (cache.Query is null)
        {
            throw new InvalidOperationException("A source cache must include its normalized query.");
        }
        var company = _companyCatalog.Get(cache.Query.CompanyId);
        var query = cache.Query.Normalize(company);
        cache = PrepareCacheForStorage(cache, query);
        await _dataStore.WriteCompanyCacheJsonAsync(
            company.Id,
            QueryFingerprint(query),
            cache);
    }

    private static JobsCacheDocument PrepareCacheForStorage(
        JobsCacheDocument cache,
        JobSourceQuery query) => cache with
    {
        SchemaVersion = CacheSchemaVersion,
        Query = query,
        Jobs = cache.Jobs.Select(job => string.IsNullOrWhiteSpace(job.DescriptionHtml)
            ? job
            : job with
            {
                DescriptionHtml = "",
                CompressedDescriptionHtml = Compress(job.DescriptionHtml)
            }).ToArray()
    };

    internal async Task<SourceStatusDocument?> LoadSourceStatusAsync(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        await EnsureSplitCacheMigrationAsync(query);
        return await _dataStore.ReadSourceStatusJsonAsync<SourceStatusDocument>(
            company.Id, QueryFingerprint(query));
    }

    internal async Task SaveSourceStatusAsync(
        JobSourceQuery query,
        DateTimeOffset lastSuccessfulRefreshUtc,
        int detailFailureCount,
        RefreshMetrics? metrics)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        await _dataStore.WriteSourceStatusJsonAsync(
            company.Id,
            QueryFingerprint(query),
            new SourceStatusDocument(
                1,
                query,
                lastSuccessfulRefreshUtc,
                detailFailureCount,
                metrics?.ListingsTruncated ?? false));
    }

    private async Task EnsureSplitCacheMigrationAsync(JobSourceQuery fallbackQuery)
    {
        if (_cacheMigrationChecked) return;
        await _cacheMigrationGate.WaitAsync();
        try
        {
            if (_cacheMigrationChecked) return;
            if (!await _dataStore.ExistsAsync(WorkspaceDataFile.JobsCache))
            {
                _cacheMigrationChecked = true;
                return;
            }

            var envelope = await _dataStore.ReadJsonAsync<JobsCacheEnvelope>(WorkspaceDataFile.JobsCache);
            var sources = envelope?.Sources is { Count: > 0 }
                ? envelope.Sources.ToArray()
                : [];
            if (sources.Length == 0)
            {
                var legacy = await _dataStore.ReadJsonAsync<JobsCacheDocument>(WorkspaceDataFile.JobsCache);
                if (legacy?.Jobs is not null)
                {
                    var query = legacy.Query ?? fallbackQuery;
                    sources = [new KeyValuePair<string, JobsCacheDocument>(query.CompanyId, legacy with
                    {
                        Query = query
                    })];
                }
            }

            var allMigrated = sources.Length > 0;
            foreach (var source in sources)
            {
                if (!_companyCatalog.TryGet(source.Key, out var company) ||
                    source.Value.Query is null || source.Value.Jobs is null)
                {
                    allMigrated = false;
                    continue;
                }
                var query = (source.Value.Query with { CompanyId = company.Id }).Normalize(company);
                var fingerprint = QueryFingerprint(query);
                if (!await _dataStore.CompanyCacheExistsAsync(company.Id, fingerprint))
                {
                    await _dataStore.WriteCompanyCacheJsonAsync(
                        company.Id,
                        fingerprint,
                        PrepareCacheForStorage(source.Value, query));
                }
                var status = await _dataStore.ReadSourceStatusJsonAsync<SourceStatusDocument>(
                    company.Id, fingerprint);
                if (status is null)
                {
                    await _dataStore.WriteSourceStatusJsonAsync(
                        company.Id,
                        fingerprint,
                        new SourceStatusDocument(
                            1,
                            query,
                            source.Value.LastRefreshedUtc ?? source.Value.SavedAtUtc,
                            source.Value.DetailFailureCount,
                            false));
                }
            }

            if (allMigrated)
            {
                await _dataStore.DeleteAsync(WorkspaceDataFile.JobsCache);
                _logger.LogInformation(
                    "Migrated the legacy cumulative job cache into {SourceCount} isolated company/query documents and retired {LegacyCachePath}.",
                    sources.Length,
                    JobsCachePath);
            }
            else
            {
                _logger.LogWarning(
                    "The legacy cumulative job cache was retained because one or more entries could not be safely migrated.");
            }
            _cacheMigrationChecked = true;
        }
        finally
        {
            _cacheMigrationGate.Release();
        }
    }

    private static string Compress(string value)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    private static string Decompress(string value)
    {
        var bytes = Convert.FromBase64String(value);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal async Task<JobHistoryDocument> LoadJobHistoryAsync()
    {
        var history = await _dataStore.ReadJsonAsync<JobHistoryDocument>(WorkspaceDataFile.JobHistory);
        if (history?.Jobs is null)
        {
            return JobHistoryDocument.Empty with
            {
                Jobs = new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal)
            };
        }

        var migrated = new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal);
        var migrationRequired = history.SchemaVersion < SchemaVersion;
        foreach (var pair in history.Jobs)
        {
            var storedCompanyId = string.IsNullOrWhiteSpace(pair.Value.CompanyId)
                ? CompanyCatalog.DefaultCompanyId
                : pair.Value.CompanyId.Trim();
            var companyId = _companyCatalog.TryGet(storedCompanyId, out var entryCompany)
                ? entryCompany.Id
                : storedCompanyId;
            var keyHasStoredCompany = pair.Key.StartsWith(
                companyId + ":", StringComparison.OrdinalIgnoreCase);
            var keyHasSupportedCompany = _companyCatalog.Companies.Any(company =>
                pair.Key.StartsWith(company.Id + ":", StringComparison.OrdinalIgnoreCase));
            var key = keyHasStoredCompany || keyHasSupportedCompany
                ? pair.Key
                : $"{companyId}:{pair.Key}";
            var hasLegacyState = pair.Value.Dismissed || pair.Value.Saved || pair.Value.Applied;
            var hasLegacyStateData = hasLegacyState || pair.Value.DismissedAt is not null ||
                pair.Value.SavedAt is not null;
            var workflowState = hasLegacyState
                ? pair.Value.Dismissed
                    ? JobWorkflowStates.Hidden
                    : pair.Value.Applied
                        ? JobWorkflowStates.Applied
                        : JobWorkflowStates.Saved
                : JobWorkflowStates.Normalize(pair.Value.WorkflowState);
            var workflowStateChangedAt = hasLegacyState
                ? workflowState switch
                {
                    JobWorkflowStates.Hidden => pair.Value.DismissedAt,
                    JobWorkflowStates.Applied => pair.Value.AppliedAt,
                    JobWorkflowStates.Saved => pair.Value.SavedAt,
                    _ => null
                }
                : pair.Value.WorkflowStateChangedAt;
            var appliedAt = workflowState is JobWorkflowStates.Applied or JobWorkflowStates.Closed
                ? pair.Value.AppliedAt ?? workflowStateChangedAt
                : null;
            var validClosure = workflowState == JobWorkflowStates.Closed &&
                JobCloseReasons.IsValid(pair.Value.CloseReason) && pair.Value.ClosedAt is not null;
            if (workflowState == JobWorkflowStates.Closed && !validClosure)
            {
                workflowState = JobWorkflowStates.Applied;
                workflowStateChangedAt = appliedAt;
                migrationRequired = true;
            }
            migrationRequired |= !string.Equals(key, pair.Key, StringComparison.Ordinal) ||
                !string.Equals(pair.Value.CompanyId, companyId, StringComparison.OrdinalIgnoreCase) ||
                !JobWorkflowStates.IsValid(pair.Value.WorkflowState) ||
                hasLegacyStateData;
            migrated[key] = pair.Value with
            {
                CompanyId = companyId,
                WorkflowState = workflowState,
                WorkflowStateChangedAt = workflowStateChangedAt,
                Dismissed = false,
                DismissedAt = null,
                Saved = false,
                SavedAt = null,
                Applied = false,
                AppliedAt = appliedAt,
                CloseReason = validClosure ? pair.Value.CloseReason : null,
                ClosedAt = validClosure ? pair.Value.ClosedAt : null
            };
        }

        var migratedDocument = new JobHistoryDocument(SchemaVersion, migrated);
        if (migrationRequired)
        {
            await SaveJobHistoryAsync(migratedDocument);
            _logger.LogInformation(
                "Migrated {HistoryPath} to workflow history schema {SchemaVersion}.",
                JobHistoryPath,
                SchemaVersion);
        }

        return migratedDocument;
    }

    internal Task SaveJobHistoryAsync(JobHistoryDocument history) =>
        _dataStore.WriteJsonAsync(
            WorkspaceDataFile.JobHistory,
            history with { SchemaVersion = SchemaVersion });

    public ViewerSettings NormalizeSettings(ViewerSettings settings)
    {
        static string[] NormalizeTerms(IReadOnlyList<string>? terms) => (terms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();

        var scope = settings.KeywordScope is "metadata" or "description"
            ? settings.KeywordScope
            : "metadata";
        var locationMode = settings.LocationMode is "all" or "hide-restricted" or "only-restricted"
            ? settings.LocationMode
            : "all";
        var collapsed = (settings.CollapsedAgeGroups ?? new Dictionary<string, bool>())
            .Where(pair => pair.Value)
            .ToDictionary(pair => pair.Key, pair => true, StringComparer.Ordinal);
        var hasConfiguredSource = settings.HasConfiguredSource ?? true;
        CompanyDefinition? selectedCompany = null;
        var activeCompanyIsSupported = hasConfiguredSource &&
            _companyCatalog.TryGet(settings.CompanyId, out selectedCompany);
        var company = selectedCompany ?? _companyCatalog.Get(CompanyCatalog.DefaultCompanyId);

        FacetSelection country;
        bool includeAllLocations;
        bool includeRemote;
        IEnumerable<FacetSelection> selectedPhysicalLocations;
        if (!hasConfiguredSource)
        {
            country = NormalizeFacetSelection(
                settings.Country,
                FacetDefaults.UnitedStatesCountry,
                FacetDefaults.AllCountriesLabel);
            includeAllLocations = false;
            includeRemote = false;
            selectedPhysicalLocations = [];
        }
        else if (!activeCompanyIsSupported)
        {
            var safeSource = settings.CompanySources?.TryGetValue(company.Id, out var previousSource) == true
                ? NormalizeCompanySource(previousSource, company)
                : new CompanySourceSettings(
                    company.DefaultCountry,
                    true,
                    company.RemoteLocationIds.Count > 0,
                    []);
            country = safeSource.Country;
            includeAllLocations = safeSource.IncludeAllLocations;
            includeRemote = safeSource.IncludeRemote;
            selectedPhysicalLocations = safeSource.SelectedPhysicalLocations;
        }
        else
        {
            country = NormalizeFacetSelection(
                settings.Country,
                company.DefaultCountry,
                FacetDefaults.AllCountriesLabel);
            includeAllLocations = settings.IncludeAllLocations;
            includeRemote = settings.IncludeRemote;
            selectedPhysicalLocations = settings.SelectedPhysicalLocations ?? [];
        }

        // Migrate the original single Location setting. A saved Remote/Teleworker
        // selection becomes remote-only; an empty legacy location preserves the
        // previous country-wide query; any other saved location becomes the sole
        // selected physical location.
        if (hasConfiguredSource && activeCompanyIsSupported && settings.Location is not null)
        {
            if (string.IsNullOrWhiteSpace(settings.Location.Id))
            {
                includeAllLocations = true;
                includeRemote = true;
                selectedPhysicalLocations = [];
            }
            else if (company.IsRemoteLocation(settings.Location.Id))
            {
                includeAllLocations = false;
                includeRemote = true;
                selectedPhysicalLocations = [];
            }
            else
            {
                includeAllLocations = false;
                includeRemote = false;
                selectedPhysicalLocations = [settings.Location];
            }
        }

        includeRemote = hasConfiguredSource && company.RemoteLocationIds.Count > 0 &&
            (includeAllLocations || includeRemote);
        var physicalLocations = includeAllLocations
            ? Array.Empty<FacetSelection>()
            : selectedPhysicalLocations
                .Where(location => !string.IsNullOrWhiteSpace(location?.Id) &&
                    !company.IsRemoteLocation(location.Id))
                .Select(location => new FacetSelection(
                    location.Id!.Trim(),
                    string.IsNullOrWhiteSpace(location.Label) ? location.Id.Trim() : location.Label.Trim()))
                .GroupBy(location => location.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(location => location.Id, StringComparer.Ordinal)
                .ToArray();
        var themeMode = ThemeModes.Normalize(settings.ThemeMode);
        var educationLevel = settings.UserProfile?.Education?.Level is
            "notSpecified" or "noCredential" or "ged" or "highSchool" or "associate" or
            "bachelor" or "master" or "doctorate"
                ? settings.UserProfile.Education.Level
                : "notSpecified";
        var doctorateType = educationLevel == "doctorate" &&
            settings.UserProfile?.Education?.DoctorateType == "phD"
                ? "phD"
                : null;
        var clearanceLevel = settings.UserProfile?.Security?.ClearanceLevel is
            "notSpecified" or "none" or "secret" or "topSecret" or "topSecretSCI" or "otherUnknown"
                ? settings.UserProfile.Security.ClearanceLevel
                : "notSpecified";
        var publicTrust = settings.UserProfile?.Security?.PublicTrust switch
        {
            "none" => "none",
            "current" => "current",
            // Migrate the original application-centric value without changing
            // its conservative unknown-status behavior.
            "notSpecified" or "unknown" => "unknown",
            _ => "unknown"
        };
        var usWorkAuthorizationStatus = settings.UserProfile?.WorkAuthorization?.UsStatus is
            "notSpecified" or "usCitizen" or "permanentResident" or "otherAuthorized" or "notAuthorized"
                ? settings.UserProfile.WorkAuthorization.UsStatus
                : "notSpecified";
        var sponsorship = settings.UserProfile?.WorkAuthorization?.Sponsorship is
            "unknown" or "notRequired" or "required"
                ? settings.UserProfile.WorkAuthorization.Sponsorship
                : "unknown";
        var credentialInventoryStatus = settings.UserProfile?.Credentials?.InventoryStatus is
            "none" or "complete"
                ? settings.UserProfile.Credentials.InventoryStatus
                : "notConfigured";
        var heldCredentialIds = credentialInventoryStatus == "complete"
            ? (settings.UserProfile?.Credentials?.HeldCredentialIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 100 &&
                    id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                .Select(id => id.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(200)
                .ToArray()
            : [];
        var userProfile = new UserProfile(
            new EducationProfile(educationLevel, doctorateType),
            new SecurityProfile(clearanceLevel, publicTrust),
            new WorkAuthorizationProfile(usWorkAuthorizationStatus, sponsorship),
            new CredentialProfile(credentialInventoryStatus, heldCredentialIds));
        var jobFit = JobFitConfiguration.Normalize(settings.JobFit, _jobConceptCatalog);

        var companySources = new Dictionary<string, CompanySourceSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in settings.CompanySources ??
            new Dictionary<string, CompanySourceSettings>(StringComparer.OrdinalIgnoreCase))
        {
            if (!_companyCatalog.TryGet(pair.Key, out var sourceCompany))
            {
                continue;
            }

            companySources[sourceCompany.Id] = NormalizeCompanySource(pair.Value, sourceCompany);
        }
        if (hasConfiguredSource)
        {
            companySources[company.Id] = new CompanySourceSettings(
                country,
                includeAllLocations,
                includeRemote,
                physicalLocations);
        }

        PendingJobSource? pendingSource = null;
        if (settings.PendingSource is { } pending &&
            _companyCatalog.TryGet(pending.CompanyId, out var pendingCompany))
        {
            pendingSource = new PendingJobSource(
                pendingCompany.Id,
                NormalizeCompanySource(pending.Source, pendingCompany));
        }

        return new ViewerSettings(
            NormalizeTerms(settings.IncludeKeywords),
            NormalizeTerms(settings.ExcludeKeywords),
            settings.MinimumSalary is >= 0 ? settings.MinimumSalary : null,
            scope,
            locationMode,
            settings.HighlightIncludeKeywords,
            collapsed,
            country,
            null,
            settings.SearchFiltersCollapsed,
            themeMode,
            userProfile,
            settings.HideStrictEducationMismatch,
            settings.HideStrictClearanceMismatch,
            includeAllLocations,
            includeRemote,
            physicalLocations,
            hasConfiguredSource ? company.Id : "",
            companySources,
            settings.HideStrictWorkAuthorizationMismatch,
            hasConfiguredSource,
            pendingSource,
            settings.ExcludeStrongExtendedLocationRequirements,
            jobFit);
    }

    public CompanySourceSettings GetSourceSettings(ViewerSettings settings, string companyId)
    {
        var company = _companyCatalog.Get(companyId);
        var normalized = NormalizeSettings(settings);
        if (normalized.CompanySources?.TryGetValue(company.Id, out var source) == true)
        {
            return NormalizeCompanySource(source, company);
        }

        return new CompanySourceSettings(
            company.DefaultCountry,
            true,
            company.RemoteLocationIds.Count > 0,
            []);
    }

    private static CompanySourceSettings NormalizeCompanySource(
        CompanySourceSettings source,
        CompanyDefinition company)
    {
        var country = NormalizeFacetSelection(
            source.Country,
            company.DefaultCountry,
            FacetDefaults.AllCountriesLabel);
        var includeAll = source.IncludeAllLocations;
        var includeRemote = company.RemoteLocationIds.Count > 0 &&
            (includeAll || source.IncludeRemote);
        var physical = includeAll
            ? Array.Empty<FacetSelection>()
            : (source.SelectedPhysicalLocations ?? [])
                .Where(location => !string.IsNullOrWhiteSpace(location?.Id) &&
                    !company.IsRemoteLocation(location.Id))
                .Select(location => new FacetSelection(
                    location.Id!.Trim(),
                    string.IsNullOrWhiteSpace(location.Label) ? location.Id.Trim() : location.Label.Trim()))
                .GroupBy(location => location.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(location => location.Id, StringComparer.Ordinal)
                .ToArray();
        return new CompanySourceSettings(country, includeAll, includeRemote, physical);
    }

    private static FacetSelection NormalizeFacetSelection(
        FacetSelection? selection,
        FacetSelection defaultSelection,
        string allLabel)
    {
        if (selection is null)
        {
            return defaultSelection;
        }

        if (string.IsNullOrWhiteSpace(selection.Id))
        {
            if (string.IsNullOrWhiteSpace(selection.Label))
            {
                return defaultSelection;
            }
            return new FacetSelection(null, allLabel);
        }

        return new FacetSelection(
            selection.Id.Trim(),
            string.IsNullOrWhiteSpace(selection.Label) ? selection.Id.Trim() : selection.Label.Trim());
    }
}
