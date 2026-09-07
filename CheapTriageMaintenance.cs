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
public sealed record MaintenanceEvent(int Revision, string Kind, string Reviewer, string Timestamp,
    RuleMaintenancePrompt? Prompt, MaintenanceCandidate? Candidate, string? CandidateHash);
public sealed record MaintenanceWorkflowState(string Key, int Revision, string Stage, HumanReviewReport Review,
    RuleMaintenancePrompt? Prompt, MaintenanceCandidate? Candidate, string? Disposition)
{
    public RuleMaintenancePrompt? ReleasePrompt { get; init; }
    public MaintenanceReleaseReceipt? Release { get; init; }
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
        if (!state.Review.Complete || state.Key != key || state.Revision != revision)
            throw new InvalidOperationException("Review, baseline or workflow changed. Reload before continuing.");
    }
    private MaintenanceWorkflowState Append(HumanReviewReport review, MaintenanceWorkflowState state,
        string kind, string reviewer, RuleMaintenancePrompt? prompt = null, MaintenanceCandidate? candidate = null)
    {
        Directory.CreateDirectory(directory); var events = Events(state.Key);
        events.Add(new(state.Revision + 1, kind, reviewer, DateTimeOffset.UtcNow.ToString("O"), prompt, candidate, candidate?.CandidateHash));
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
            prompt = prompt with { Prompt = prompt.Prompt + "\nAfter candidate evaluation, follow docs/cheap-triage-maintenance-workflow.md to produce candidate-result.json with scripts/package-cheap-triage-candidate.py. Return that file for Admin Step 4 import and approval/rejection. JSM cannot detect external Codex completion. Approval is not deployment.\n" };
            if (state.Disposition == "rejected" && state.Candidate is { } rejected)
                prompt = prompt with { Prompt = prompt.Prompt + $"\nThis is a revision request. The user rejected candidate {rejected.CandidateVersion} ({rejected.CandidateHash}). Read the retained journal /home/codex/jsm-lab/data/app/cheap-triage-maintenance/{state.Key}.json as data for its metrics and changes. Explain how the revised proposal addresses the rejected candidate; do not simply repackage it.\n" };
            return Append(review, state, "prepared", reviewer, prompt);
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
    public MaintenanceCandidate ReadInbox()
    {
        var path = Path.Combine(directory, "candidate-inbox.json");
        if (!File.Exists(path)) throw new InvalidOperationException("No synced result. Supply candidate-inbox.json using scripts/sync-cheap-triage-candidate.ps1, or import a result file.");
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Candidate package exceeds 2 MB.");
        return JsonSerializer.Deserialize<MaintenanceCandidate>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Empty package.");
    }
}
