using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class SalaryRules
{
    private static readonly Lazy<SalaryRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "salary-v1.json")));
    internal static SalaryRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal static SalaryRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load salary rules '{path}': {ex.Message}", ex); }
    }
    internal static SalaryRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Salary rules must be an object.");
            return new SalaryRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid salary rules: {ex.Message}", ex); }
    }
    private SalaryRules(Definition definition, byte[] bytes)
    {
        Rules = definition; Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid salary rules: {message}"); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "invalid rulesetVersion.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "invalid finite regex timeout.");
        Require(definition.Patterns.Length is >= 1 and <= 128 && definition.Patterns.All(p => p is not null), "invalid patterns.");
        foreach (var p in definition.Patterns)
        {
            Require(!string.IsNullOrWhiteSpace(p.Id) && p.Id.Length <= 300 && !patterns.ContainsKey(p.Id), "empty or duplicate pattern ID.");
            Require(p.Pattern.Length is >= 1 and <= 8192, "invalid pattern length.");
            var options = RegexOptions.Compiled | (p.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | (p.CultureInvariant ? RegexOptions.CultureInvariant : RegexOptions.None);
            patterns.Add(p.Id, new Regex(p.Pattern, options, TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds)));
        }
        var required = new[] { "SpecificSalaryRegex", "SummaryPayRangeRegex", "SummaryPayHeadingRegex", "SummarySectionRangeRegex", "UsdSalaryRangeRegex", "StandardPayRangeRegex", "CompensationRangeRegex", "SeparatedSalaryBoundsRegex", "HourlyCueRegex", "AnnualCueRegex" };
        Require(patterns.Count == required.Length && required.All(patterns.ContainsKey), "missing or unsupported pattern reference.");
        foreach (var id in required.Where(id => id is not ("SummaryPayHeadingRegex" or "HourlyCueRegex" or "AnnualCueRegex")))
        {
            var groups = patterns[id].GetGroupNames();
            Require(new[] { "minimum", "maximum", "context" }.All(groups.Contains), $"missing amount/context captures in '{id}'.");
            if (id is "SpecificSalaryRegex" or "SummaryPayRangeRegex" or "SummarySectionRangeRegex" or "StandardPayRangeRegex" or "CompensationRangeRegex")
                Require(new[] { "minimumScale", "maximumScale" }.All(groups.Contains), $"missing scale captures in '{id}'.");
            if (id is "SummaryPayRangeRegex" or "SummarySectionRangeRegex")
                Require(new[] { "minimumDollar", "maximumDollar" }.All(groups.Contains), $"missing currency captures in '{id}'.");
        }
        Require(definition.UnparseablePhrases.Length is >= 1 and <= 64 && definition.UnparseablePhrases.All(p => !string.IsNullOrWhiteSpace(p) && p.Length <= 300), "invalid unparseable phrase list.");
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, string[] UnparseablePhrases);
    internal sealed record PatternDefinition(string Id, string Pattern, bool IgnoreCase, bool CultureInvariant);
}
