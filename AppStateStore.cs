using System.Text.Json;

namespace LeidosJobsViewer;

public sealed class AppStateStore
{
    private const int SchemaVersion = 1;
    private readonly ILogger<AppStateStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AppStateStore(ILogger<AppStateStore> logger)
    {
        _logger = logger;
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

        return history with
        {
            Jobs = new Dictionary<string, JobHistoryEntry>(history.Jobs, StringComparer.Ordinal)
        };
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

    public static ViewerSettings NormalizeSettings(ViewerSettings settings)
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
        var country = NormalizeFacetSelection(
            settings.Country,
            new FacetSelection(FacetDefaults.CountryId, FacetDefaults.CountryLabel),
            FacetDefaults.AllCountriesLabel);
        var location = NormalizeFacetSelection(
            settings.Location,
            new FacetSelection(FacetDefaults.LocationId, FacetDefaults.LocationLabel),
            FacetDefaults.AllLocationsLabel);
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
        var publicTrust = settings.UserProfile?.Security?.PublicTrust is
            "notSpecified" or "none" or "current"
                ? settings.UserProfile.Security.PublicTrust
                : "notSpecified";
        var userProfile = new UserProfile(
            new EducationProfile(educationLevel, doctorateType),
            new SecurityProfile(clearanceLevel, publicTrust));

        return new ViewerSettings(
            NormalizeTerms(settings.IncludeKeywords),
            NormalizeTerms(settings.ExcludeKeywords),
            settings.MinimumSalary is >= 0 ? settings.MinimumSalary : null,
            scope,
            locationMode,
            settings.HighlightIncludeKeywords,
            collapsed,
            country,
            location,
            settings.SearchFiltersCollapsed,
            settings.AutomaticCheckEnabled ?? true,
            automaticCheckInterval,
            themeMode,
            userProfile,
            settings.HideStrictEducationMismatch,
            settings.HideStrictClearanceMismatch);
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
            return new FacetSelection(null, allLabel);
        }

        return new FacetSelection(
            selection.Id.Trim(),
            string.IsNullOrWhiteSpace(selection.Label) ? selection.Id.Trim() : selection.Label.Trim());
    }
}
