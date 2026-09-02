using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public static class SemanticRuleStatuses
{
    public const string Proposed = "proposed";
    public const string Validated = "validated";
    public const string Active = "active";
    public const string ReviewDue = "review-due";
    public const string Retired = "retired";
    public const string Deleted = "deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Proposed, Validated, Active, ReviewDue, Retired, Deleted], StringComparer.Ordinal);

    public static bool RunsInProduction(string value) => value is Active or ReviewDue;
}

public static class SemanticRuleScopes
{
    public const string Title = "title";
    public const string Posting = "posting";
    public const string Both = "both";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Title, Posting, Both], StringComparer.Ordinal);
}

public static class SemanticRuleTypes
{
    public const string PositiveEvidence = "positive-evidence";
    public const string Exclusion = "exclusion";
    public const string RequiredContext = "required-context";
    public const string TitleEvidence = "title-evidence";
    public const string RemoteDesignation = "remote-designation";
    public const string RemoteSignal = "remote-signal";
    public const string ExtendedLocationSignal = "extended-location-signal";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([
        PositiveEvidence, Exclusion, RequiredContext, TitleEvidence,
        RemoteDesignation, RemoteSignal, ExtendedLocationSignal
    ], StringComparer.Ordinal);
}

public static class SemanticRuleRelationshipTypes
{
    public const string Supersedes = "supersedes";
    public const string DerivedFrom = "derived-from";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Supersedes, DerivedFrom], StringComparer.Ordinal);
}

public sealed record SemanticRule(
    string RuleId,
    string ConceptId,
    string Pattern,
    string Scope,
    string RuleType,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset? LastMatchedUtc,
    DateTimeOffset? LastReviewedUtc,
    DateTimeOffset? RetiredUtc,
    long MatchCountLifetime,
    long MatchCountSinceReview,
    string Provenance,
    string? Reason,
    string? ContextGroupId = null,
    long TimeoutCountLifetime = 0,
    DateTimeOffset? LastTimedOutUtc = null);

public sealed record SemanticRuleRelationship(
    string SourceRuleId, string TargetRuleId, string RelationshipType, DateTimeOffset CreatedUtc);

public sealed record SemanticRuleRelationshipCandidate(
    string SourceRuleId, string TargetRuleId, string RelationshipType);

public sealed record SemanticRuleCandidate(
    string ConceptId,
    string Pattern,
    string Scope,
    string RuleType,
    string Provenance,
    string? Reason = null,
    string? ContextGroupId = null,
    string Status = SemanticRuleStatuses.Proposed);

public sealed record SemanticRulesSnapshot(
    string Fingerprint,
    DateTimeOffset LoadedUtc,
    IReadOnlyList<SemanticRule> Rules,
    IReadOnlyList<SemanticRuleRelationship> Relationships);

public sealed record SemanticRulePolicy(
    int ReviewAfterDays = 30,
    int RetiredRetentionDays = 180,
    int TelemetryFlushSeconds = 10,
    int MaximumPatternLength = 4096,
    int RegexTimeoutMilliseconds = 100);

public sealed record SemanticRuleExport(
    int SchemaVersion,
    string TaxonomyFingerprint,
    DateTimeOffset ExportedUtc,
    IReadOnlyList<SemanticRule> Rules,
    IReadOnlyList<SemanticRuleRelationship> Relationships);

public sealed record SemanticRuleCandidateValidation(
    string RuleId,
    string RuleFingerprint,
    DateTimeOffset ValidatedUtc,
    RegexRuleEvaluationResult RuleResult,
    RegexAggregateEvaluation BaselineMacro,
    RegexAggregateEvaluation CandidateMacro,
    RegexAggregateEvaluation BaselineMicro,
    RegexAggregateEvaluation CandidateMicro,
    string ValidationCorpusFingerprint,
    string TaxonomyFingerprint);

public sealed record RegexClassification(
    string PostingContentHash,
    string RulesetFingerprint,
    DateTimeOffset ClassifiedUtc,
    IReadOnlyList<DetectedJobConcept> Concepts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MatchedRuleIds,
    IReadOnlyList<string> TimedOutRuleIds);

