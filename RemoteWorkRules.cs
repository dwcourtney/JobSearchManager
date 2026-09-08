using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class RemoteWorkRules
{
    private static readonly Lazy<RemoteWorkRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "remote-work-v1.json")));
    internal static RemoteWorkRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal static RemoteWorkRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load remote-work rules '{path}': {ex.Message}", ex); }
    }
    internal static RemoteWorkRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Remote-work rules must be an object.");
            return new RemoteWorkRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid remote-work rules: {ex.Message}", ex); }
    }
    private RemoteWorkRules(Definition definition, byte[] bytes)
    {
        Rules = definition; Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid remote-work rules: {message}"); }
        void Count<T>(T[] values, string name)
        { Require(values.Length is >= 1 and <= 128 && values.All(v => v is not null), $"{name} must have 1..128 non-null entries."); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "invalid rulesetVersion.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "invalid finite regex timeout.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 300 && ids.Add(id), $"empty or duplicate ID '{id}'.");
        Count(definition.Patterns, "patterns");
        foreach (var p in definition.Patterns)
        {
            Id(p.Id); Require(p.Type == "regex", "unsupported pattern type.");
            Require(p.Scope is "metadata" or "sentence" or "plainText" or "html" or "evidence", "unsupported pattern scope.");
            Require(p.Pattern.Length is >= 1 and <= 8192, "invalid pattern length.");
            try { patterns.Add(p.Id, new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds))); }
            catch (ArgumentException ex) { throw new InvalidDataException($"Invalid remote-work regex '{p.Id}': {ex.Message}", ex); }
        }
        var definitions = definition.Patterns.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Reference(string id, string scope)
        { Require(definitions.TryGetValue(id, out var p) && p.Scope == scope, $"unknown or incorrectly scoped reference '{id}'."); used.Add(id); }
        Reference("BlockEndPattern", "html");
        Reference("CurrentObligationPattern", "sentence");
        Reference("ExplicitRemoteRolePattern", "plainText");
        Reference("HistoricalExperiencePattern", "sentence");
        Reference("RemoteDesignationPattern", "metadata");
        Reference("SentenceSplitPattern", "plainText");
        Reference("TravelPercentagePattern", "sentence");
        Reference("WhitespacePattern", "evidence");
        Require(patterns["TravelPercentagePattern"].GetGroupNames().Contains("minimum") && patterns["TravelPercentagePattern"].GetGroupNames().Contains("maximum"), "missing travel capture groups.");
        bool Concern(string level) => level is "informational" or "questionable" or "strong";
        bool Category(string category) => category is "scheduled-onsite" or "onsite-duty" or "field-deployment" or "physical-installation" or "operational-site" or "commuting-area" or "occasional-travel" or "moderate-travel" or "substantial-travel" or "frequent-travel";
        bool Text(string value) => value.Length is >= 1 and <= 300;
        var priorities = new HashSet<int>(); Count(definition.SignalRules, "signalRules");
        foreach (var rule in definition.SignalRules)
        {
            Id(rule.Id); Reference(rule.PatternId, "sentence");
            Require(rule.Priority is >= 0 and <= 10000 && priorities.Add(rule.Priority), "invalid or duplicate priority.");
            Require(Category(rule.Category) && Concern(rule.ConcernLevel) && Text(rule.Reason), "unsupported signal mapping.");
            Require(rule.UnlessContains is null || Text(rule.UnlessContains), "invalid literal exclusion.");
            Require((rule.OverridePatternId is null) == (rule.OverrideConcernLevel is null) && (rule.OverridePatternId is null) == (rule.OverrideReason is null), "override fields must be paired.");
            if (rule.OverridePatternId is not null)
            { Reference(rule.OverridePatternId, "sentence"); Require(Concern(rule.OverrideConcernLevel!) && Text(rule.OverrideReason!), "invalid override mapping."); }
        }
        Count(definition.TravelBands, "travelBands");
        var limits = new HashSet<int>();
        foreach (var band in definition.TravelBands)
            Require(band.MaximumPercent is >= 0 and <= 100 && limits.Add(band.MaximumPercent) && Category(band.Category) && Concern(band.ConcernLevel), "invalid travel band.");
        Require(limits.Contains(100), "travel bands must cover through 100 percent.");
        Require(Text(definition.TravelReasonTemplate) && definition.TravelReasonTemplate.Contains("{range}", StringComparison.Ordinal), "missing range placeholder.");
        var frequent = definition.FrequentTravel; Reference(frequent.PatternId, "sentence");
        Require(Category(frequent.Category) && Concern(frequent.ConcernLevel) && Text(frequent.Reason), "invalid frequent travel mapping.");
        Require(used.Count == patterns.Count, "unreferenced pattern definitions.");
        Array.Sort(definition.SignalRules, (a,b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.TravelBands, (a,b) => a.MaximumPercent.CompareTo(b.MaximumPercent));
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, SignalRule[] SignalRules, TravelBand[] TravelBands, string TravelReasonTemplate, FrequentRule FrequentTravel);
    internal sealed record PatternDefinition(string Id, string Type, string Scope, string Pattern);
    internal sealed record SignalRule(string Id, int Priority, string PatternId, string Category, string ConcernLevel, string Reason,
        string? UnlessContains, string? OverridePatternId, string? OverrideConcernLevel, string? OverrideReason);
    internal sealed record TravelBand(int MaximumPercent, string Category, string ConcernLevel);
    internal sealed record FrequentRule(string PatternId, string Category, string ConcernLevel, string Reason);
}
