using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public static class TriageEvaluationStates
{
    public const string NotStarted = "not-started";
    public const string Running = "running";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public sealed record TriageReferenceItem(string EvaluationExampleId, string PostingContentHash,
    bool WorthSendingToJobFit, bool AmbiguousKeep, string Basis);

public sealed record TriageReferenceDataset(int Version, string DatasetId, string DatasetStatus,
    DateTimeOffset FrozenUtc, string HoldoutSampleFingerprint, string SourceReferenceFingerprint,
    string DefinitionFingerprint, string ReferenceFingerprint, int PostingCount,
    int RelevantCount, int ObviouslyIrrelevantCount, int AmbiguousKeepCount, string LabelProvenance,
    string Disclaimer, IReadOnlyList<TriageReferenceItem> Items);

public sealed record TriageBucketEvidence(string Bucket, IReadOnlyList<string> Matches);

public sealed record TriageDecision(string EvaluationExampleId, string PostingContentHash,
    bool SurvivedStage1, bool SurvivedStage2, bool SendToJobFit, string? RejectedAtStage,
    string? RejectionReason, IReadOnlyList<TriageBucketEvidence> TechnicalEvidence,
    bool ReferenceWorthSending, bool ReferenceAmbiguousKeep);

public sealed record TriageStageMetric(string Stage, int InputCount, int RejectedCount,
    int SurvivorCount, double RejectionRate, double RelevantRecall, int FalseNegativeCount,
    double ElapsedMilliseconds);

public sealed record TriageExample(string EvaluationExampleId, string Title, string CompanyId,
    string? RejectedAtStage, string? RejectionReason, bool ReferenceWorthSending,
    bool ReferenceAmbiguousKeep, IReadOnlyList<TriageBucketEvidence> TechnicalEvidence);

public sealed record TriageRuntimeEstimate(string Hardware, double SecondsPerPosting,
    int BatchSize, int Survivors, double BeforeSeconds, double AfterSeconds, double SavedSeconds);

public sealed record TriageEvaluationReport(int Version, string EvaluationRunId,
    DateTimeOffset EvaluatedUtc, string DatasetId, string HoldoutSampleFingerprint,
    string ReferenceFingerprint, string CandidateFingerprint, int PostingCount,
    int RelevantCount, int ObviouslyIrrelevantCount, int AmbiguousKeepCount,
    IReadOnlyList<TriageStageMetric> Stages, int FinalSurvivorCount, double FinalSurvivorRate,
    double FinalRelevantRecall, int FinalFalseNegativeCount, double RejectionPrecision,
    double WorkloadReduction, double TotalElapsedMilliseconds, double AverageMicrosecondsPerPosting,
    IReadOnlyList<string> BucketDefinitions, IReadOnlyList<TriageRuntimeEstimate> RuntimeEstimates,
    IReadOnlyList<TriageExample> RejectedExamples, IReadOnlyList<TriageExample> FalseNegatives,
    IReadOnlyList<TriageExample> AmbiguousSurvivors, string LabelProvenance, string Disclaimer,
    string ProductionBehavior);

public sealed record TriageEvaluationStatus(string State, string DisplayState,
    DateTimeOffset UpdatedUtc, string? EvaluationRunId, int Completed, int Total, string? Message,
    string? ReportFile);

public sealed class TriageEvaluationService
{
    public const string ExactDisclaimer = AiHoldoutEvaluationService.ExactDisclaimer;
    private const string HoldoutName = "holdout.json";
    private const string SourceReferenceName = "ai-reference-labels-v1.json";
    private const string TriageReferenceName = "triage-reference-labels-v2.json";
    private const string LatestReportName = "triage-evaluation-report-latest.json";
    private const string StatusName = "triage-evaluation-status.json";
    private const string Definition = "v2: worth sending is the conservative default; explicit frozen hard-conflict concepts are irrelevant; explicit frozen physical/manual occupational concepts are irrelevant only when no present or unresolved technical role or technical-skill concept exists; generic responsibility concepts are not wheelhouse evidence; unresolved wheelhouse evidence and all otherwise indeterminate postings are ambiguity-kept.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    private static readonly IReadOnlySet<string> WheelhouseConcepts = new HashSet<string>(
    [
        "role.software-engineering", "role.systems-engineering", "role.devops-platform",
        "role.cloud-engineering", "role.ai-ml-engineering", "role.data-engineering",
        "role.data-science", "role.cybersecurity", "role.network-engineering",
        "role.infrastructure-engineering", "role.test-validation-engineering",
        "role.hardware-engineering", "technical.application-development",
        "technical.backend-development", "technical.frontend-development",
        "technical.api-development", "technical.automation-scripting",
        "technical.linux-administration", "technical.windows-administration",
        "technical.containers", "technical.infrastructure-as-code", "technical.networking",
        "technical.cisco-networking", "technical.storage", "technical.virtualization",
        "technical.embedded-systems", "technical.machine-learning",
        "technical.artificial-intelligence", "technical.nlp", "technical.large-language-models",
        "technical.software-development", "technical.linux", "technical.cloud", "technical.cicd"
    ], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> HardConflictConcepts = new HashSet<string>(
    ["work.shipboard", "work.scuba", "work.extended-away-assignment", "work.international-assignment"],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> IrrelevantOccupationConcepts = new HashSet<string>(
    [
        "role.mechanical-maintenance-repair", "role.fabrication-assembly-machining",
        "role.physical-inspection-quality-control", "role.lab-test-technician",
        "role.warehouse-material-handling", "role.manufacturing-production-operations"
    ], StringComparer.Ordinal);

    private readonly string _directory;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public TriageEvaluationService(IConfiguration configuration, IHostEnvironment environment,
        HostingConfiguration hosting)
    {
        var configured = configuration["Evaluation:Directory"];
        _directory = Path.GetFullPath(configured ?? Path.Combine(
            hosting.IsContainer ? "/app/data" : environment.ContentRootPath, "evaluation"));
        RecoverInterruptedRun();
    }

    internal TriageEvaluationService(string directory)
    {
        _directory = Path.GetFullPath(directory);
        RecoverInterruptedRun();
    }

    public TriageEvaluationStatus GetStatus()
    {
        var path = Path.Combine(_directory, StatusName);
        if (!File.Exists(path)) return new(TriageEvaluationStates.NotStarted, "Not started",
            DateTimeOffset.MinValue, null, 0, 200, null, null);
        try
        {
            return JsonSerializer.Deserialize<TriageEvaluationStatus>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new(TriageEvaluationStates.Failed, "Failed", DateTimeOffset.UtcNow, null, 0, 200,
                "The durable triage status file is invalid.", null);
        }
    }

    public TriageEvaluationReport? GetLatestReport()
    {
        var path = Path.Combine(_directory, LatestReportName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<TriageEvaluationReport>(File.ReadAllBytes(path), JsonOptions)
            : null;
    }

    public bool TryStart()
    {
        if (!_runGate.Wait(0)) return false;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(); }
            catch (Exception exception)
            {
                await WriteStatusAsync(new(TriageEvaluationStates.Failed, "Failed",
                    DateTimeOffset.UtcNow, GetStatus().EvaluationRunId, 0, 0, exception.Message, null));
            }
            finally { _runGate.Release(); }
        });
        return true;
    }

    public async Task<TriageReferenceDataset> FreezeReferenceAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(_directory);
        var holdout = await ReadRequiredAsync<HoldoutSampleDocument>(HoldoutName, token);
        var source = await ReadRequiredAsync<AiReferenceDataset>(SourceReferenceName, token);
        var catalog = JobConceptCatalog.LoadDefault();
        AiHoldoutEvaluationService.ValidateFrozenHoldout(holdout, catalog);
        if (source.HoldoutSampleFingerprint != holdout.SampleFingerprint ||
            AiHoldoutEvaluationService.CalculateReferenceFingerprint(source) != source.ReferenceDatasetFingerprint)
            throw new InvalidDataException("The frozen source reference does not verify against the holdout.");

        var definitionFingerprint = Hash(Definition);
        var decisions = source.Decisions.GroupBy(item => item.EvaluationExampleId,
            StringComparer.Ordinal).ToDictionary(group => group.Key,
            group => group.ToDictionary(item => item.ConceptId, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var items = holdout.Examples.Select(example =>
        {
            if (!decisions.TryGetValue(example.EvaluationExampleId, out var posting) ||
                posting.Count != source.ConceptCount)
                throw new InvalidDataException("The source reference is incomplete for a holdout posting.");
            var hard = HardConflictConcepts.Where(id => IsPresent(posting, id)).ToArray();
            var positive = WheelhouseConcepts.Where(id => IsPresent(posting, id)).ToArray();
            var unresolved = WheelhouseConcepts.Where(id => IsUnresolved(posting, id)).ToArray();
            var irrelevant = IrrelevantOccupationConcepts.Where(id => IsPresent(posting, id)).ToArray();
            if (hard.Length > 0)
                return new TriageReferenceItem(example.EvaluationExampleId, example.PostingContentHash,
                    false, false, $"Explicit hard conflict: {string.Join(", ", hard)}");
            if (irrelevant.Length > 0 && positive.Length == 0 && unresolved.Length == 0)
                return new TriageReferenceItem(example.EvaluationExampleId, example.PostingContentHash,
                    false, false, $"Explicit irrelevant occupation without wheelhouse evidence: {string.Join(", ", irrelevant)}");
            if (positive.Length > 0)
                return new TriageReferenceItem(example.EvaluationExampleId, example.PostingContentHash,
                    true, false, $"Frozen wheelhouse evidence: {string.Join(", ", positive)}");
            var basis = unresolved.Length > 0
                ? $"Unresolved wheelhouse evidence; ambiguity kept: {string.Join(", ", unresolved)}"
                : "No conclusive irrelevant evidence; ambiguity kept";
            return new TriageReferenceItem(example.EvaluationExampleId, example.PostingContentHash,
                true, true, basis);
        }).ToArray();
        var canonical = string.Join("\n", items.Select(item =>
            $"{item.EvaluationExampleId}|{item.PostingContentHash}|{item.WorthSendingToJobFit}|{item.AmbiguousKeep}|{item.Basis}"));
        var fingerprint = Hash($"{holdout.SampleFingerprint}\n{source.ReferenceDatasetFingerprint}\n{definitionFingerprint}\n{canonical}");
        var frozen = new TriageReferenceDataset(2, $"{source.DatasetId}-triage-v2",
            "frozen-ai-derived-triage-reference", DateTimeOffset.UtcNow, holdout.SampleFingerprint,
            source.ReferenceDatasetFingerprint, definitionFingerprint, fingerprint, items.Length,
            items.Count(item => item.WorthSendingToJobFit),
            items.Count(item => !item.WorthSendingToJobFit), items.Count(item => item.AmbiguousKeep),
            "Deterministic conservative derivation from frozen prediction-blinded AI-adjudicated concept references; no RegEx or triage predictions used.",
            ExactDisclaimer, items);
        var path = Path.Combine(_directory, TriageReferenceName);
        if (File.Exists(path))
        {
            var existing = await ReadRequiredAsync<TriageReferenceDataset>(TriageReferenceName, token);
            if (existing.ReferenceFingerprint != frozen.ReferenceFingerprint ||
                existing.Items.Count != frozen.Items.Count)
                throw new InvalidDataException("The frozen triage reference exists but does not match the registered derivation.");
            return existing;
        }
        await WriteAtomicallyAsync(path, frozen, token);
        return frozen;
    }

    public async Task<TriageEvaluationReport> RunAsync(CancellationToken token = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        await WriteStatusAsync(new(TriageEvaluationStates.Running, "Running triage", DateTimeOffset.UtcNow,
            runId, 0, 200, "Applying the frozen coarse prefilter to the frozen holdout.", null), token);
        var holdout = await ReadRequiredAsync<HoldoutSampleDocument>(HoldoutName, token);
        var predictions = new List<(EvaluationExample Example, CheapTriageResult Result)>(
            holdout.Examples.Count);
        var stage1Elapsed = TimeSpan.Zero;
        var stage2Elapsed = TimeSpan.Zero;
        foreach (var example in holdout.Examples)
        {
            token.ThrowIfCancellationRequested();
            var triage = CheapTriageClassifier.Classify(example.Title, example.DescriptionHtml);
            stage1Elapsed += triage.Stage1Elapsed;
            stage2Elapsed += triage.Stage2Elapsed;
            predictions.Add((example, triage));
        }

        // The candidate decision set is complete before frozen references are opened.
        var reference = await FreezeReferenceAsync(token);
        var referenceById = reference.Items.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        var decisions = new List<TriageDecision>(holdout.Examples.Count);
        foreach (var (example, triage) in predictions)
        {
            var expected = referenceById[example.EvaluationExampleId];
            decisions.Add(new(example.EvaluationExampleId, example.PostingContentHash,
                triage.SurvivedStage1, triage.SurvivedStage2, triage.SendToJobFit,
                triage.RejectedAtStage, triage.RejectionReason, triage.TechnicalEvidence,
                expected.WorthSendingToJobFit, expected.AmbiguousKeep));
        }
        var relevant = reference.RelevantCount;
        var stage1Survivors = decisions.Where(item => item.SurvivedStage1).ToArray();
        var finalSurvivors = decisions.Where(item => item.SendToJobFit).ToArray();
        var stage1FalseNegatives = decisions.Count(item => !item.SurvivedStage1 && item.ReferenceWorthSending);
        var finalFalseNegatives = decisions.Count(item => !item.SendToJobFit && item.ReferenceWorthSending);
        var rejected = decisions.Where(item => !item.SendToJobFit).ToArray();
        var trueRejects = rejected.Count(item => !item.ReferenceWorthSending);
        var stages = new[]
        {
            Stage("Stage 1 — obvious irrelevant / hard conflict", decisions.Count,
                decisions.Count(item => !item.SurvivedStage1), stage1Survivors.Length,
                relevant, stage1FalseNegatives, stage1Elapsed.TotalMilliseconds),
            Stage("Stage 2 — broad technical relevance", stage1Survivors.Length,
                stage1Survivors.Count(item => !item.SurvivedStage2), finalSurvivors.Length,
                relevant - stage1FalseNegatives,
                stage1Survivors.Count(item => !item.SurvivedStage2 && item.ReferenceWorthSending),
                stage2Elapsed.TotalMilliseconds)
        };
        var byId = holdout.Examples.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        TriageExample Example(TriageDecision item)
        {
            var source = byId[item.EvaluationExampleId];
            return new(item.EvaluationExampleId, source.Title, source.CompanyId, item.RejectedAtStage,
                item.RejectionReason, item.ReferenceWorthSending, item.ReferenceAmbiguousKeep,
                item.TechnicalEvidence);
        }
        var totalMs = stage1Elapsed.TotalMilliseconds + stage2Elapsed.TotalMilliseconds;
        var finalRate = (double)finalSurvivors.Length / decisions.Count;
        var runtime = new[]
        {
            Runtime("GTX 1070", 23.225, finalRate), Runtime("RTX 5080", 7.544, finalRate)
        };
        var report = new TriageEvaluationReport(1, runId, DateTimeOffset.UtcNow,
            reference.DatasetId, holdout.SampleFingerprint, reference.ReferenceFingerprint,
            CheapTriageClassifier.CandidateFingerprint, decisions.Count, reference.RelevantCount,
            reference.ObviouslyIrrelevantCount, reference.AmbiguousKeepCount, stages,
            finalSurvivors.Length, finalRate, Divide(relevant - finalFalseNegatives, relevant),
            finalFalseNegatives, Divide(trueRejects, rejected.Length), 1 - finalRate, totalMs,
            totalMs * 1000 / decisions.Count, CheapTriageClassifier.BucketDefinitions,
            runtime, rejected.Select(Example).ToArray(),
            decisions.Where(item => !item.SendToJobFit && item.ReferenceWorthSending).Select(Example).ToArray(),
            decisions.Where(item => item.SendToJobFit && item.ReferenceAmbiguousKeep).Select(Example).ToArray(),
            reference.LabelProvenance, ExactDisclaimer,
            "Diagnostic evaluation only. Production ingestion and Job Fit behavior are unchanged.");
        var immutableName = $"triage-evaluation-report-{runId}.json";
        await WriteAtomicallyAsync(Path.Combine(_directory, immutableName), report, token);
        await WriteAtomicallyAsync(Path.Combine(_directory, LatestReportName), report, token);
        await WriteStatusAsync(new(TriageEvaluationStates.Complete, "Complete", DateTimeOffset.UtcNow,
            runId, decisions.Count, decisions.Count, "Triage evaluation complete.", immutableName), token);
        return report;
    }

    private void RecoverInterruptedRun()
    {
        var status = GetStatus();
        if (status.State == TriageEvaluationStates.Running)
            WriteStatusAsync(status with { State = TriageEvaluationStates.Failed,
                DisplayState = "Failed", UpdatedUtc = DateTimeOffset.UtcNow,
                Message = "The diagnostic process stopped before its atomic report was written; rerun is safe." })
                .GetAwaiter().GetResult();
    }

    private static TriageStageMetric Stage(string name, int input, int rejected, int survivors,
        int relevantInput, int falseNegatives, double elapsedMs) => new(name, input, rejected,
        survivors, Divide(rejected, input), Divide(relevantInput - falseNegatives, relevantInput),
        falseNegatives, elapsedMs);

    private static TriageRuntimeEstimate Runtime(string hardware, double secondsPerPosting,
        double survivorRate)
    {
        const int batch = 1000;
        var survivors = (int)Math.Round(batch * survivorRate, MidpointRounding.AwayFromZero);
        var before = batch * secondsPerPosting;
        var after = survivors * secondsPerPosting;
        return new(hardware, secondsPerPosting, batch, survivors, before, after, before - after);
    }

    private static bool IsPresent(IReadOnlyDictionary<string, AiReferenceDecision> posting, string id) =>
        posting.TryGetValue(id, out var decision) &&
        decision.FinalReferenceJudgment == AiReferenceJudgments.Present;
    private static bool IsUnresolved(IReadOnlyDictionary<string, AiReferenceDecision> posting, string id) =>
        posting.TryGetValue(id, out var decision) &&
        decision.FinalReferenceJudgment == AiReferenceJudgments.Unresolved;
    private static double Divide(int numerator, int denominator) =>
        denominator == 0 ? 0 : (double)numerator / denominator;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private async Task<T> ReadRequiredAsync<T>(string name, CancellationToken token)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) throw new InvalidDataException($"Required evaluation artifact '{name}' is missing.");
        return JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException($"Evaluation artifact '{name}' is invalid.");
    }
    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
        File.Move(temporary, path, true);
    }
    private Task WriteStatusAsync(TriageEvaluationStatus status, CancellationToken token = default) =>
        WriteAtomicallyAsync(Path.Combine(_directory, StatusName), status, token);
}

