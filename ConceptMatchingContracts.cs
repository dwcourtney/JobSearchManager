using System.Security.Cryptography;
using System.Text;
namespace JobSearchManager;

// Shared matching output contract; historical tools implement this without entering production DI.
public interface IConceptMatcher
{
    string RulesetFingerprint { get; }
    int ActiveRuleCount { get; }
    RegexClassification Classify(string title, string descriptionHtml, RemoteWorkAnalysis? remoteWork,
        ExtendedLocationRequirementAnalysis? extendedLocation, bool productionUsage);
}

internal sealed record ConceptMatchRule(string RuleId, string ConceptId, string Pattern,
    string Scope, string RuleType, string? ContextGroupId);
internal sealed record ConceptMatchSnapshot(string Fingerprint, IReadOnlyList<ConceptMatchRule> Rules);
internal sealed record ConceptRegexPolicy(int MaximumPatternLength = 4096, int RegexTimeoutMilliseconds = 100);

public sealed record RegexClassification(
    string PostingContentHash,
    string RulesetFingerprint,
    DateTimeOffset ClassifiedUtc,
    IReadOnlyList<DetectedJobConcept> Concepts,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MatchedRuleIds,
    IReadOnlyList<string> TimedOutRuleIds);

public static class ConceptRuleScopes
{
    public const string Title = "title";
    public const string Posting = "posting";
    public const string Both = "both";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Title, Posting, Both], StringComparer.Ordinal);
}

public static class ConceptRuleTypes
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

public static class ConceptFingerprint
{
    public static string PostingContentHash(string title, string description) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(title ?? "", "\n", description ?? "")))).ToLowerInvariant();

    public static string ClassificationFingerprint(string postingContentHash,
        string rulesetFingerprint, string taxonomyFingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            postingContentHash, rulesetFingerprint, taxonomyFingerprint)))).ToLowerInvariant();
}