public static class SemanticRuleValidation
{
    public static void Validate(SemanticRuleCandidate candidate, JobConceptCatalog catalog,
        SemanticRulePolicy policy)
    {
        if (!catalog.Contains(candidate.ConceptId))
            throw new InvalidDataException($"Unknown canonical concept '{candidate.ConceptId}'.");
        if (!SemanticRuleScopes.All.Contains(candidate.Scope))
            throw new InvalidDataException($"Unknown rule scope '{candidate.Scope}'.");
        if (!SemanticRuleTypes.All.Contains(candidate.RuleType))
            throw new InvalidDataException($"Unknown rule type '{candidate.RuleType}'.");
        if (!SemanticRuleStatuses.All.Contains(candidate.Status) ||
            candidate.Status == SemanticRuleStatuses.Deleted)
            throw new InvalidDataException($"Invalid initial rule status '{candidate.Status}'.");
        if (string.IsNullOrWhiteSpace(candidate.Provenance) || candidate.Provenance.Length > 256 ||
            candidate.Reason?.Length > 4000 || candidate.ContextGroupId?.Length > 128)
            throw new InvalidDataException("Rule metadata is missing or exceeds its allowed bound.");

        if (candidate.RuleType is SemanticRuleTypes.RemoteDesignation or
            SemanticRuleTypes.RemoteSignal or SemanticRuleTypes.ExtendedLocationSignal)
        {
            if (string.IsNullOrWhiteSpace(candidate.Pattern) || candidate.Pattern.Length > 256)
                throw new InvalidDataException("Signal-category rules require a bounded category value.");
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.Pattern) ||
            candidate.Pattern.Length > policy.MaximumPatternLength)
            throw new InvalidDataException("Rule pattern is missing or exceeds its allowed size.");
        try
        {
            _ = new Regex(candidate.Pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                TimeSpan.FromMilliseconds(policy.RegexTimeoutMilliseconds));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // Some recovered patterns use constructs unsupported by NonBacktracking. They still receive
            // a strict timeout at runtime; syntax must always be valid.
            _ = new Regex(candidate.Pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(policy.RegexTimeoutMilliseconds));
        }

        if (candidate.RuleType == SemanticRuleTypes.RequiredContext &&
            string.IsNullOrWhiteSpace(candidate.ContextGroupId))
            throw new InvalidDataException("Required-context rules need a context group.");
    }

    public static void ValidateTransition(string current, string next)
    {
        var allowed = current switch
        {
            SemanticRuleStatuses.Proposed => new[] { SemanticRuleStatuses.Validated, SemanticRuleStatuses.Deleted },
            SemanticRuleStatuses.Validated => new[] { SemanticRuleStatuses.Active, SemanticRuleStatuses.Proposed, SemanticRuleStatuses.Deleted },
            SemanticRuleStatuses.Active => new[] { SemanticRuleStatuses.ReviewDue, SemanticRuleStatuses.Retired },
            SemanticRuleStatuses.ReviewDue => new[] { SemanticRuleStatuses.Active, SemanticRuleStatuses.Retired },
            SemanticRuleStatuses.Retired => new[] { SemanticRuleStatuses.Validated, SemanticRuleStatuses.Deleted },
            _ => Array.Empty<string>()
        };
        if (!allowed.Contains(next, StringComparer.Ordinal))
            throw new InvalidOperationException($"Rule lifecycle transition '{current}' -> '{next}' is not allowed.");
    }
}

public static class SemanticRulesetFingerprint
{
    public static string RuleVersion(SemanticRule rule) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            rule.RuleId, rule.ConceptId, rule.Pattern, rule.Scope, rule.RuleType,
            rule.ContextGroupId, rule.Provenance, rule.Reason
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();

    public static string Calculate(IEnumerable<SemanticRule> rules,
        IEnumerable<SemanticRuleRelationship> relationships, JobConceptCatalog catalog,
        SemanticRulePolicy policy)
    {
        var canonical = new
        {
            taxonomyVersion = catalog.Version,
            taxonomyFingerprint = catalog.Fingerprint,
            detector = new { policy.RegexTimeoutMilliseconds },
            rules = rules.Where(item => SemanticRuleStatuses.RunsInProduction(item.Status))
                .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                .ThenBy(item => item.RuleType, StringComparer.Ordinal)
                .ThenBy(item => item.Scope, StringComparer.Ordinal)
                .ThenBy(item => item.Pattern, StringComparer.Ordinal)
                .ThenBy(item => item.RuleId, StringComparer.Ordinal)
                .Select(item => new { item.RuleId, item.ConceptId, item.Pattern, item.Scope,
                    item.RuleType, item.ContextGroupId }),
            relationships = relationships.OrderBy(item => item.SourceRuleId, StringComparer.Ordinal)
                .ThenBy(item => item.TargetRuleId, StringComparer.Ordinal)
                .ThenBy(item => item.RelationshipType, StringComparer.Ordinal)
                .Select(item => new { item.SourceRuleId, item.TargetRuleId, item.RelationshipType })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
    }

    public static string PostingContentHash(string title, string description) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(title ?? "", "\n", description ?? "")))).ToLowerInvariant();

    public static string ClassificationFingerprint(string postingContentHash,
        string rulesetFingerprint, string taxonomyFingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            postingContentHash, rulesetFingerprint, taxonomyFingerprint)))).ToLowerInvariant();
}