internal sealed record CheapTriageResult(bool SurvivedStage1, bool SurvivedStage2,
    bool SendToJobFit, string? RejectedAtStage, string? RejectionReason,
    IReadOnlyList<TriageBucketEvidence> TechnicalEvidence, TimeSpan Stage1Elapsed,
    TimeSpan Stage2Elapsed);

internal static partial class CheapTriageClassifier
{
    private sealed record Signal(string Reason, Regex Pattern);
    private sealed record Bucket(string Name, string Definition, Regex Pattern);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(25);
    private static readonly Signal[] HardConflictSignals =
    [
        S("Shipboard / sea-going assignment", @"\b(shipboard|sea[- ]going|aboard (?:a |the )?(?:ship|vessel)|merchant mariner|deckhand|seaman)\b"),
        S("SCUBA / diving role", @"\b(scuba|deep[- ]sea diver|commercial diver|underwater diver)\b"),
        S("Antarctica / extended deployment", @"\b(antarctic(?:a)?|south pole)\b.{0,100}\b(deploy|deployment|assignment|rotation|season)\b|\b(deploy|deployment|assignment|rotation|season)\b.{0,100}\b(antarctic(?:a)?|south pole)\b"),
        S("Extended away-from-home assignment", @"\b(?:away from home|unaccompanied tour|remote camp)\b|\b(?:rotation|rotational schedule)\b.{0,50}\b(?:weeks? on|days? on)\b")
    ];
    private static readonly Signal[] PhysicalTitleSignals =
    [
        S("Physical electrical utility field role", @"\b(?:power ?line|lineman|lineworker|substation|utility field)\b"),
        S("Physical trade / mechanical maintenance", @"\b(?:aircraft|aviation|industrial|diesel|heavy equipment|maintenance)\s+(?:mechanic|technician)|\bmillwright\b"),
        S("Fabrication / machining trade", @"\b(?:machinist|welder|fabricator|sheet metal worker|cnc operator)\b"),
        S("Warehouse / material handling", @"\b(?:warehouse (?:associate|worker|operator|specialist)|material handler|forklift operator|order picker)\b"),
        S("Manufacturing-floor role", @"\b(?:production|manufacturing|assembly|machine)\s+(?:operator|assembler|worker)|\bassembly line\b"),
        S("Physical inspection / construction field role", @"\b(?:construction|building|bridge|highway|roadway|welding|coating)\s+inspector\b|\bquality control inspector\b"),
        S("Civil / roadway engineering", @"\b(?:roadway|highway|transportation|traffic|civil site)\s+(?:engineer|designer)\b"),
        S("Facilities maintenance", @"\b(?:facilities|facility|building maintenance)\s+(?:technician|mechanic|worker|engineer)\b"),
        S("Cabling / racking installation role", @"\b(?:cable|cabling|fiber|structured cabling)\s+(?:installer|technician)|\brack and stack technician\b"),
        S("Field-service technician", @"\bfield service (?:technician|mechanic)\b")
    ];
    private static readonly Signal[] NonTechnicalTitleSignals =
    [
        S("Retail / food / hospitality occupation", @"\b(?:cashier|barista|server|bartender|cook|chef|housekeeper|front desk agent|store associate)\b"),
        S("Clinical / direct-care occupation", @"\b(?:registered nurse|licensed practical nurse|nursing assistant|medical assistant|dental hygienist|phlebotomist|physical therapist)\b"),
        S("Sales / account occupation without technical evidence", @"\b(?:sales representative|account executive|insurance agent|real estate agent|loan officer)\b"),
        S("Administrative / human-resources occupation without technical evidence", @"\b(?:administrative assistant|executive assistant|recruiter|human resources (?:generalist|specialist)|payroll specialist)\b"),
        S("Finance / legal occupation without technical evidence", @"\b(?:accountant|bookkeeper|paralegal|attorney|financial advisor|tax specialist)\b")
    ];
    private static readonly Bucket[] Buckets =
    [
        B("Software / application development", "Programming, application, backend, frontend, API, or software delivery evidence.", @"\b(?:software|application|backend|back-end|frontend|front-end|full[- ]stack|developer|programmer|\.net|c#|java|python|javascript|typescript|api|microservices?)\b"),
        B("Cloud / platform / DevOps", "Cloud platforms, platform engineering, DevOps, containers, infrastructure as code, or CI/CD.", @"\b(?:cloud|azure|aws|amazon web services|gcp|google cloud|devops|devsecops|platform engineer|kubernetes|docker|terraform|ansible|infrastructure as code|ci/?cd|continuous integration)\b"),
        B("Systems / infrastructure / administration", "IT systems, servers, operating systems, virtualization, storage, or infrastructure engineering.", @"\b(?:systems? (?:engineer|administrator|administration)|sysadmin|linux|windows server|active directory|vmware|virtualization|storage engineer|infrastructure engineer|server administration)\b"),
        B("Automation / scripting", "Automation engineering, scripting, orchestration, or repeatable technical tooling.", @"\b(?:automation|scripting|powershell|bash|shell script|python script|orchestration|workflow engineer)\b"),
        B("IT networking", "Computer networking, network administration/engineering, routing, switching, or network protocols.", @"\b(?:network (?:engineer|administrator|architect|operations)|networking|tcp/?ip|cisco|router|routing|switching|firewall|dns|dhcp|lan|wan)\b"),
        B("Cybersecurity", "Security engineering, operations, automation, application security, or defensive/offensive cyber work.", @"\b(?:cybersecurity|cyber security|security engineer|security operations|soc analyst|siem|devsecops|application security|penetration test|incident response|vulnerability management)\b"),
        B("Data / AI / ML", "Data engineering/science, analytics engineering, machine learning, NLP, or LLM work.", @"\b(?:data engineer|data scientist|analytics engineer|machine learning|artificial intelligence|\bai\b|\bml\b|natural language processing|\bnlp\b|large language model|\bllm\b|data pipeline|etl)\b"),
        B("Test / validation automation", "Software test engineering, automated testing, SDET, or technical validation automation.", @"\b(?:test automation|automated test|software test|test engineer|qa engineer|quality assurance engineer|sdet|selenium|playwright|integration test)\b"),
        B("Technical architecture / integration", "Solution/system architecture, systems integration, middleware, or enterprise integration.", @"\b(?:solutions? architect|technical architect|systems? architect|enterprise architect|systems? integration|application integration|integration engineer|middleware)\b"),
        B("Technical support / operations", "Technical support or operations with explicit systems, software, cloud, network, or engineering content.", @"\b(?:technical support engineer|support engineer|site reliability|\bsre\b|noc engineer|cloud operations|systems operations|production support engineer|application support engineer)\b")
    ];
    public static IReadOnlyList<string> BucketDefinitions { get; } = Buckets
        .Select(bucket => $"{bucket.Name}: {bucket.Definition}").ToArray();
    public static string CandidateFingerprint { get; } = Hash(string.Join("\n",
        HardConflictSignals.Concat(PhysicalTitleSignals).Concat(NonTechnicalTitleSignals)
            .Select(signal => $"signal|{signal.Reason}|{signal.Pattern}")) + "\n" +
        string.Join("\n", Buckets.Select(bucket =>
            $"bucket|{bucket.Name}|{bucket.Definition}|{bucket.Pattern}")));

