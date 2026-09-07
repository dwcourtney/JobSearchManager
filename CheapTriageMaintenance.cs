using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public sealed record CandidateMetrics(double KeepRecall, double DescribedKeepRecall, double TitleOnlyKeepRecall,
    double RejectionRate, double OldCacheRejectionRate, int FalseRejectCount);
public sealed record MaintenanceCandidate(string SnapshotBundle, string BaselineVersion, string BaselineHash,
    string CandidateVersion, string CandidateHash, string RulesetJson, int HumanCorrections,
    CandidateMetrics Before, CandidateMetrics After, JsonElement[] ChangedDecisions,
    string[] Warnings, string[] SafetyFailures, string ValidationStatus);
public sealed record NoUpdateHumanMatch(string Id, HumanAdjudication Human, string Decision);
public sealed record MaintenanceNoUpdate(string SnapshotBundle, string RulesetVersion, string RulesetHash,
    string QueueFingerprint, string SourceManifestHash, NoUpdateHumanMatch[] HumanMatches,
    CandidateMetrics Metrics, int ChangedDecisionCount, string ValidationStatus,
    string[] Warnings, string[] SafetyFailures, string EvaluationArtifactHash);
public sealed record MaintenanceResult(string ResultType, MaintenanceCandidate? Candidate, MaintenanceNoUpdate? NoUpdate);
public sealed record MaintenanceResultImport(string Key, int Revision, MaintenanceResult Result);
public sealed record MaintenanceEvent(int Revision, string Kind, string Reviewer, string Timestamp,
    RuleMaintenancePrompt? Prompt, MaintenanceCandidate? Candidate, string? CandidateHash)
{
    public MaintenanceNoUpdate? NoUpdate { get; init; }
}
public sealed record MaintenanceWorkflowState(string Key, int Revision, string Stage, HumanReviewReport Review,
    RuleMaintenancePrompt? Prompt, MaintenanceCandidate? Candidate, string? Disposition)
{
    public RuleMaintenancePrompt? ReleasePrompt { get; init; }
    public MaintenanceReleaseReceipt? Release { get; init; }
    public MaintenanceNoUpdate? NoUpdate { get; init; }
    public string? ResultHash { get; init; }
}
public sealed record MaintenanceReleaseReceipt(string WorkflowKey, string CandidateHash, string RulesetVersion, string DeploymentIdentity, string ValidationStatus);
public sealed record WorkflowRevision(string Key, int Revision);
public sealed record CandidateImport(string Key, int Revision, MaintenanceCandidate Candidate);
public sealed record CandidateDisposition(string Key, int Revision, string CandidateHash, string Decision);

