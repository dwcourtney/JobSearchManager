namespace JobSearchManager;

using System.IO.Compression;
using System.Text;

public sealed class AppStateStore
{
    private const int SchemaVersion = 4;
    private const int CacheSchemaVersion = 5;
    private readonly ILogger<AppStateStore> _logger;
    private readonly CompanyCatalog _companyCatalog;
    private readonly IWorkspaceDataStore _dataStore;

    public AppStateStore(
        ILogger<AppStateStore> logger,
        CompanyCatalog companyCatalog,
        IWorkspaceDataStore dataStore)
    {
        _logger = logger;
        _companyCatalog = companyCatalog;
        _dataStore = dataStore;
    }

    public string DataDirectory => _dataStore.Description;
    public string SettingsPath => _dataStore.Describe(WorkspaceDataFile.Settings);
    public string JobsCachePath => _dataStore.Describe(WorkspaceDataFile.JobsCache);
    public string JobHistoryPath => _dataStore.Describe(WorkspaceDataFile.JobHistory);

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

    internal async Task<JobsCacheDocument?> LoadJobsCacheAsync(string? companyId = null)
    {
        var envelope = await _dataStore.ReadJsonAsync<JobsCacheEnvelope>(WorkspaceDataFile.JobsCache);
        JobsCacheDocument? cache = null;
        if (envelope?.Sources is { Count: > 0 })
        {
            cache = !string.IsNullOrWhiteSpace(companyId) &&
                envelope.Sources.TryGetValue(companyId, out var companyCache)
                    ? companyCache
                    : envelope.Sources.Values.FirstOrDefault();
        }
        else
        {
            // Version 1-4 stored one source cache directly at jobs-cache.json.
            cache = await _dataStore.ReadJsonAsync<JobsCacheDocument>(WorkspaceDataFile.JobsCache);
        }
        if (cache?.Jobs is null)
        {
            return cache;
        }

        var migrationRequired = false;
        var jobs = cache.Jobs.Select(job =>
        {
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

        if (!migrationRequired)
        {
            return cache with { Jobs = jobs };
        }

        var migrated = cache with { SchemaVersion = CacheSchemaVersion, Jobs = jobs };
        await SaveCacheDocumentAsync(migrated);
        _logger.LogInformation(
            "Migrated cached job data in {JobsCachePath} to the current schema.",
            JobsCachePath);
        return migrated;
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
        cache = cache with
        {
            Jobs = cache.Jobs.Select(job => string.IsNullOrWhiteSpace(job.DescriptionHtml)
                ? job with { CompressedDescriptionHtml = null }
                : job with
                {
                    DescriptionHtml = "",
                    CompressedDescriptionHtml = Compress(job.DescriptionHtml)
                }).ToArray()
        };
        var stored = await _dataStore.ReadJsonAsync<JobsCacheEnvelope>(WorkspaceDataFile.JobsCache);
        var sources = stored?.Sources is { Count: > 0 }
            ? new Dictionary<string, JobsCacheDocument>(stored.Sources, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JobsCacheDocument>(StringComparer.OrdinalIgnoreCase);

        // Preserve a pre-envelope source when another company is saved first.
        if (sources.Count == 0)
        {
            var legacy = await _dataStore.ReadJsonAsync<JobsCacheDocument>(WorkspaceDataFile.JobsCache);
            if (legacy?.Query is not null && legacy.Jobs is not null)
            {
                sources[legacy.Query.CompanyId] = legacy;
            }
        }

        if (cache.Query is null)
        {
            throw new InvalidOperationException("A source cache must include its normalized query.");
        }
        sources[cache.Query.CompanyId] = cache;
        await _dataStore.WriteJsonAsync(
            WorkspaceDataFile.JobsCache,
            new JobsCacheEnvelope(CacheSchemaVersion, sources));
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
                pair.Value.SavedAt is not null || pair.Value.AppliedAt is not null;
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
                AppliedAt = null
            };
        }

        var migratedDocument = new JobHistoryDocument(SchemaVersion, migrated);
        if (migrationRequired)
        {
            await SaveJobHistoryAsync(migratedDocument);
            _logger.LogInformation(
                "Migrated {HistoryPath} to company-scoped history schema {SchemaVersion}.",
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
            ? []
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
        var automaticCheckInterval = settings.AutomaticCheckIntervalMinutes is 30 or 60 or 120 or 240 or 480
            ? settings.AutomaticCheckIntervalMinutes
            : 60;
        var themeMode = settings.ThemeMode is "light" or "dark"
            ? settings.ThemeMode
            : "light";
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
        var userProfile = new UserProfile(
            new EducationProfile(educationLevel, doctorateType),
            new SecurityProfile(clearanceLevel, publicTrust),
            new WorkAuthorizationProfile(usWorkAuthorizationStatus, sponsorship));

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
            settings.AutomaticCheckEnabled ?? true,
            automaticCheckInterval,
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
            pendingSource);
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
            ? []
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
