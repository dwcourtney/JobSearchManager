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

    public async Task<ViewerSettings> LoadSettingsAsync() =>
        NormalizeSettings(await _dataStore.ReadJsonAsync<ViewerSettings>(WorkspaceDataFile.Settings) ??
            ViewerSettings.Default);

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
            var companyId = _companyCatalog.TryGet(pair.Value.CompanyId, out var entryCompany)
                ? entryCompany.Id
                : CompanyCatalog.DefaultCompanyId;
            var keyHasCompany = _companyCatalog.Companies.Any(company =>
                pair.Key.StartsWith(company.Id + ":", StringComparison.OrdinalIgnoreCase));
            var key = keyHasCompany ? pair.Key : $"{companyId}:{pair.Key}";
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
        var company = _companyCatalog.Get(settings.CompanyId);
        var country = NormalizeFacetSelection(
            settings.Country,
            company.DefaultCountry,
            FacetDefaults.AllCountriesLabel);
        var includeAllLocations = settings.IncludeAllLocations;
        var includeRemote = settings.IncludeRemote;
        IEnumerable<FacetSelection> selectedPhysicalLocations = settings.SelectedPhysicalLocations ?? [];

        // Migrate the original single Location setting. A saved Remote/Teleworker
        // selection becomes remote-only; an empty legacy location preserves the
        // previous country-wide query; any other saved location becomes the sole
        // selected physical location.
        if (settings.Location is not null)
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
        var userProfile = new UserProfile(
            new EducationProfile(educationLevel, doctorateType),
            new SecurityProfile(clearanceLevel, publicTrust));

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
            companySources);
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
