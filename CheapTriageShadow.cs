using System.Security.Cryptography;
using System.Text.Json;

namespace JobSearchManager;

public sealed record CheapTriageObservation(int AnalysisVersion, DateTimeOffset AnalyzedAtUtc,
    bool DescriptionAvailable, CheapRejectDecision Result)
{
    public string Decision => Result.FailOpen ? "UNDETERMINED" : Result.Decision;
}

/// <summary>Observations only. Configuration and rules are immutable startup snapshots.</summary>
public sealed class CheapTriageShadow
{
    public const int AnalysisVersion = 1;
    public string Mode { get; }
    public CheapRejectRules Rules { get; }
    public bool Enabled => Mode == "Shadow";

    public CheapTriageShadow(CheapRejectRules rules, IConfiguration configuration)
    {
        Rules = rules;
        Mode = configuration["CheapTriage:Mode"] ?? "Off";
        if (Mode is not ("Off" or "Shadow"))
            throw new InvalidDataException("CheapTriage:Mode must be Off or Shadow. Active is not supported.");
    }

    internal static string InputFingerprint(JobRecord job) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new[] { job.Title ?? "", job.DescriptionHtml ?? "" }))).ToLowerInvariant();

    public bool IsCurrent(JobRecord job) => job.CheapTriage is { } observation &&
        observation.AnalysisVersion == AnalysisVersion &&
        observation.Result.RulesetVersion == Rules.Version &&
        observation.Result.RulesetFingerprint == Rules.Fingerprint &&
        observation.Result.PostingFingerprint == InputFingerprint(job);

    internal CheapTriageObservation Observe(JobRecord job)
    {
        if (!Enabled) throw new InvalidOperationException("Live Cheap Triage is Off.");
        CheapRejectDecision result;
        try
        {
            result = string.IsNullOrWhiteSpace(job.Title) && string.IsNullOrWhiteSpace(job.DescriptionHtml)
                ? Undetermined("missing-input", "No title or description is available.")
                : Rules.Decide(job.Title ?? "", job.DescriptionHtml ?? "");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never turn a diagnostic failure into a classification or visibility decision.
            result = Undetermined("evaluation-error", "Shadow evaluation failed; normal processing continues.");
        }
        return new(AnalysisVersion, DateTimeOffset.UtcNow, !string.IsNullOrWhiteSpace(job.DescriptionHtml), result);

        CheapRejectDecision Undetermined(string category, string reason) => new(
            Rules.Version, Rules.Fingerprint, InputFingerprint(job), "KEEP", category, reason, [], [], [], true);
    }
}

public sealed record CheapTriageDiagnostic(string Mode, string RulesetVersion, string RulesetFingerprint,
    bool Current, CheapTriageObservation? Observation);

public sealed record CheapTriageReviewJob(string Employer, JobListItem Job, string WorkflowState,
    bool SourceAvailable, bool Current, CheapTriageObservation Observation);

public sealed record CheapTriageLiveReport(string Mode, string Scope, string CompanyId,
    string RulesetVersion, string RulesetFingerprint, int TotalJobs, int AnalyzedJobs,
    int Keep, int Reject, int Undetermined, int TitleOnly, int DescriptionBacked,
    int StalePreviousRuleset, int RequiringReconciliation, double RejectionPercentage,
    bool Running, DateTimeOffset? LatestAnalysisUtc, DateTimeOffset? LastReconciliationUtc,
    string? Error, IReadOnlyList<CheapTriageReviewJob> RejectedJobs);