    public static CheapTriageResult Classify(string title, string descriptionHtml)
    {
        var stage1 = Stopwatch.StartNew();
        var titleText = Normalize(title);
        var bodyText = Normalize(descriptionHtml);
        var combined = $"{titleText} {bodyText}";
        var evidence = Buckets.Select(bucket => new TriageBucketEvidence(bucket.Name,
                Matches(bucket.Pattern, combined)))
            .Where(item => item.Matches.Count > 0).ToArray();
        var conflict = HardConflictSignals.FirstOrDefault(signal => signal.Pattern.IsMatch(combined));
        var physical = conflict is null
            ? PhysicalTitleSignals.FirstOrDefault(signal => signal.Pattern.IsMatch(titleText)) : null;
        stage1.Stop();
        if (conflict is not null || (physical is not null && evidence.Length == 0))
        {
            var reason = conflict?.Reason ?? physical!.Reason;
            return new(false, false, false, "stage-1", reason, evidence,
                stage1.Elapsed, TimeSpan.Zero);
        }
        var stage2 = Stopwatch.StartNew();
        var nonTechnical = evidence.Length == 0
            ? NonTechnicalTitleSignals.FirstOrDefault(signal => signal.Pattern.IsMatch(titleText)) : null;
        stage2.Stop();
        if (nonTechnical is not null)
            return new(true, false, false, "stage-2", nonTechnical.Reason, evidence,
                stage1.Elapsed, stage2.Elapsed);
        return new(true, true, true, null, null, evidence, stage1.Elapsed, stage2.Elapsed);
    }

    private static Signal S(string reason, string pattern) => new(reason,
        new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout));
    private static Bucket B(string name, string definition, string pattern) => new(name, definition,
        new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout));
    private static IReadOnlyList<string> Matches(Regex pattern, string text) => pattern.Matches(text)
        .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
    private static string Normalize(string value)
    {
        var decoded = WebUtility.HtmlDecode(TagRegex().Replace(value ?? "", " "));
        return WhitespaceRegex().Replace(decoded, " ").Trim().ToLowerInvariant();
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 25)]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 25)]
    private static partial Regex WhitespaceRegex();
}
