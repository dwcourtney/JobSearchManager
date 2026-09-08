using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class WorkAuthorizationRules
{
    private static readonly Lazy<WorkAuthorizationRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "work-authorization-v1.json")));
    internal static WorkAuthorizationRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];

    internal static WorkAuthorizationRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load work-authorization rules '{path}': {ex.Message}", ex); }
    }

    internal static WorkAuthorizationRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Work-authorization rules must be an object.");
            return new WorkAuthorizationRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid work-authorization rules: {ex.Message}", ex); }
    }

    private WorkAuthorizationRules(Definition definition, byte[] bytes)
    {
        Rules = definition;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid work-authorization rules: {message}"); }
        void Count<T>(T[] values, string name, int minimum = 0)
        { Require(values.Length >= minimum && values.Length <= 64 && values.All(v => v is not null), $"{name} must have {minimum}..64 non-null entries."); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var versionParts = definition.RulesetVersion.Split('.');
        Require(versionParts.Length == 3 && versionParts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "rulesetVersion must have three numeric components.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "regexTimeoutMilliseconds must be 1..1000.");
        Count(definition.Patterns, "patterns", 1);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), $"empty or duplicate ID '{id}'.");
        foreach (var pattern in definition.Patterns)
        {
            Id(pattern.Id);
            Require(pattern.Type == "regex", $"unsupported pattern type '{pattern.Type}'.");
            Require(pattern.Scope is "segment" or "plainText", $"unsupported scope '{pattern.Scope}'.");
            Require(pattern.Pattern.Length is > 0 and <= 8192, $"pattern '{pattern.Id}' length must be 1..8192.");
            Require(pattern.Options.SequenceEqual(new[] { "IgnoreCase" }) || pattern.Options.SequenceEqual(new[] { "IgnoreCase", "CultureInvariant" }), $"unsupported regex options for '{pattern.Id}'.");
            var options = RegexOptions.Compiled | RegexOptions.IgnoreCase;
            if (pattern.Options.Length == 2) options |= RegexOptions.CultureInvariant;
            try { patterns.Add(pattern.Id, new Regex(pattern.Pattern, options, TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds))); }
            catch (ArgumentException ex) { throw new InvalidDataException($"Invalid work-authorization regex '{pattern.Id}': {ex.Message}", ex); }
        }
        var definitions = definition.Patterns.ToDictionary(p => p.Id, StringComparer.Ordinal);
        void Reference(string id, string scope = "segment") => Require(definitions.TryGetValue(id, out var p) && p.Scope == scope, $"unknown or incorrectly scoped reference '{id}'.");
        void References(string[] refs)
        {
            Count(refs, "pattern references");
            foreach (var id in refs) Reference(id);
        }
        var priorities = new HashSet<int>();
        void Ordered(string id, int priority, string patternId)
        {
            Id(id); Reference(patternId);
            Require(priority >= 0 && priorities.Add(priority), $"negative or duplicate priority {priority}.");
        }
        Count(definition.Normalizations, "normalizations");
        foreach (var rule in definition.Normalizations)
        {
            Id(rule.Id); Reference(rule.PatternId, "plainText");
            Require(rule.Replacement.Length <= 256, "normalization replacement exceeds 256 characters.");
        }
        Count(definition.SectionRules, "sectionRules");
        foreach (var rule in definition.SectionRules)
        {
            Ordered(rule.Id, rule.Priority, rule.PatternId);
            Require(rule.Result is "strict" or "preferred", $"unsupported section result '{rule.Result}'.");
        }
        priorities.Clear();
        Count(definition.SponsorshipRules, "sponsorshipRules");
        foreach (var rule in definition.SponsorshipRules)
        {
            Ordered(rule.Id, rule.Priority, rule.PatternId); References(rule.UnlessPatternIds);
            Require(rule.Result == "notAvailable" && rule.Strength == "strict", "unsupported sponsorship mapping.");
        }
        priorities.Clear();
        Count(definition.EligibilityRules, "eligibilityRules", 1);
        foreach (var rule in definition.EligibilityRules)
        {
            Ordered(rule.Id, rule.Priority, rule.PatternId); References(rule.AnyPatternIds); References(rule.UnlessPatternIds);
            Require(rule.Result is "usPerson" or "usCitizenOrPermanentResident" or "usCitizen" or "australianCitizen" or "usWorkAuthorized" or "locationWorkAuthorized" or "ambiguousCitizenship" or "exportControlled", $"unsupported eligibility result '{rule.Result}'.");
            Require(rule.CountryCode is null or "US" or "AU", "unsupported countryCode.");
            Require(rule.Strength is "strict" or "preferred" or "ambiguous" or "mentioned" or "section", "unsupported strength.");
            Require(rule.Application is "replace" or "firstSpecific" or "fillUnset", "unsupported application mode.");
            Require(rule.EvidenceIndex is "match" or "segmentStart", "unsupported evidenceIndex.");
            Require((rule.ConditionalPatternId is null) == (rule.ConditionalStrength is null), "conditional pattern/strength must be paired.");
            if (rule.ConditionalPatternId is not null)
            {
                Reference(rule.ConditionalPatternId);
                Require(rule.ConditionalStrength == "customerDependent", "unsupported conditionalStrength.");
            }
        }
        Array.Sort(definition.SectionRules, (a, b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.SponsorshipRules, (a, b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.EligibilityRules, (a, b) => a.Priority.CompareTo(b.Priority));
    }

    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, Normalization[] Normalizations, SectionRule[] SectionRules,
        SponsorshipRule[] SponsorshipRules, EligibilityRule[] EligibilityRules);
    internal sealed record PatternDefinition(string Id, string Type, string Pattern, string Scope, string[] Options);
    internal sealed record Normalization(string Id, string PatternId, string Replacement);
    internal sealed record SectionRule(string Id, int Priority, string PatternId, string Result);
    internal sealed record SponsorshipRule(string Id, int Priority, string PatternId, string[] UnlessPatternIds, string Result, string Strength);
    internal sealed record EligibilityRule(string Id, int Priority, string PatternId, string[] AnyPatternIds,
        string[] UnlessPatternIds, bool OnlyWhenUnset, string Result, string? CountryCode, string Strength,
        string Application, string EvidenceIndex, string? ConditionalPatternId, string? ConditionalStrength);
}
