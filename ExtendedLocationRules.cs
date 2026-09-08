using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class ExtendedLocationRules
{
    private static readonly Lazy<ExtendedLocationRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "extended-location-v1.json")));
    internal static ExtendedLocationRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal static ExtendedLocationRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load extended-location rules '{path}': {ex.Message}", ex); }
    }
    internal static ExtendedLocationRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Extended-location rules must be an object.");
            return new ExtendedLocationRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid extended-location rules: {ex.Message}", ex); }
    }
    private ExtendedLocationRules(Definition definition, byte[] bytes)
    {
        Rules = definition; Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid extended-location rules: {message}"); }
        void Count<T>(T[] values, string name)
        { Require(values.Length is >= 1 and <= 128 && values.All(v => v is not null), $"{name} must have 1..128 non-null entries."); }
        bool Text(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 300;
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "invalid rulesetVersion.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "invalid finite regex timeout.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(Text(id) && ids.Add(id), $"empty or duplicate ID '{id}'.");
        Count(definition.Patterns, "patterns");
        foreach (var p in definition.Patterns)
        {
            Id(p.Id); Require(p.Pattern.Length is >= 1 and <= 8192, "invalid pattern length.");
            patterns.Add(p.Id, new Regex(p.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds)));
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Reference(string id) { Require(patterns.ContainsKey(id), $"unknown pattern '{id}'."); used.Add(id); }
        foreach (var id in new[] { "HistoricalPattern", "CurrentObligationPattern", "ConditionalOnlyPattern", "DefiniteObligationPattern", "ExplicitObligationPattern", "AwayPresencePattern", "LongDurationTravelPresencePattern", "DurationPattern" }) Reference(id);
        Require(new[] { "value", "upper", "unit" }.All(patterns["DurationPattern"].GetGroupNames().Contains), "missing duration capture groups.");
        bool Confidence(string value) => value is "strong" or "questionable";
        bool Category(string value) => value is "explicit-job-location" or "required-deployment" or "recurring-deployment" or "extended-deployment" or "winter-over-assignment" or "long-term-away-assignment" or "forward-deployed" or "oconus-assignment" or "rotation" or "temporary-duty" or "required-unusual-relocation" or "possible-deployment" or "possible-extended-assignment" or "extended-away-duration";
        var priorities = new HashSet<int>(); Count(definition.SignalRules, "signalRules");
        foreach (var rule in definition.SignalRules)
        {
            Id(rule.Id); Reference(rule.PatternId);
            Require(rule.Priority is >= 0 and <= 10000 && priorities.Add(rule.Priority), "invalid or duplicate signal priority.");
            Require(Category(rule.Category) && Confidence(rule.Confidence) && Text(rule.Reason), "unsupported signal mapping.");
        }
        priorities.Clear(); Count(definition.Destinations, "destinations");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var destination in definition.Destinations)
        {
            Reference(destination.PatternId);
            Require(Text(destination.Name) && names.Add(destination.Name), "empty or duplicate destination.");
            Require(destination.Priority is >= 0 and <= 10000 && priorities.Add(destination.Priority), "invalid or duplicate destination priority.");
        }
        var duration = definition.Duration;
        Require(duration.MinimumDays is >= 1 and <= 10000 && Category(duration.Category) && Confidence(duration.Confidence) && Text(duration.Reason), "invalid duration mapping.");
        Require(duration.Numbers.Count is >= 1 and <= 128 && duration.Numbers.All(p => Text(p.Key) && p.Key == p.Key.ToLowerInvariant() && p.Value is >= 1 and <= 1000), "invalid number vocabulary.");
        Require(duration.UnitPrefixes.Count is >= 1 and <= 32 && duration.UnitPrefixes.All(p => Text(p.Key) && p.Key == p.Key.ToLowerInvariant() && p.Value is >= 1 and <= 366), "invalid duration unit mapping.");
        Require(duration.DefaultDaysPerUnit is >= 1 and <= 366, "invalid default duration unit.");
        Require(definition.Summaries.Count is >= 1 and <= 128 && definition.Summaries.All(p => Category(p.Key) && p.Value is not null && Text(p.Value)), "invalid summary mapping.");
        Require(definition.SignalRules.All(p => definition.Summaries.ContainsKey(p.Category)) && definition.Summaries.ContainsKey(duration.Category), "missing summary mapping.");
        Require(Text(definition.UnspecifiedDestination) && Text(definition.StrongFallbackSummary) && Text(definition.QuestionableFallbackSummary), "invalid fallback text.");
        Require(used.Count == patterns.Count, "unreferenced pattern definitions.");
        Array.Sort(definition.SignalRules, (a, b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.Destinations, (a, b) => a.Priority.CompareTo(b.Priority));
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, SignalRule[] SignalRules, Destination[] Destinations, DurationRule Duration,
        Dictionary<string, string> Summaries, string UnspecifiedDestination, string StrongFallbackSummary, string QuestionableFallbackSummary);
    internal sealed record PatternDefinition(string Id, string Pattern);
    internal sealed record SignalRule(string Id, int Priority, string PatternId, string Category, string Confidence, string Reason);
    internal sealed record Destination(string Name, string PatternId, int Priority);
    internal sealed record DurationRule(int MinimumDays, string Category, string Confidence, string Reason,
        Dictionary<string, int> Numbers, Dictionary<string, int> UnitPrefixes, int DefaultDaysPerUnit);
}
