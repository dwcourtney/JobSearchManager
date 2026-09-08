using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed class EducationRules
{
    private static readonly Lazy<EducationRules> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, "rules", "education-v1.json")));
    internal static EducationRules Default => Loaded.Value;
    internal Definition Rules { get; }
    internal string Version => Rules.RulesetVersion;
    internal string Fingerprint { get; }
    private readonly Dictionary<string, Regex> patterns = new(StringComparer.Ordinal);
    internal Regex Pattern(string id) => patterns[id];
    internal static EducationRules Load(string path)
    {
        try { return Parse(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidDataException($"Cannot load education rules '{path}': {ex.Message}", ex); }
    }
    internal static EducationRules Parse(byte[] bytes)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<Definition>(bytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true
            }) ?? throw new InvalidDataException("Education rules must be an object.");
            return new EducationRules(definition, bytes);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException($"Invalid education rules: {ex.Message}", ex); }
    }
    private EducationRules(Definition definition, byte[] bytes)
    {
        Rules = definition;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        void Require(bool valid, string message)
        { if (!valid) throw new InvalidDataException($"Invalid education rules: {message}"); }
        void Count<T>(T[] values, string name)
        { Require(values.Length is >= 1 and <= 128 && values.All(v => v is not null), $"{name} must have 1..128 non-null entries."); }
        Require(definition.SchemaVersion == 1, "unsupported schemaVersion.");
        var version = definition.RulesetVersion.Split('.');
        Require(version.Length == 3 && version.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)), "rulesetVersion must have three numeric components.");
        Require(definition.RegexTimeoutMilliseconds is >= 1 and <= 1000, "regexTimeoutMilliseconds must be 1..1000.");
        Require(definition.RelatedFieldResult.Length is >= 1 and <= 80, "invalid relatedFieldResult.");
        Count(definition.Patterns, "patterns");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), $"empty or duplicate ID '{id}'.");
        foreach (var p in definition.Patterns)
        {
            Id(p.Id);
            Require(p.Type == "regex", $"unsupported pattern type '{p.Type}'.");
            Require(p.Scope is "segment" or "html" or "text" or "tail" or "field" or "connector" or "context" or "window" or "token", $"unsupported scope '{p.Scope}'.");
            Require(p.Pattern.Length is >= 1 and <= 8192, $"invalid pattern length '{p.Id}'.");
            Require(p.Options.Length == 0 || p.Options.SequenceEqual(new[] { "IgnoreCase", "CultureInvariant" }), $"unsupported regex options '{p.Id}'.");
            var options = RegexOptions.Compiled;
            if (p.Options.Length > 0) options |= RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            try { patterns.Add(p.Id, new Regex(p.Pattern, options, TimeSpan.FromMilliseconds(definition.RegexTimeoutMilliseconds))); }
            catch (ArgumentException ex) { throw new InvalidDataException($"Invalid education regex '{p.Id}': {ex.Message}", ex); }
        }
        var definitions = definition.Patterns.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        void Reference(string id, string scope)
        {
            Require(definitions.TryGetValue(id, out var p) && p.Scope == scope, $"unknown or incorrectly scoped pattern '{id}'.");
            referenced.Add(id);
        }
        void Group(string id, string group) => Require(patterns[id].GetGroupNames().Contains(group, StringComparer.Ordinal), $"missing capture group '{group}' in '{id}'.");
        bool Level(string value) => value is "highSchool" or "associate" or "bachelor" or "master" or "doctorate";
        bool Requirement(string value) => value is "required" or "minimum" or "preferred" or "desired" or "mentioned";
        var priorities = new HashSet<int>();
        void Ordered(string id, int priority, string patternId, string scope)
        { Id(id); Reference(patternId, scope); Require(priority is >= 0 and <= 10000 && priorities.Add(priority), $"invalid or duplicate priority {priority}."); }
        // Named slots are executor operations, not embedded parsing vocabulary.
        Reference("AnyTag", "html");
        Reference("BlockTag", "html");
        Reference("DegreeAlternativeConnector", "connector");
        Reference("DegreeOrExperience", "segment");
        Reference("ExperienceAfterDegree", "window");
        Reference("ExperienceBeforeDegree", "window");
        Reference("FieldList", "tail");
        Reference("FieldPreferenceQualifier", "context");
        Reference("FieldSeparator", "field");
        Reference("FieldStop", "field");
        Reference("HigherDegreeSubstitution", "segment");
        Reference("IgnoredFieldFragment", "field");
        Reference("ImplicitQualification", "segment");
        Reference("InLieuOfDegree", "segment");
        Reference("Parenthetical", "field");
        Reference("PreferenceParenthetical", "field");
        Reference("RelatedField", "field");
        Reference("RelatedFieldMention", "field");
        Reference("TrailingFieldQualifier", "field");
        Reference("Whitespace", "text");
        Group("ExperienceAfterDegree", "min"); Group("ExperienceAfterDegree", "max");
        Group("ExperienceBeforeDegree", "min"); Group("ExperienceBeforeDegree", "max");
        Group("FieldList", "fields");
        Count(definition.DegreeRules, "degreeRules");
        foreach (var rule in definition.DegreeRules)
        {
            Ordered(rule.Id, rule.Priority, rule.PatternId, "segment");
            Require(Level(rule.Level), $"unknown degree level '{rule.Level}'.");
            Require(rule.SpecificDegree is null || rule.SpecificDegree == "phD" && rule.Level == "doctorate", "unsupported specificDegree mapping.");
        }
        var abbreviations = definition.Abbreviations;
        Reference(abbreviations.PatternId, "segment"); Reference(abbreviations.NormalizationPatternId, "token");
        Group(abbreviations.PatternId, abbreviations.CaptureGroup);
        Require(abbreviations.Levels.Count is >= 1 and <= 128 && abbreviations.Levels.All(p => p.Key.Length is >= 1 and <= 32 && p.Key.All(char.IsAsciiLetterUpper) && Level(p.Value)), "invalid abbreviation mapping.");
        priorities.Clear(); Count(definition.QualifierRules, "qualifierRules");
        foreach (var rule in definition.QualifierRules)
        { Ordered(rule.Id, rule.Priority, rule.PatternId, "context"); Require(Requirement(rule.Result), "unsupported qualifier result."); }
        priorities.Clear(); Count(definition.SectionRules, "sectionRules");
        foreach (var rule in definition.SectionRules)
        {
            Ordered(rule.Id, rule.Priority, rule.PatternId, "segment"); Require(Requirement(rule.Result), "unsupported section result.");
            Require((rule.ConditionalPatternId is null) == (rule.ConditionalResult is null), "conditional pattern/result must be paired.");
            if (rule.ConditionalPatternId is not null)
            { Reference(rule.ConditionalPatternId, "segment"); Require(Requirement(rule.ConditionalResult!), "unsupported conditional result."); }
        }
        var accreditation = definition.Accreditation;
        Reference(accreditation.PatternId, "segment"); Require(accreditation.Name.Length is >= 1 and <= 80, "invalid accreditation name.");
        priorities.Clear(); Count(accreditation.RequirementRules, "accreditation requirementRules");
        foreach (var rule in accreditation.RequirementRules)
        { Ordered(rule.Id, rule.Priority, rule.PatternId, "segment"); Require(Requirement(rule.Result), "unsupported accreditation result."); }
        Require(referenced.Count == patterns.Count, "unreferenced pattern definitions.");
        Array.Sort(definition.DegreeRules, (a,b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.QualifierRules, (a,b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(definition.SectionRules, (a,b) => a.Priority.CompareTo(b.Priority));
        Array.Sort(accreditation.RequirementRules, (a,b) => a.Priority.CompareTo(b.Priority));
    }
    internal sealed record Definition(int SchemaVersion, string RulesetVersion, int RegexTimeoutMilliseconds,
        PatternDefinition[] Patterns, DegreeRule[] DegreeRules, AbbreviationDefinition Abbreviations,
        ResultRule[] QualifierRules, SectionRule[] SectionRules, AccreditationDefinition Accreditation, string RelatedFieldResult);
    internal sealed record PatternDefinition(string Id, string Type, string Pattern, string Scope, string[] Options);
    internal sealed record DegreeRule(string Id, string PatternId, int Priority, string Level, string? SpecificDegree);
    internal sealed record AbbreviationDefinition(string PatternId, string NormalizationPatternId, string CaptureGroup, Dictionary<string,string> Levels);
    internal sealed record ResultRule(string Id, string PatternId, int Priority, string Result);
    internal sealed record SectionRule(string Id, string PatternId, int Priority, string Result, string? ConditionalPatternId, string? ConditionalResult);
    internal sealed record AccreditationDefinition(string PatternId, string Name, ResultRule[] RequirementRules);
}
