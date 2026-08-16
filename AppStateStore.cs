namespace WorkdayJobManager;

public sealed class AppStateStore
{
    private const int SchemaVersion = 2;
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
        var hadUnsupportedActiveCompany = !_companyCatalog.TryGet(stored.CompanyId, out _);
        var hadUnsupportedCompanySource = (stored.CompanySources ??
            new Dictionary<string, CompanySourceSettings>())
            .Keys.Any(companyId => !_companyCatalog.TryGet(companyId, out _));
        var normalized = NormalizeSettings(stored);

        if (hadUnsupportedActiveCompany || hadUnsupportedCompanySource)
        {
            await _dataStore.WriteJsonAsync(WorkspaceDataFile.Settings, normalized);
            _logger.LogInformation(
                "Migrated unsupported Workday company state in {SettingsPath} to supported company {CompanyId}; unsupported per-company source selections were removed.",
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

    internal Task<JobsCacheDocument?> LoadJobsCacheAsync() =>
        _dataStore.ReadJsonAsync<JobsCacheDocument>(WorkspaceDataFile.JobsCache);

    internal Task SaveJobsCacheAsync(
        IReadOnlyList<JobRecord> jobs,
        DateTimeOffset lastRefreshedUtc,
        int detailFailureCount,
        WorkdayQuery query) =>
        _dataStore.WriteJsonAsync(
            WorkspaceDataFile.JobsCache,
            new JobsCacheDocument(
                SchemaVersion,
                DateTimeOffset.UtcNow,
                lastRefreshedUtc,
                detailFailureCount,
                jobs,
                query));

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
            migrationRequired |= !string.Equals(key, pair.Key, StringComparison.Ordinal) ||
                !string.Equals(pair.Value.CompanyId, companyId, StringComparison.OrdinalIgnoreCase);
            migrated[key] = pair.Value with { CompanyId = companyId };
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
        var activeCompanyIsSupported = _companyCatalog.TryGet(settings.CompanyId, out var company);
        company ??= _companyCatalog.Get(CompanyCatalog.DefaultCompanyId);

        FacetSelection country;
        bool includeAllLocations;
        bool includeRemote;
        IEnumerable<FacetSelection> selectedPhysicalLocations;
        if (!activeCompanyIsSupported)
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
        if (activeCompanyIsSupported && settings.Location is not null)
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

        includeRemote = company.RemoteLocationIds.Count > 0 &&
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
        companySources[company.Id] = new CompanySourceSettings(
            country,
            includeAllLocations,
            includeRemote,
            physicalLocations);

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
            company.Id,
            companySources,
            settings.HideStrictWorkAuthorizationMismatch);
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
