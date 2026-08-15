using System.Text.Json;

namespace WorkdayJobManager;

public sealed class AppStateStore
{
    private const int SchemaVersion = 2;
    private readonly ILogger<AppStateStore> _logger;
    private readonly CompanyCatalog _companyCatalog;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AppStateStore(ILogger<AppStateStore> logger, CompanyCatalog companyCatalog)
    {
        _logger = logger;
        _companyCatalog = companyCatalog;
        DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        JobsCachePath = Path.Combine(DataDirectory, "jobs-cache.json");
        JobHistoryPath = Path.Combine(DataDirectory, "job-history.json");
        EnsureLocalDataDirectoryIsWritable();
    }

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public string JobsCachePath { get; }
    public string JobHistoryPath { get; }

    public async Task<ViewerSettings> LoadSettingsAsync() =>
        NormalizeSettings(await ReadJsonOrDefaultAsync<ViewerSettings>(SettingsPath) ?? ViewerSettings.Default);

    public Task SaveSettingsAsync(ViewerSettings settings) =>
        WriteJsonSafelyAsync(SettingsPath, NormalizeSettings(settings));

    public async Task EnsureSettingsFileAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            await SaveSettingsAsync(ViewerSettings.Default);
        }
    }

    internal Task<JobsCacheDocument?> LoadJobsCacheAsync() =>
        ReadJsonOrDefaultAsync<JobsCacheDocument>(JobsCachePath);

    internal Task SaveJobsCacheAsync(
        IReadOnlyList<JobRecord> jobs,
        DateTimeOffset lastRefreshedUtc,
        int detailFailureCount,
        WorkdayQuery query) =>
        WriteJsonSafelyAsync(
            JobsCachePath,
            new JobsCacheDocument(
                SchemaVersion,
                DateTimeOffset.UtcNow,
                lastRefreshedUtc,
                detailFailureCount,
                jobs,
                query));

    internal async Task<JobHistoryDocument> LoadJobHistoryAsync()
    {
        var history = await ReadJsonOrDefaultAsync<JobHistoryDocument>(JobHistoryPath);
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
        WriteJsonSafelyAsync(JobHistoryPath, history with { SchemaVersion = SchemaVersion });

    private async Task<T?> ReadJsonOrDefaultAsync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not read persistent state from {StatePath}. The file was left untouched and defaults will be used.",
                path);
            return default;
        }
    }

    private async Task WriteJsonSafelyAsync<T>(string path, T value)
    {
        await _writeGate.WaitAsync();
        var temporaryPath = Path.Combine(
            DataDirectory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Persistent state could not be saved in the application-local data directory '{DataDirectory}'. " +
                "Move the application to a writable directory or grant write access. No AppData fallback is used.",
                ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            _writeGate.Release();
        }
    }

    private void EnsureLocalDataDirectoryIsWritable()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var probePath = Path.Combine(DataDirectory, $".write-test-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.WriteThrough))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The application-local data directory '{DataDirectory}' is not writable. " +
                "Persistent state must be stored beside the application; no AppData fallback will be used.",
                ex);
        }
    }

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
