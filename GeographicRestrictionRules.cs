using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class GeographicRestrictionRules
{
    private static readonly Lazy<GeographicRestrictionRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "geographic-restriction-v1.json")));
    internal static GeographicRestrictionRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal IReadOnlyList<RuleDefinition> OrderedRules { get; }
    internal static GeographicRestrictionRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load geographic-restriction rules '{path}': {ex.Message}", ex); }
    }
    internal static GeographicRestrictionRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Geographic-restriction rules must be an object.");
            return new GeographicRestrictionRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid geographic-restriction rules: {ex.Message}", ex); }
    }
    private GeographicRestrictionRules(Definition definition, byte[] bytes)
    {
        Rules = definition; Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid geographic-restriction rules: {message}"); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "invalid rulesetVersion.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "invalid finite regex timeout.");
        Require(definition.Patterns.Length is >= 1 and <= 128 && definition.Patterns.All(p => p is not null), "invalid patterns.");
        foreach (var p in definition.Patterns)
        {
            Require(!string.IsNullOrWhiteSpace(p.Id) && p.Id.Length <= 300 && !patterns.ContainsKey(p.Id), "empty or duplicate pattern ID.");
            Require(p.Pattern.Length is >= 1 and <= 8192, "invalid pattern length.");
            patterns.Add(p.Id, new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds)));
        }
        var cues = definition.RemoteDesignationCues;
        Require(new[] { cues.PrimaryLocation, cues.AdditionalLocation, cues.Description }.All(c => !string.IsNullOrWhiteSpace(c) && c.Length <= 300), "invalid remote designation cue.");
        Require(definition.Rules.Length is >= 1 and <= 128 && definition.Rules.All(r => r is not null), "invalid rules.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var priorities = new HashSet<int>();
        var categories = new[] { "distance-radius", "commuting-distance", "hybrid-local", "required-region", "regional-preference" };
        foreach (var rule in definition.Rules)
        {
            Require(!string.IsNullOrWhiteSpace(rule.Id) && rule.Id.Length <= 300 && ids.Add(rule.Id), "empty or duplicate rule ID.");
            Require(rule.Priority is >= 0 and <= 10000 && priorities.Add(rule.Priority), "invalid or duplicate priority.");
            Require(categories.Contains(rule.Category, StringComparer.Ordinal), "unknown category mapping.");
            Require(patterns.ContainsKey(rule.PatternId) && rule.UnlessPatternIds.Length <= 128 && rule.UnlessPatternIds.All(id => id is not null && patterns.ContainsKey(id)), "unknown pattern reference.");
        }
        OrderedRules = definition.Rules.OrderBy(r => r.Priority).ToArray();
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, RemoteCues RemoteDesignationCues, RuleDefinition[] Rules);
    internal sealed record RemoteCues(string PrimaryLocation, string AdditionalLocation, string Description);
    internal sealed record RuleDefinition(string Id, int Priority, string PatternId, string Category, string[] UnlessPatternIds);
    internal sealed record PatternDefinition(string Id, string Pattern);
}