/// <summary>Administrative handoff records only. Never updates rules, jobs or human adjudications.</summary>
public sealed class CheapTriageMaintenance(IConfiguration config, IHostEnvironment environment,
    CheapRejectRules rules, HostingConfiguration? hosting = null)
{
    private readonly object gate = new();
    private readonly string directory = config["CheapTriage:MaintenanceDirectory"] ?? Path.Combine(
        hosting?.IsContainer == true ? "/app/data" : Path.Combine(environment.ContentRootPath, "data"), "cheap-triage-maintenance");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private string Key(HumanReviewReport review) => Hash(JsonSerializer.SerializeToUtf8Bytes(new
    {
        review.QueueFingerprint, review.SourceManifestHash, rules.Fingerprint,
        decisions = review.Reviews.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray()
    }, Json));
    private List<MaintenanceEvent> Events(string key) => File.Exists(Path.Combine(directory, key + ".json"))
        ? JsonSerializer.Deserialize<List<MaintenanceEvent>>(File.ReadAllBytes(Path.Combine(directory, key + ".json")), Json)!
        : [];
    public MaintenanceWorkflowState Read(HumanReviewReport review)
    {
        lock (gate)
        {
            var key = Key(review); var events = Events(key); var last = events.LastOrDefault();
            var prompt = events.LastOrDefault(e => e.Kind == "prepared")?.Prompt;
            var candidate = last?.Candidate;
            if (review.Complete && last?.Kind == "no-update-needed" && last.NoUpdate is { } completed)
                return new(key, last.Revision, "no-update-needed", review, prompt, null, null)
                { NoUpdate = completed, ResultHash = Hash(JsonSerializer.SerializeToUtf8Bytes(completed, Json)) };
            // A supplied receipt is evidence, never an instruction to activate or deploy.
            if (review.Complete && Directory.Exists(directory))
            foreach (var file in Directory.EnumerateFiles(directory, "*.release.json"))
            {
                MaintenanceReleaseReceipt? receipt;
                try { receipt = JsonSerializer.Deserialize<MaintenanceReleaseReceipt>(File.ReadAllBytes(file), Json); }
                catch (JsonException) { continue; }
                if (receipt is null || string.IsNullOrEmpty(receipt.WorkflowKey) || receipt.WorkflowKey.Length != 64 || !receipt.WorkflowKey.All(Uri.IsHexDigit) ||
                    receipt.CandidateHash != rules.Fingerprint || receipt.RulesetVersion != rules.Version || receipt.ValidationStatus != "PASS" ||
                    string.IsNullOrEmpty(receipt.DeploymentIdentity) || receipt.DeploymentIdentity.Length != 40 || !receipt.DeploymentIdentity.All(Uri.IsHexDigit)) continue;
                var identity = typeof(CheapTriageMaintenance).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
                if (!identity.EndsWith("+" + receipt.DeploymentIdentity, StringComparison.OrdinalIgnoreCase)) continue;
                var releasedEvent = Events(receipt.WorkflowKey).LastOrDefault();
                if (releasedEvent?.Kind != "release-prepared" || releasedEvent.Candidate is not { } releasedCandidate || releasedCandidate.CandidateHash != rules.Fingerprint) continue;
                // Recompute the old key with the old baseline; changed human decisions start a new cycle.
                var oldKey = Hash(JsonSerializer.SerializeToUtf8Bytes(new { review.QueueFingerprint, review.SourceManifestHash,
                    Fingerprint = releasedCandidate.BaselineHash, decisions = review.Reviews.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray() }, Json));
                if (oldKey != receipt.WorkflowKey) continue;
                return new(key, releasedEvent.Revision, "released", review, null, releasedCandidate, "approved") { Release = receipt };
            }
            return new(key, last?.Revision ?? 0, !review.Complete ? (review.Reviewed == 0 ? "review-required" : "review-progress")
                : last?.Kind switch { "imported" => "candidate", "approved" => "approved", "rejected" => "rejected", "prepared" => "prompt-ready", "release-prepared" => "release-request-ready", _ => "review-complete" },
                review, prompt, candidate, last?.Kind == "release-prepared" ? "approved" : last?.Kind is "approved" or "rejected" ? last.Kind : null)
                { ReleasePrompt = last?.Kind == "release-prepared" ? last.Prompt : null };
        }
    }
    private void Check(MaintenanceWorkflowState state, string key, int revision)
    {
        if (!state.Review.Complete || state.Key != key || state.Revision != revision || state.Stage == "no-update-needed")
            throw new InvalidOperationException("Review, baseline or workflow changed. Reload before continuing.");
    }
    private MaintenanceWorkflowState Append(HumanReviewReport review, MaintenanceWorkflowState state,
        string kind, string reviewer, RuleMaintenancePrompt? prompt = null, MaintenanceCandidate? candidate = null, MaintenanceNoUpdate? noUpdate = null)
    {
        Directory.CreateDirectory(directory); var events = Events(state.Key);
        events.Add(new(state.Revision + 1, kind, reviewer, DateTimeOffset.UtcNow.ToString("O"), prompt, candidate, candidate?.CandidateHash) { NoUpdate = noUpdate });
        var file = Path.Combine(directory, state.Key + ".json"); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(events, Json)); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Read(review);
    }
    public MaintenanceWorkflowState Prepare(HumanReviewReport review, WorkflowRevision request, RuleMaintenancePrompt prompt, string reviewer)
    {
        lock (gate)
        {
            var state = Read(review); Check(state, request.Key, request.Revision);
            if (prompt.RulesetFingerprint != rules.Fingerprint || prompt.ArtifactBundle is null)
                throw new InvalidDataException("Prompt must identify the current immutable evidence and baseline.");
            prompt = prompt with { Prompt = prompt.Prompt + "\nAfter evaluation, follow docs/cheap-triage-maintenance-workflow.md and scripts/package-cheap-triage-candidate.py to produce candidate-result.json with an explicit resultType: CANDIDATE for a new version, or NO_UPDATE_NEEDED when the current rules already match every saved human decision and safety checks pass. Use --result-type NO_UPDATE_NEEDED for the latter; do not invent a version. Return the file for Admin result import. CANDIDATE proceeds to review; NO_UPDATE_NEEDED completes maintenance without approval or release. JSM cannot detect external Codex completion. Approval is not deployment.\n" };
            if (state.Disposition == "rejected" && state.Candidate is { } rejected)
                prompt = prompt with { Prompt = prompt.Prompt + $"\nThis is a revision request. The user rejected candidate {rejected.CandidateVersion} ({rejected.CandidateHash}). Read the retained journal /home/codex/jsm-lab/data/app/cheap-triage-maintenance/{state.Key}.json as data for its metrics and changes. Explain how the revised proposal addresses the rejected candidate; do not simply repackage it.\n" };
            return Append(review, state, "prepared", reviewer, prompt);
        }
    }
    public MaintenanceWorkflowState ImportResult(HumanReviewReport review, MaintenanceResultImport request, string reviewer)
    {
        if (request.Result is not { } result) throw new InvalidDataException("Missing maintenance result.");
        if (result.ResultType == "CANDIDATE" && result.Candidate is not null && result.NoUpdate is null)
            return Import(review, new(request.Key, request.Revision, result.Candidate), reviewer);
        if (result.ResultType != "NO_UPDATE_NEEDED" || result.NoUpdate is not { } n || result.Candidate is not null)
            throw new InvalidDataException("Result must contain exactly one CANDIDATE or NO_UPDATE_NEEDED payload.");
        lock (gate)
        {
            var state = Read(review); Check(state, request.Key, request.Revision);
            if (state.Stage != "prompt-ready" || state.Prompt?.ArtifactBundle != n.SnapshotBundle ||
                n.RulesetHash != rules.Fingerprint || n.RulesetVersion != rules.Version ||
                n.QueueFingerprint != review.QueueFingerprint || n.SourceManifestHash != review.SourceManifestHash)
                throw new InvalidOperationException("No-update result references stale rules, review or prepared snapshot.");
            if (n.HumanMatches is null || n.HumanMatches.Length != review.Reviewed ||
                n.HumanMatches.Select(x => x?.Id).Distinct(StringComparer.Ordinal).Count() != review.Reviewed ||
                n.HumanMatches.Any(x => x is null || x.Id is null || !review.Reviews.TryGetValue(x.Id, out var saved) ||
                    x.Human != saved || x.Decision is not ("KEEP" or "REJECT") || x.Decision != saved.Decision))
                throw new InvalidDataException("Every exact saved human decision and revision must match the reported current-rule outcome.");
            var m = n.Metrics;
            if (m is null || new[] { m.KeepRecall,m.DescribedKeepRecall,m.TitleOnlyKeepRecall,m.RejectionRate,m.OldCacheRejectionRate }
                .Any(v => !double.IsFinite(v) || v < 0 || v > 1) || m.FalseRejectCount < 0 ||
                m.KeepRecall < .98 || m.DescribedKeepRecall < .98 || m.TitleOnlyKeepRecall < .98 ||
                n.ChangedDecisionCount != 0 || n.ValidationStatus != "PASS" || n.Warnings is null ||
                n.SafetyFailures is null || n.SafetyFailures.Length != 0 ||
                n.EvaluationArtifactHash is null || n.EvaluationArtifactHash.Length != 64 || !n.EvaluationArtifactHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("No-update completion requires valid high-recall metrics, zero changes, PASS and no reported safety failures.");
            if (JsonSerializer.SerializeToUtf8Bytes(n, Json).Length > 2_000_000)
                throw new InvalidDataException("No-update result exceeds the 2 MB package limit.");
            return Append(review, state, "no-update-needed", reviewer, noUpdate: n);
        }
    }
    public MaintenanceWorkflowState Import(HumanReviewReport review, CandidateImport request, string reviewer)
    {
        lock (gate)
        {
            var state = Read(review); Check(state, request.Key, request.Revision);
            var c = request.Candidate;
            if (c is null || JsonSerializer.SerializeToUtf8Bytes(c, Json).Length > 2_000_000) throw new InvalidDataException("Missing or oversized candidate package.");
            if (state.Prompt is null || c.SnapshotBundle != state.Prompt.ArtifactBundle ||
                c.BaselineHash != rules.Fingerprint || c.BaselineVersion != rules.Version)
                throw new InvalidOperationException("Candidate references different review evidence or baseline. Prepare and evaluate the current snapshot.");
            if (string.IsNullOrWhiteSpace(c.RulesetJson) || Encoding.UTF8.GetByteCount(c.RulesetJson) > 1_000_000)
                throw new InvalidDataException("Missing or oversized candidate rules.");
            var candidate = CheapRejectRules.Parse(Encoding.UTF8.GetBytes(c.RulesetJson));
            if (candidate.Fingerprint != c.CandidateHash || candidate.Version != c.CandidateVersion || candidate.Version == rules.Version)
                throw new InvalidDataException("Candidate identity does not match its rules file, or reuses the baseline version.");
            foreach (var m in new[] { c.Before, c.After })
                if (m is null || new[] {m.KeepRecall,m.DescribedKeepRecall,m.TitleOnlyKeepRecall,m.RejectionRate,m.OldCacheRejectionRate}.Any(v => !double.IsFinite(v) || v < 0 || v > 1) || m.FalseRejectCount < 0)
                    throw new InvalidDataException("Invalid or missing candidate metrics.");
            if (c.HumanCorrections < 0 || c.HumanCorrections > review.Reviewed || c.ChangedDecisions is null ||
                c.Warnings is null || c.SafetyFailures is null || c.ValidationStatus is not ("PASS" or "FAIL" or "INCOMPLETE"))
                throw new InvalidDataException("Candidate must include corrections, all changed decisions, warnings, safety and validation status.");
            foreach (var change in c.ChangedDecisions)
                if (change.ValueKind != JsonValueKind.Object || !change.TryGetProperty("id", out _) ||
                    !change.TryGetProperty("before", out var before) || !change.TryGetProperty("after", out var after) ||
                    !before.TryGetProperty("decision", out var bd) || !after.TryGetProperty("decision", out var ad) ||
                    bd.GetString() is not ("KEEP" or "REJECT") || ad.GetString() is not ("KEEP" or "REJECT"))
                    throw new InvalidDataException("Changed decisions must include IDs and before/after decisions.");
            return Append(review, state, "imported", reviewer, candidate: c);
        }
    }
    public MaintenanceWorkflowState Decide(HumanReviewReport review, CandidateDisposition request, string reviewer)
    {
        lock (gate)
        {
            var state = Read(review); Check(state, request.Key, request.Revision);
            if (state.Candidate is not { } c || c.CandidateHash != request.CandidateHash || request.Decision is not ("approved" or "rejected"))
                throw new InvalidOperationException("Select the current imported candidate and a valid disposition.");
            if (request.Decision == "approved" && (c.ValidationStatus != "PASS" || c.SafetyFailures.Length != 0 ||
                c.After.KeepRecall < c.Before.KeepRecall || c.After.DescribedKeepRecall < c.Before.DescribedKeepRecall ||
                c.After.TitleOnlyKeepRecall < c.Before.TitleOnlyKeepRecall || c.After.FalseRejectCount > c.Before.FalseRejectCount ||
                c.After.KeepRecall < .98 || c.After.DescribedKeepRecall < .98 || c.ChangedDecisions.Any(x =>
                    x.GetProperty("before").GetProperty("decision").GetString() == "KEEP" && x.GetProperty("after").GetProperty("decision").GetString() == "REJECT")))
                throw new InvalidOperationException("Approval blocked by reported safety failures, new false-reject risk or incomplete validation.");
            return Append(review, state, request.Decision, reviewer, candidate: c);
        }
    }
    public MaintenanceWorkflowState PrepareRelease(HumanReviewReport review, WorkflowRevision request, string reviewer)
    {
        lock (gate)
        {
            var state = Read(review); Check(state, request.Key, request.Revision);
            if (state.Disposition != "approved" || state.Candidate is not { } candidate || state.Prompt is null)
                throw new InvalidOperationException("Approve the current candidate before preparing release.");
            var template = File.ReadAllText(Path.Combine(environment.ContentRootPath, "CheapTriage", "release-prompt-v1.txt"));
            var text = template.Replace("{{repository}}", config["CheapTriage:RepositoryPath"] ?? @"D:\Computer Everything\Programming\CS\JobSearchManager")
                .Replace("{{candidateVersion}}", candidate.CandidateVersion).Replace("{{candidateHash}}", candidate.CandidateHash)
                .Replace("{{baselineHash}}", candidate.BaselineHash).Replace("{{bundle}}", candidate.SnapshotBundle).Replace("{{workflowKey}}", state.Key);
            return Append(review, state, "release-prepared", reviewer,
                state.Prompt with { TemplateVersion = "release-1.0.0", Prompt = text }, candidate);
        }
    }
    public MaintenanceResult ReadInbox()
    {
        var path = Path.Combine(directory, "candidate-inbox.json");
        if (!File.Exists(path)) throw new InvalidOperationException("No synced result. Supply candidate-inbox.json using scripts/sync-cheap-triage-candidate.ps1, or import a result file.");
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Candidate package exceeds 2 MB.");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.TryGetProperty("resultType", out _))
            return JsonSerializer.Deserialize<MaintenanceResult>(bytes, Json) ?? throw new InvalidDataException("Empty result.");
        // Legacy files remain candidates; a same-version file is never inferred to be a no-update result.
        return new("CANDIDATE", JsonSerializer.Deserialize<MaintenanceCandidate>(bytes, Json)
            ?? throw new InvalidDataException("Empty package."), null);
    }
}
