// Rollback compatibility only. Normal JSM neither evaluates nor updates these observations.
namespace JobSearchManager;

public sealed record CheapRejectEvidence(string PredicateId, string Scope, string Text);
public sealed record CheapRejectDecision(string RulesetVersion, string RulesetFingerprint,
    string PostingFingerprint, string Decision, string Category, string Reason,
    IReadOnlyList<string> RuleIds, IReadOnlyList<CheapRejectEvidence> Evidence,
    IReadOnlyList<string> Guards, bool FailOpen);

public sealed record CheapTriageObservation(int AnalysisVersion, DateTimeOffset AnalyzedAtUtc,
    bool DescriptionAvailable, CheapRejectDecision Result)
{
    public string Decision => Result.FailOpen ? "UNDETERMINED" : Result.Decision;
}
