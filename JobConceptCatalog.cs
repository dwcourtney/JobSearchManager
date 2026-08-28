using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record JobConceptDefinition(
    string Id,
    string DisplayName,
    string Category,
    IReadOnlyList<string>? EvidencePatterns = null,
    bool RemoteDesignation = false,
    IReadOnlyList<string>? RemoteSignalCategories = null,
    IReadOnlyList<string>? ExtendedLocationCategories = null,
    IReadOnlyList<string>? Supersedes = null);

public sealed record JobConceptOption(
    string Id,
    string DisplayName,
    string Category,
    IReadOnlyList<string> Supersedes);

internal sealed record JobConceptCatalogDocument(
    int Version,
    IReadOnlyList<JobConceptDefinition> Concepts);

public sealed class JobConceptCatalog
{
    private static readonly Regex IdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private readonly Dictionary<string, JobConceptDefinition> _byId;

    public JobConceptCatalog(IHostEnvironment environment)
        : this(LoadDocument(Path.Combine(environment.ContentRootPath, "JobConceptCatalog.json")))
    {
    }

    internal JobConceptCatalog(JobConceptCatalogDocument document)
    {
        if (document.Version < 1 || document.Concepts is null || document.Concepts.Count == 0)
        {
            throw new InvalidDataException("JobConceptCatalog.json is empty or has an invalid version.");
        }

        Version = document.Version;
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
    public IReadOnlyList<JobConceptDefinition> Concepts { get; }

    public IReadOnlyList<JobConceptOption> Options => Concepts
        .Select(concept => new JobConceptOption(
            concept.Id,
            concept.DisplayName,
            concept.Category,
            concept.Supersedes ?? []))
        .ToArray();

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    public JobConceptDefinition Get(string id) => _byId.TryGetValue(id, out var concept)
        ? concept
        : throw new KeyNotFoundException($"Unknown canonical job concept '{id}'.");

    internal static JobConceptCatalog LoadDefault() =>
        new(LoadDocument(Path.Combine(AppContext.BaseDirectory, "JobConceptCatalog.json")));

    private static JobConceptCatalogDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The canonical job concept catalog is missing.", path);
        }

        return JsonSerializer.Deserialize<JobConceptCatalogDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("JobConceptCatalog.json could not be read.");
    }

    private static void Validate(JobConceptDefinition concept)
    {
        if (string.IsNullOrWhiteSpace(concept.Id) || !IdPattern.IsMatch(concept.Id) ||
            string.IsNullOrWhiteSpace(concept.DisplayName) ||
            string.IsNullOrWhiteSpace(concept.Category))
        {
            throw new InvalidDataException("A canonical job concept has invalid identity or display metadata.");
        }

        var hasEvidence = concept.EvidencePatterns is { Count: > 0 } ||
            concept.RemoteDesignation ||
            concept.RemoteSignalCategories is { Count: > 0 } ||
            concept.ExtendedLocationCategories is { Count: > 0 };
        if (!hasEvidence)
        {
            throw new InvalidDataException($"Job concept '{concept.Id}' has no corpus evidence mapping.");
        }

        foreach (var pattern in concept.EvidencePatterns ?? [])
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

        if ((concept.Supersedes ?? []).Any(id =>
                string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id)))
        {
            throw new InvalidDataException(
                $"Job concept '{concept.Id}' contains an invalid superseded concept ID.");
        }
    }
}
