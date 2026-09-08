using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class ClearanceRules
{
    private static readonly Lazy<ClearanceRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "clearance-v1.json")));
    internal static ClearanceRules Default => Loaded.Value;
    internal string Version => Definition.RulesetVersion;
    internal string Fingerprint { get; }
    internal RuleSet Definition { get; }
    private readonly Dictionary<string, Regex> regexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PatternDefinition> patterns;
    internal string Literal(string id) => patterns[id].Pattern;
    internal Regex Regex(string id) => regexes[id];

    internal static ClearanceRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new InvalidDataException($"Cannot load clearance rules '{path}': {ex.Message}", ex); }
    }

    internal static ClearanceRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<RuleSet>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true })
                ?? throw new InvalidDataException("Clearance rules must be an object.");
            return new ClearanceRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid clearance rules: {ex.Message}", ex); }
    }

    private ClearanceRules(RuleSet definition, byte[] bytes)
    {
        Definition = definition;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException($"Invalid clearance rules: {message}"); }
        Require(definition.SchemaVersion == 1, "schemaVersion must be 1.");
        Require(System.Version.TryParse(definition.RulesetVersion, out _), "rulesetVersion must be a version number.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "regexTimeoutMilliseconds must be 1..1000.");
        Require(definition.RegexOptions.SequenceEqual(new[] { "IgnoreCase", "CultureInvariant" }), "regexOptions must be IgnoreCase, CultureInvariant.");
        Require(definition.Patterns.Length is > 0 and <= 64, "patterns must contain 1..64 entries.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), $"empty or duplicate rule ID '{id}'.");
        foreach (var pattern in definition.Patterns)
        {
            Require(pattern is not null, "null pattern.");
            Id(pattern!.Id);
            Require(pattern.Pattern.Length is > 0 and <= 8192, $"pattern '{pattern.Id}' length must be 1..8192.");
            Require(pattern.Type is "regex" or "literal", $"unknown pattern type '{pattern.Type}'.");
            Require(pattern.Type == "literal" ? pattern.Scope == "sectionPrefix" : pattern.Scope is "normalizedText" or "clearanceContext" or "textOrContext", $"invalid scope for '{pattern.Id}'.");
            if (pattern.Type == "regex")
            {
                try { regexes.Add(pattern.Id, new Regex(pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds))); }
                catch (ArgumentException ex) { throw new InvalidDataException($"Invalid clearance regex '{pattern.Id}': {ex.Message}", ex); }
            }
        }
        patterns = definition.Patterns.ToDictionary(p => p.Id, StringComparer.Ordinal);
        void Reference(string id, string scope)
        {
            Require(patterns.TryGetValue(id, out var p) && (p.Scope == scope || p.Scope == "textOrContext" && scope != "sectionPrefix"), $"unknown or incorrectly scoped pattern '{id}' for {scope}.");
        }
        bool Level(string value) => value is "topSecretSCI" or "topSecret" or "publicTrust" or "secret" or "other";
        Reference(definition.NegationPatternId, "normalizedText");
        Reference(definition.ContextPatternId, "normalizedText");
        Reference(definition.PolygraphPatternId, "normalizedText");
        Require(definition.Sections.LookbehindCharacters is >= 1 and <= 10000, "section lookbehind must be 1..10000.");
        foreach (var markers in new[] { definition.Sections.PreferredMarkerIds, definition.Sections.ResetMarkerIds })
        {
            Require(markers.Length is > 0 and <= 64, "section markers must contain 1..64 entries.");
            foreach (var id in markers) Reference(id, "sectionPrefix");
        }
        Require(definition.LevelRules.Length is > 0 and <= 64 && definition.RequirementRules.Length is > 0 and <= 64 && definition.LevelOverrides.Length <= 64, "invalid rule counts.");
        var priorities = new HashSet<int>();
        var levels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in definition.LevelRules)
        {
            Require(rule is not null, "null level rule."); Id(rule!.Id); Reference(rule.PatternId, "normalizedText");
            Require(Level(rule.Result) && levels.Add(rule.Result), $"unknown or duplicate level mapping '{rule.Result}'.");
            Require(priorities.Add(rule.Priority), "duplicate level priority.");
        }
        Require(levels.Contains("other"), "level mapping 'other' is required for fallback evidence.");
        priorities.Clear();
        foreach (var rule in definition.RequirementRules)
        {
            Require(rule is not null, "null requirement rule."); Id(rule!.Id); Reference(rule.PatternId, "clearanceContext");
            Require(rule.Result is "activeRequired" or "eligible" or "publicTrustSuitability" or "mustPossess" or "obtainAndMaintain" or "obtain" or "maintain" or "preferred", $"unknown requirement mapping '{rule.Result}'.");
            Require(rule.Level is null || Level(rule.Level), $"unknown level '{rule.Level}'.");
            Require(priorities.Add(rule.Priority), "duplicate requirement priority.");
            foreach (var id in rule.UnlessPatternIds) Reference(id, "clearanceContext");
        }
        foreach (var rule in definition.LevelOverrides)
        {
            Require(rule is not null, "null override."); Id(rule!.Id); Reference(rule.PatternId, "normalizedText");
            Require(Level(rule.FromLevel) && levels.Contains(rule.Result), "unknown override level mapping.");
            foreach (var id in rule.UnlessPatternIds) Reference(id, "normalizedText");
        }
        Array.Sort(definition.LevelRules, (a, b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.RequirementRules, (a, b) => a.Priority.CompareTo(b.Priority));
    }

    internal sealed record RuleSet(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds, string[] RegexOptions,
        PatternDefinition[] Patterns, string NegationPatternId, string ContextPatternId, string PolygraphPatternId,
        Sections Sections, LevelRule[] LevelRules, RequirementRule[] RequirementRules, LevelOverride[] LevelOverrides);
    internal sealed record PatternDefinition(string Id, string Type, string Pattern, string Scope);
    internal sealed record Sections(int LookbehindCharacters, string[] PreferredMarkerIds, string[] ResetMarkerIds);
    internal sealed record LevelRule(string Id, string PatternId, int Priority, string Result);
    internal sealed record RequirementRule(string Id, string PatternId, int Priority, string Result, string? Level, string[] UnlessPatternIds);
    internal sealed record LevelOverride(string Id, string FromLevel, string Result, string PatternId, string[] UnlessPatternIds);
}
