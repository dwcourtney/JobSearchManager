using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JobSearchManager;

namespace JobSearchManager;

// Validated once before host construction; the sole normal-runtime concept authority.
internal sealed class ConceptRuleSnapshot
{
    internal sealed record Taxonomy(int Version, string Sha256);
    internal sealed record Contract(string Id, int Version);
    internal sealed record Policy(int TimeoutMilliseconds, int MaximumPatternLength,
        ImmutableArray<string> Options, bool PreferNonBacktracking, string UnsupportedRegexFallback,
        string RuleTimeoutBehavior, string LocalNegationTimeoutBehavior);
    internal sealed record Provenance(string Phase1ExportSha256, string SqliteRuntimeFingerprint, string ArchiveDatabaseSha256);
    internal sealed record Selector(string Source, string Operator, string? Category = null);
    internal sealed record Rule(string RuleId, string ConceptId, string Kind, string Scope, int ExecutionOrder,
        string Provenance, string Description, string? Pattern = null, string? ContextGroupId = null, Selector? Selector = null);
    private sealed record Document(int SchemaVersion, string RulesetVersion, Taxonomy Taxonomy,
        Contract EngineContract, Policy RegexPolicy, Provenance MigrationProvenance, ImmutableArray<Rule> Rules);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
    };
    internal const string SupportedSchemaHash = "f1d7f95e374cdf6d123caeb7417b4e051a2593bfc0467927d49d209adef31a7d";
    internal string Version { get; }
    internal string ByteHash { get; }
    internal string SchemaHash => SupportedSchemaHash;
    internal Taxonomy TaxonomyIdentity { get; }
    internal Contract EngineContract { get; }
    internal string EngineContractHash { get; }
    internal Policy RegexPolicy { get; }
    internal string PolicyHash { get; }
    internal string PipelineFingerprint { get; }
    internal string CandidatePipelineFingerprint { get; }
    internal object FactualDependencies => new {
        remoteWork = new { version = RemoteWorkRules.Default.Version, hash = RemoteWorkRules.Default.Fingerprint, analysisVersion = RemoteWorkDetector.CurrentAnalysisVersion },
        extendedLocation = new { version = ExtendedLocationRules.Default.Version, hash = ExtendedLocationRules.Default.Fingerprint, analysisVersion = ExtendedLocationRequirementDetector.CurrentAnalysisVersion } };
    internal Provenance MigrationProvenance { get; }
    internal ImmutableArray<Rule> Rules { get; }
    internal RegexSemanticClassifier Matcher { get; }
    internal object Identity => new { Version, ByteHash, SchemaHash, TaxonomyIdentity, EngineContract,
        EngineContractHash, RegexPolicy, PolicyHash, PipelineFingerprint, CandidatePipelineFingerprint, FactualDependencies, Authority = "json-regex-v1", MigrationProvenance,
        RemoteWorkRulesHash = RemoteWorkRules.Default.Fingerprint, ExtendedLocationRulesHash = ExtendedLocationRules.Default.Fingerprint };

    internal static ConceptRuleSnapshot Load(string path, JobConceptCatalog catalog)
    {
        var schema = File.ReadAllBytes(Path.ChangeExtension(path, ".schema.json"));
        Require(Hash(schema) == SupportedSchemaHash, "schema bytes differ from supported v1 schema");
        return Parse(File.ReadAllBytes(path), catalog);
    }

    internal static ConceptRuleSnapshot Parse(byte[] bytes, JobConceptCatalog catalog)
    {
        try
        {
            using var tree = JsonDocument.Parse(bytes);
            ValidateProperties(tree.RootElement);
            var document = JsonSerializer.Deserialize<Document>(bytes, Json)
                ?? throw new InvalidDataException("Missing JSON concept document.");
            return new(document, bytes, catalog);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException("Invalid JSON concept candidate: " + ex.Message, ex); }
    }

    private ConceptRuleSnapshot(Document d, byte[] bytes, JobConceptCatalog catalog)
    {
        Require(d.SchemaVersion == 1 && d.RulesetVersion == "1.0.0", "unsupported schema/ruleset version");
        Require(d.EngineContract == new Contract("jsm-concept-matching", 1), "unsupported engine contract");
        Require(d.Taxonomy.Version == 9 && d.Taxonomy.Version == catalog.Version && d.Taxonomy.Sha256 == catalog.Fingerprint &&
            d.Taxonomy.Sha256 == "514ed1c8c644d1eec426b5fdcf4d5a2c447aa61ce5572ae70b2d03fc3815a049",
            "taxonomy identity mismatch");
        var policy = d.RegexPolicy;
        Require(policy.TimeoutMilliseconds == 100 && policy.MaximumPatternLength == 4096 &&
            policy.Options.SequenceEqual(["IgnoreCase", "CultureInvariant"]) && policy.PreferNonBacktracking &&
            policy.UnsupportedRegexFallback == "bounded-backtracking" && policy.RuleTimeoutBehavior == "record-non-match" &&
            policy.LocalNegationTimeoutBehavior == "reject-match", "unsupported timeout/options policy");
        Require(HashText(d.MigrationProvenance.Phase1ExportSha256) && HashText(d.MigrationProvenance.SqliteRuntimeFingerprint) &&
            HashText(d.MigrationProvenance.ArchiveDatabaseSha256), "invalid migration provenance");
        Require(!d.Rules.IsDefault && d.Rules.Length is >= 1 and <= 10000 && d.Rules.All(r => r is not null), "invalid rule inventory");
        var ids = new HashSet<string>(StringComparer.Ordinal); var positions = new HashSet<int>();
        var remote = RemoteWorkRules.Default.Rules;
        var remoteCategories = remote.SignalRules.Select(r => r.Category).Concat(remote.TravelBands.Select(r => r.Category))
            .Append(remote.FrequentTravel.Category).ToHashSet(StringComparer.Ordinal);
        var extended = ExtendedLocationRules.Default.Rules;
        var extendedCategories = extended.SignalRules.Select(r => r.Category).Append(extended.Duration.Category).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in d.Rules)
        {
            Require(Text(rule.RuleId, 300) && ids.Add(rule.RuleId), "invalid/duplicate RuleId");
            Require(Text(rule.ConceptId, 300) && catalog.Contains(rule.ConceptId), "unknown ConceptId");
            Require(rule.ExecutionOrder >= 0 && rule.ExecutionOrder < d.Rules.Length && positions.Add(rule.ExecutionOrder), "invalid/duplicate execution position");
            Require(ConceptRuleTypes.All.Contains(rule.Kind), "invalid rule kind");
            Require(ConceptRuleScopes.All.Contains(rule.Scope), "invalid scope");
            Require(rule.Kind is not ("title-evidence" or "exclusion") || rule.Scope == "title", "title rule requires title scope");
            Require(rule.Provenance.Length <= 1000 && rule.Description.Length <= 4000, "excessive descriptive metadata");
            Require(rule.Kind == "required-context" ? Text(rule.ContextGroupId, 300) : rule.ContextGroupId is null, "invalid context group");
            if (rule.Kind is "remote-designation" or "remote-signal" or "extended-location-signal")
            {
                Require(rule.Pattern is null && rule.Selector is not null, "malformed parsed-fact selector");
                var selector = rule.Selector!;
                Require(rule.Kind switch
                {
                    "remote-designation" => selector.Source == "remoteWork.isRemoteDesignated" && selector.Operator == "isTrue" && selector.Category is null,
                    "remote-signal" => selector.Source == "remoteWork.signals" && selector.Operator == "firstCategoryEquals" && selector.Category is not null && remoteCategories.Contains(selector.Category),
                    _ => selector.Source == "extendedLocation.signals" && selector.Operator == "firstCategoryEquals" && selector.Category is not null && extendedCategories.Contains(selector.Category)
                }, "unknown source/category or malformed selector");
            }
            else
            {
                Require(rule.Selector is null && rule.Pattern is { Length: >= 1 and <= 4096 }, "invalid/excessive regex pattern");
                const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
                try { _ = new Regex(rule.Pattern!, options | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)); }
                catch (NotSupportedException) { _ = new Regex(rule.Pattern!, options, TimeSpan.FromMilliseconds(100)); }
            }
        }
        foreach (var group in d.Rules.Where(r => r.ContextGroupId is not null).GroupBy(r => r.ContextGroupId, StringComparer.Ordinal))
            Require(group.Count() >= 2 && group.Select(r => r.ConceptId).Distinct(StringComparer.Ordinal).Count() == 1, "invalid context membership");
        var canonical = d.Rules.OrderBy(r => r.ConceptId, StringComparer.Ordinal).ThenBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.RuleId, StringComparer.Ordinal).ToArray();
        Require(canonical.Select((r, i) => r.ExecutionOrder == i).All(v => v), "execution positions do not preserve SQL contract");
        Rules = d.Rules.OrderBy(r => r.ExecutionOrder).ToImmutableArray(); // Physical JSON order is irrelevant.
        Version = d.RulesetVersion; ByteHash = Hash(bytes); TaxonomyIdentity = d.Taxonomy; EngineContract = d.EngineContract;
        RegexPolicy = policy; MigrationProvenance = d.MigrationProvenance;
        EngineContractHash = Hash(JsonSerializer.SerializeToUtf8Bytes(new { d.EngineContract,
            frozenMatchingContract = "6aa0f195141a27c982c759cfd314b60d25de1e503ffdcd86f480ac2296db419c" }, Json));
        PolicyHash = Hash(JsonSerializer.SerializeToUtf8Bytes(policy, Json));
        CandidatePipelineFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { Version, ByteHash, SchemaHash,
            TaxonomyIdentity, EngineContractHash, PolicyHash,
            remoteWork = RemoteWorkRules.Default.Fingerprint, extendedLocation = ExtendedLocationRules.Default.Fingerprint }, Json));
        PipelineFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { authority = "json-regex-v1", CandidatePipelineFingerprint, FactualDependencies, inputContract = "posting-and-consumed-facts-v1" }, Json));
        // Preserve exact execution order and matching inputs; lifecycle metadata is not runtime data.
        var matching = Rules.Select(r => new ConceptMatchRule(r.RuleId, r.ConceptId,
            r.Pattern ?? r.Selector!.Category ?? "remote-designation", r.Scope, r.Kind,
            r.ContextGroupId)).ToImmutableArray();
        Matcher = new RegexSemanticClassifier(new(PipelineFingerprint, matching), catalog, new ConceptRegexPolicy());
    }

    internal RegexClassification Classify(string title, string html, RemoteWorkAnalysis? remote, ExtendedLocationRequirementAnalysis? extended) =>
        Matcher.Classify(title, html, remote, extended, productionUsage: false);
    private static void ValidateProperties(JsonElement node)
    {
        Require(node.ValueKind != JsonValueKind.Null, "explicit null is not allowed by the schema");
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            { Require(names.Add(property.Name), "duplicate JSON property"); ValidateProperties(property.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) ValidateProperties(item);
    }
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static bool HashText(string value) => value.Length == 64 && value.All(c => char.IsAsciiHexDigitLower(c));
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException("Invalid JSON concept candidate: " + message); }
}
