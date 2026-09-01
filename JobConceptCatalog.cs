using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace JobSearchManager;

public sealed record JobConceptDefinition(
    string Id,
    string DisplayName,
    string Category,
    string Definition,
    IReadOnlyList<string>? EvidencePatterns = null,
    IReadOnlyList<string>? TitleEvidencePatterns = null,
    IReadOnlyList<string>? TitleExclusionPatterns = null,
    bool RemoteDesignation = false,
    IReadOnlyList<string>? RemoteSignalCategories = null,
    IReadOnlyList<string>? ExtendedLocationCategories = null,
    IReadOnlyList<string>? Supersedes = null,
    bool UserConfigurable = true,
    int? TravelLevel = null,
    int? WorkLocationLevel = null,
    IReadOnlyList<JobConceptContextRule>? ContextRules = null);

public sealed record JobConceptContextRule(
    IReadOnlyList<string> RequiredPatterns);

public sealed record JobConceptOption(
    string Id,
    string DisplayName,
    string Category,
    string Definition,
    IReadOnlyList<string> Supersedes,
    bool UserConfigurable,
    int? TravelLevel,
    int? WorkLocationLevel);

internal sealed record JobConceptCatalogDocument(
    int Version,
    IReadOnlyList<JobConceptDefinition> Concepts);

public sealed class JobConceptCatalog
{
    private sealed record LoadedCatalog(JobConceptCatalogDocument Document, string Fingerprint);
    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private readonly Dictionary<string, JobConceptDefinition> _byId;

    public JobConceptCatalog(IHostEnvironment environment)
        : this(Load(Path.Combine(environment.ContentRootPath, "JobConceptCatalog.json")))
    {
    }

    internal JobConceptCatalog(JobConceptCatalogDocument document)
        : this(document, FingerprintFor(JsonSerializer.SerializeToUtf8Bytes(
            document, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
    {
    }

    private JobConceptCatalog(LoadedCatalog loaded)
        : this(loaded.Document, loaded.Fingerprint)
    {
    }

    private JobConceptCatalog(JobConceptCatalogDocument document, string fingerprint)
    {
        if (document.Version < 1 || document.Concepts is null || document.Concepts.Count == 0)
        {
            throw new InvalidDataException("JobConceptCatalog.json is empty or has an invalid version.");
        }

        Version = document.Version;
        Fingerprint = fingerprint;
        _byId = new Dictionary<string, JobConceptDefinition>(StringComparer.Ordinal);
        foreach (var concept in document.Concepts)
        {
            Validate(concept);
            if (!_byId.TryAdd(concept.Id, concept))
            {
                throw new InvalidDataException($"Duplicate job concept ID '{concept.Id}'.");
            }
        }
        foreach (var concept in _byId.Values)
        {
            foreach (var supersededId in concept.Supersedes ?? [])
            {
                if (supersededId == concept.Id || !_byId.ContainsKey(supersededId))
                {
                    throw new InvalidDataException(
                        $"Job concept '{concept.Id}' supersedes unknown or invalid concept '{supersededId}'.");
                }
            }
        }

        Concepts = _byId.Values
            .OrderBy(concept => concept.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(concept => concept.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int Version { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<JobConceptDefinition> Concepts { get; }

    public IReadOnlyList<JobConceptOption> Options => Concepts
        .Select(concept => new JobConceptOption(
            concept.Id,
            concept.DisplayName,
            concept.Category,
            concept.Definition,
            concept.Supersedes ?? [],
            concept.UserConfigurable,
            concept.TravelLevel,
            concept.WorkLocationLevel))
        .ToArray();

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    public JobConceptDefinition Get(string id) => _byId.TryGetValue(id, out var concept)
        ? concept
        : throw new KeyNotFoundException($"Unknown canonical job concept '{id}'.");

    internal static JobConceptCatalog LoadDefault() =>
        new(Load(Path.Combine(AppContext.BaseDirectory, "JobConceptCatalog.json")));

    private static LoadedCatalog Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new LoadedCatalog(Deserialize(bytes, path), FingerprintFor(bytes));
    }

    private static JobConceptCatalogDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The canonical job concept catalog is missing.", path);
        }

        return Deserialize(File.ReadAllBytes(path), path);
    }

    private static JobConceptCatalogDocument Deserialize(byte[] bytes, string path) =>
        JsonSerializer.Deserialize<JobConceptCatalogDocument>(
            bytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("JobConceptCatalog.json could not be read.");

    private static string FingerprintFor(byte[] bytes)
    {
        // Git may materialize the canonical JSON with CRLF on Windows and LF in Linux
        // containers. Line endings do not change the taxonomy and must not invalidate cache.
        var normalized = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
        return Convert.ToHexString(SHA256.HashData(normalized)).ToLowerInvariant();
    }

    private static void Validate(JobConceptDefinition concept)
    {
        if (string.IsNullOrWhiteSpace(concept.Id) || !IdPattern.IsMatch(concept.Id) ||
            string.IsNullOrWhiteSpace(concept.DisplayName) ||
            string.IsNullOrWhiteSpace(concept.Category) ||
            string.IsNullOrWhiteSpace(concept.Definition) ||
            concept.TravelLevel is < TravelTolerance.Minimum or > TravelTolerance.Maximum ||
            concept.WorkLocationLevel is < WorkLocationPreference.Minimum or > WorkLocationPreference.Maximum)
        {
            throw new InvalidDataException("A canonical job concept has invalid identity or display metadata.");
        }

        var hasEvidence = concept.EvidencePatterns is { Count: > 0 } ||
            concept.TitleEvidencePatterns is { Count: > 0 } ||
            concept.ContextRules is { Count: > 0 } ||
            concept.RemoteDesignation ||
            concept.RemoteSignalCategories is { Count: > 0 } ||
            concept.ExtendedLocationCategories is { Count: > 0 };
        if (!hasEvidence)
        {
            throw new InvalidDataException($"Job concept '{concept.Id}' has no corpus evidence mapping.");
        }

        foreach (var pattern in (concept.EvidencePatterns ?? [])
            .Concat(concept.TitleEvidencePatterns ?? [])
            .Concat(concept.TitleExclusionPatterns ?? [])
            .Concat((concept.ContextRules ?? []).SelectMany(rule => rule.RequiredPatterns ?? [])))
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(
                    $"Job concept '{concept.Id}' contains an invalid evidence pattern.", ex);
            }
        }

        if ((concept.ContextRules ?? []).Any(rule =>
                rule.RequiredPatterns is null || rule.RequiredPatterns.Count < 2 ||
                rule.RequiredPatterns.Any(string.IsNullOrWhiteSpace)))
        {
            throw new InvalidDataException(
                $"Job concept '{concept.Id}' contains an invalid contextual evidence rule.");
        }

        if ((concept.Supersedes ?? []).Any(id =>
                string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id)))
        {
            throw new InvalidDataException(
                $"Job concept '{concept.Id}' contains an invalid superseded concept ID.");
        }
    }
}
