using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class FactualObservationRules
{
    private static readonly Lazy<FactualObservationRules> Loaded = new(() => Load(
        Path.Combine(AppContext.BaseDirectory, "rules", "factual-observations-v1.json")));
    internal static FactualObservationRules Default => Loaded.Value;
    internal sealed record PatternDefinition(string Id, string Pattern);
    internal sealed record Definition(int SchemaVersion, string Version, int TimeoutMilliseconds, PatternDefinition[] Patterns);
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal string Fingerprint { get; }
    internal static FactualObservationRules Load(string path) => new(File.ReadAllText(path));
    internal FactualObservationRules(string json)
    {
        var d = JsonSerializer.Deserialize<Definition>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
        }) ?? throw new InvalidDataException("Missing observation rules.");
        if (d.SchemaVersion != 1 || d.Version != "1.0.0" || d.TimeoutMilliseconds is < 1 or > 250)
            throw new InvalidDataException("Invalid observation schema/version/timeout.");
        Fingerprint = FactObservations.Hash(json);
        foreach (var p in d.Patterns)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || p.Pattern.Length is < 1 or > 8192 || patterns.ContainsKey(p.Id))
                throw new InvalidDataException("Invalid/duplicate observation pattern.");
            patterns.Add(p.Id, new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(d.TimeoutMilliseconds)));
        }
        string[] required = ["sponsorship-available", "preferred", "required", "optional", "conditional",
            "prohibited", "or", "and", "geography-heading", "job-level", "employment-type", "temporal",
            "money", "pay-context", "currency", "hourly", "annual", "monthly", "weekly", "bonus", "base", "total",
            "amount-range", "unknown-range", "scope-heading", "boilerplate", "not-required", "sponsorship-unavailable", "permanent-residency"];
        if (required.Any(id => !patterns.ContainsKey(id))) throw new InvalidDataException("Missing observation pattern.");
    }
    internal Regex Pattern(string id) => patterns[id];
    internal string Obligation(string text, string? section = null) => Pattern("not-required").IsMatch(text) ? "not-required" : Pattern("prohibited").IsMatch(text) ? "prohibited" :
        Pattern("preferred").IsMatch(text) || section == "preferred" ? "preferred" :
        Pattern("optional").IsMatch(text) ? "optional" : Pattern("required").IsMatch(text) || section == "required" ? "required" : "unknown";
    internal string Connective(string text) => Pattern("or").IsMatch(text) ? "or" : Pattern("and").IsMatch(text) ? "and" : "unspecified";
    internal FactScope Scope(string text, string? section = null, string? geography = null) => new(section,
        geography, Capture("temporal", text), Capture("job-level", text), Capture("employment-type", text), text);
    internal string? Capture(string id, string text)
    { var match = Pattern(id).Match(text); return match.Success ? match.Value : null; }
}
