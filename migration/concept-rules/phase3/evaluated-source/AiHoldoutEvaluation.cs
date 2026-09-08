using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public static class AiHoldoutEvaluationStates
{
    public const string NotStarted = "not-started";
    public const string LabelingPassA = "labeling-pass-a";
    public const string LabelingPassB = "labeling-pass-b";
    public const string ComparingLabels = "comparing-labels";
    public const string Adjudicating = "adjudicating-disagreements";
    public const string Freezing = "freezing-reference-labels";
    public const string Scoring = "scoring-regex";
    public const string Calculating = "calculating-metrics";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public static class AiReferenceJudgments
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Unresolved = "unresolved";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Present, Absent, Unresolved], StringComparer.Ordinal);
}

public sealed record AiReviewerIdentity(string Role, string Engine, string ModelConfiguration,
    string PromptVersion, string PromptFingerprint, DateTimeOffset CompletedUtc);

public sealed record AiHoldoutRunManifest(int Version, string HoldoutFile,
    string ExpectedSampleFingerprint, AiReviewerIdentity LabelerA, AiReviewerIdentity LabelerB,
    AiReviewerIdentity Adjudicator, string Disclaimer,
    string? Notes = null);

public sealed record AiLabelingPassItem(string EvaluationExampleId, string PostingContentHash,
    IReadOnlyList<string> PresentConceptIds, IReadOnlyList<string> UnresolvedConceptIds,
    int ReviewedConceptCount);

public sealed record AiAdjudicationItem(string EvaluationExampleId, string PostingContentHash,
    string ConceptId, string LabelerAJudgment, string LabelerBJudgment, string Judgment);

public sealed record AiDisagreementRequest(string EvaluationExampleId, string PostingContentHash,
    string Title, string DescriptionHtml, string ConceptId, string ConceptName,
    string ConceptDefinition, string LabelerAJudgment, string LabelerBJudgment);

public sealed record AiReferenceDecision(string EvaluationExampleId, string PostingContentHash,
    string ConceptId, string LabelerAJudgment, string LabelerBJudgment, bool Agreed,
    string? AdjudicatedJudgment, string FinalReferenceJudgment, string LabelProvenance,
    bool Unresolved, bool Contaminated);

public sealed record AiReferenceDataset(int Version, string DatasetId, string DatasetRole,
    string DatasetDisplayName, string DatasetStatus, DateTimeOffset FrozenUtc,
    string HoldoutSampleFingerprint, string SamplingRunId, long RandomSeed, int PostingCount,
    int ConceptCount, int ConceptDecisionCount, string TaxonomyFingerprint, int TaxonomyVersion,
    AiReviewerIdentity LabelerA, AiReviewerIdentity LabelerB, AiReviewerIdentity Adjudicator,
    int AgreementCount, int DisagreementCount, int AdjudicatedCount, int UnresolvedCount,
    string Disclaimer, IReadOnlyList<AiReferenceDecision> Decisions,
    string ReferenceDatasetFingerprint = "");

public sealed record AiHoldoutConceptMetric(string ConceptId, string ConceptName, int Support,
    int EligibleDecisions, int UnresolvedDecisions, int TruePositive, int FalsePositive,
    int FalseNegative, int TrueNegative, double? Precision, double? Recall, double? F1,
    int Disagreements, double DisagreementRate);

public sealed record AiHoldoutPostingDisagreement(string EvaluationExampleId,
    int Disagreements, double DisagreementRate);

public sealed record AiLabelingAgreement(int TotalDecisions, int Agreements, int Disagreements,
    double DisagreementRate, int Adjudicated, int Unresolved,
    IReadOnlyList<AiHoldoutConceptMetric> PerConcept,
    IReadOnlyList<AiHoldoutPostingDisagreement> HighestDisagreementPostings);

public sealed record AiHoldoutEvaluationReport(string EvaluationRunId, DateTimeOffset EvaluatedUtc,
    string DatasetId, string DatasetRole, string DatasetDisplayName, string Purpose,
    string DatasetFingerprint, string ReferenceLabelFingerprint, string RulesetFingerprint,
    string TaxonomyFingerprint, int TaxonomyVersion, string ConfigurationFingerprint,
    string SamplingRunId, string SamplingMethod, long RandomSeed, int SampleSize,
    string LabelProvenance, string Disclaimer, AiReviewerIdentity LabelerA,
    AiReviewerIdentity LabelerB, AiReviewerIdentity Adjudicator, AiLabelingAgreement Agreement,
    int PostingCount, int TotalConceptDecisions, int EligibleConceptDecisions,
    int UnresolvedExcludedDecisions, int PositiveDecisions, int NegativeDecisions,
    double PositivePrevalence, double NegativePrevalence, RegexAggregateEvaluation Macro,
    RegexAggregateEvaluation Micro, IReadOnlyList<AiHoldoutConceptMetric> Concepts,
    string PrCurveStatus, string EvaluationStatus, string? Notes);

public sealed record AiHoldoutEvaluationStatus(string State, string DisplayState,
    DateTimeOffset UpdatedUtc, string? EvaluationRunId, int Completed, int Total,
    string? Message, string? ReferenceLabelFingerprint, string? ReportFile);

public sealed class AiHoldoutEvaluationService
{
    public const string ExactDisclaimer = "Reference labels were generated through prediction-blinded AI review and adjudication. They are not human-ground-truth labels.";
    private const string ManifestName = "ai-holdout-manifest.json";
    private const string LabelerAName = "labeler-a.jsonl";
    private const string LabelerBName = "labeler-b.jsonl";
    private const string AdjudicationName = "adjudication.jsonl";
    private const string DisagreementsName = "disagreements-for-adjudication.jsonl";
    private const string ReferenceName = "ai-reference-labels-v1.json";
    private const string StatusName = "ai-holdout-status.json";
    private const string LatestReportName = "ai-holdout-report-latest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    private readonly string _directory;
    private readonly RegexSemanticClassifier _classifier;
    private readonly SqliteSemanticRuleStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly RemoteWorkDetector _remote;
    private readonly ExtendedLocationRequirementDetector _extended;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public AiHoldoutEvaluationService(IConfiguration configuration, IHostEnvironment environment,
        HostingConfiguration hosting, RegexSemanticClassifier classifier,
        SqliteSemanticRuleStore store, JobConceptCatalog catalog,
        RemoteWorkDetector remote, ExtendedLocationRequirementDetector extended)
    {
        var configured = configuration["Evaluation:Directory"];
        _directory = Path.GetFullPath(configured ?? Path.Combine(
            hosting.IsContainer ? "/app/data" : environment.ContentRootPath, "evaluation"));
        _classifier = classifier;
        _store = store;
        _catalog = catalog;
        _remote = remote;
        _extended = extended;
        RecoverInterruptedRun();
    }

    internal AiHoldoutEvaluationService(string directory, RegexSemanticClassifier classifier,
        SqliteSemanticRuleStore store, JobConceptCatalog catalog)
    {
        _directory = Path.GetFullPath(directory);
        _classifier = classifier;
        _store = store;
        _catalog = catalog;
        _remote = new();
        _extended = new();
        RecoverInterruptedRun();
    }

    public AiHoldoutEvaluationStatus GetStatus()
    {
        var path = Path.Combine(_directory, StatusName);
        if (!File.Exists(path)) return new(AiHoldoutEvaluationStates.NotStarted, "Not started",
            DateTimeOffset.MinValue, null, 0, 200, null, null, null);
        try
        {
            return JsonSerializer.Deserialize<AiHoldoutEvaluationStatus>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new(AiHoldoutEvaluationStates.Failed, "Failed", DateTimeOffset.UtcNow,
                null, 0, 200, "The durable status file is invalid.", null, null);
        }
    }

    public AiHoldoutEvaluationReport? GetLatestReport()
    {
        var path = Path.Combine(_directory, LatestReportName);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<AiHoldoutEvaluationReport>(File.ReadAllBytes(path), JsonOptions);
    }

    public bool TryStart()
    {
        if (!_runGate.Wait(0)) return false;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(); }
            catch (Exception exception)
            {
                await WriteStatusAsync(new(AiHoldoutEvaluationStates.Failed, "Failed",
                    DateTimeOffset.UtcNow, GetStatus().EvaluationRunId, 0, 0,
                    exception.Message, null, null));
            }
            finally { _runGate.Release(); }
        });
        return true;
    }

    private void RecoverInterruptedRun()
    {
        var status = GetStatus();
        if (status.State is AiHoldoutEvaluationStates.NotStarted or AiHoldoutEvaluationStates.Complete
            or AiHoldoutEvaluationStates.Failed) return;
        var recovered = status with
        {
            State = AiHoldoutEvaluationStates.Failed,
            DisplayState = "Failed",
            UpdatedUtc = DateTimeOffset.UtcNow,
            Message = "The application restarted during evaluation. Completed labeling artifacts remain durable; run the evaluation again to resume safely."
        };
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, StatusName);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(recovered, JsonOptions));
        File.Move(temporary, path, true);
    }

    public async Task<AiHoldoutEvaluationReport> RunAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(_directory);
        var manifest = await ReadRequiredAsync<AiHoldoutRunManifest>(ManifestName, token);
        if (manifest.Version != 1 || manifest.Disclaimer != ExactDisclaimer ||
            Path.GetFileName(manifest.HoldoutFile) != manifest.HoldoutFile ||
            !manifest.HoldoutFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The AI holdout manifest is invalid or uses an unapproved disclaimer.");
        ValidateReviewer(manifest.LabelerA, "Labeler A");
        ValidateReviewer(manifest.LabelerB, "Labeler B");
        ValidateReviewer(manifest.Adjudicator, "Adjudicator");
        await ValidatePromptAsync(manifest.LabelerA, "labeler-a-prompt.txt", token);
        await ValidatePromptAsync(manifest.LabelerB, "labeler-b-prompt.txt", token);
        await ValidatePromptAsync(manifest.Adjudicator, "adjudicator-prompt.txt", token);
        var holdout = await ReadRequiredAsync<HoldoutSampleDocument>(manifest.HoldoutFile, token);
        ValidateFrozenHoldout(holdout, _catalog);
        if (holdout.SampleFingerprint != manifest.ExpectedSampleFingerprint)
            throw new InvalidDataException("The frozen holdout does not match the approved manifest.");
        var runId = Guid.NewGuid().ToString("N");

        await StatusAsync(AiHoldoutEvaluationStates.LabelingPassA, "Labeling pass A",
            runId, 0, holdout.Examples.Count, "Validating prediction-blinded Codex A judgments.", token);
        var passA = await ReadPassAsync(LabelerAName, holdout, token);
        await StatusAsync(AiHoldoutEvaluationStates.LabelingPassA, "Labeling pass A",
            runId, passA.Count, holdout.Examples.Count, "Codex A judgments complete.", token);

        await StatusAsync(AiHoldoutEvaluationStates.LabelingPassB, "Labeling pass B",
            runId, 0, holdout.Examples.Count, "Validating independent Codex B judgments.", token);
        var passB = await ReadPassAsync(LabelerBName, holdout, token);
        await StatusAsync(AiHoldoutEvaluationStates.LabelingPassB, "Labeling pass B",
            runId, passB.Count, holdout.Examples.Count, "Codex B judgments complete.", token);

        await StatusAsync(AiHoldoutEvaluationStates.ComparingLabels, "Comparing labels",
            runId, 0, holdout.Examples.Count * _catalog.Concepts.Count,
            "Comparing A and B without invoking the detector.", token);
        var disagreements = BuildDisagreements(holdout, passA, passB);
        await WriteJsonLinesAsync(Path.Combine(_directory, DisagreementsName), disagreements, token);

        await StatusAsync(AiHoldoutEvaluationStates.Adjudicating, "Adjudicating disagreements",
            runId, 0, disagreements.Count, "Validating disagreement-only Codex adjudication.", token);
        var adjudications = await ReadAdjudicationsAsync(disagreements, token);
        await StatusAsync(AiHoldoutEvaluationStates.Adjudicating, "Adjudicating disagreements",
            runId, adjudications.Count, disagreements.Count, "Adjudication complete.", token);

        await StatusAsync(AiHoldoutEvaluationStates.Freezing, "Freezing reference labels",
            runId, 0, holdout.Examples.Count * _catalog.Concepts.Count,
            "Freezing machine-derived references before any RegEx scoring.", token);
        var references = BuildReferenceDataset(holdout, manifest, passA, passB, adjudications);
        var fingerprint = CalculateReferenceFingerprint(references);
        references = references with { ReferenceDatasetFingerprint = fingerprint };
        var referencePath = Path.Combine(_directory, ReferenceName);
        if (File.Exists(referencePath))
        {
            var frozen = JsonSerializer.Deserialize<AiReferenceDataset>(
                await File.ReadAllBytesAsync(referencePath, token), JsonOptions)
                ?? throw new InvalidDataException("The frozen reference dataset is invalid.");
            if (frozen.ReferenceDatasetFingerprint != fingerprint ||
                CalculateReferenceFingerprint(frozen) != fingerprint)
                throw new InvalidDataException(
                    "Reference judgments changed after freeze. Create a new reference dataset version; the existing frozen references were not modified.");
            references = frozen;
        }
        else
        {
            await WriteAtomicallyAsync(referencePath, references, token);
        }

        // This is the first point in the pipeline at which detector output is computed or revealed.
        await StatusAsync(AiHoldoutEvaluationStates.Scoring, "Scoring RegEx", runId, 0,
            holdout.Examples.Count, "Reference labels are frozen; scoring the current ruleset.", token,
            fingerprint);
        var predictions = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var completed = 0;
        foreach (var example in holdout.Examples)
        {
            token.ThrowIfCancellationRequested();
            var remote = _remote.Analyze(example.Title, "", [], example.DescriptionHtml);
            var extended = _extended.Analyze(example.Title, "", [], example.DescriptionHtml);
            var result = _classifier.Classify(example.Title, example.DescriptionHtml, remote, extended,
                productionUsage: false);
            predictions[example.EvaluationExampleId] = result.Concepts.Select(item => item.ConceptId)
                .ToHashSet(StringComparer.Ordinal);
            completed++;
            if (completed % 20 == 0 || completed == holdout.Examples.Count)
                await StatusAsync(AiHoldoutEvaluationStates.Scoring, "Scoring RegEx", runId,
                    completed, holdout.Examples.Count, "Reference labels are frozen; scoring the current ruleset.",
                    token, fingerprint);
        }

        await StatusAsync(AiHoldoutEvaluationStates.Calculating, "Calculating metrics", runId,
            0, _catalog.Concepts.Count, "Calculating one binary operating point; no PR curve is fabricated.",
            token, fingerprint);
        var report = BuildReport(runId, holdout, manifest, references, predictions);
        var immutableReport = $"ai-holdout-report-{runId}.json";
        await WriteAtomicallyAsync(Path.Combine(_directory, immutableReport), report, token);
        await WriteAtomicallyAsync(Path.Combine(_directory, LatestReportName), report, token);
        await _store.SaveEvaluationAsync(ToLedgerReport(report), token);
        await WriteStatusAsync(new(AiHoldoutEvaluationStates.Complete, "Complete",
            DateTimeOffset.UtcNow, runId, holdout.Examples.Count, holdout.Examples.Count,
            "AI-adjudicated production holdout evaluation complete.", fingerprint,
            immutableReport), token);
        return report;
    }

    internal static void ValidateFrozenHoldout(HoldoutSampleDocument holdout,
        JobConceptCatalog catalog)
    {
        if (holdout.DatasetRole != EvaluationDatasetRoles.ProductionHoldout ||
            holdout.DatasetStatus != "frozen-unlabeled" || holdout.Examples.Count != holdout.Plan.SampleSize ||
            catalog.Concepts.Count != 85 ||
            holdout.Examples.Select(item => item.EvaluationExampleId).Distinct(StringComparer.Ordinal).Count()
                != holdout.Examples.Count ||
            holdout.Examples.Any(item => item.DetectorOutputExposedDuringLabeling ||
                item.UsedForRuleDevelopment || item.ContaminatedUtc is not null ||
                !string.IsNullOrWhiteSpace(item.ContaminationReason) || item.PredictionScore is not null ||
                item.LabelProvenance != "unlabeled"))
            throw new InvalidDataException("The frozen holdout is changed, contaminated, exposed, or invalid.");
        var calculated = Hash(string.Join("\n", holdout.Examples.Select(value =>
            $"{value.EvaluationExampleId}|{value.PostingContentHash}")));
        if (calculated != holdout.SampleFingerprint)
            throw new InvalidDataException("The frozen holdout sample fingerprint does not verify.");
        foreach (var example in holdout.Examples)
            if (Hash($"{example.Title}\n{example.DescriptionHtml}") != example.PostingContentHash)
                throw new InvalidDataException($"Posting content hash failed for {example.EvaluationExampleId}.");
    }

    private async Task<IReadOnlyDictionary<string, AiLabelingPassItem>> ReadPassAsync(string name,
        HoldoutSampleDocument holdout, CancellationToken token)
    {
        var items = await ReadJsonLinesAsync<AiLabelingPassItem>(name, token);
        var byId = items.ToDictionary(item => item.EvaluationExampleId, StringComparer.Ordinal);
        if (byId.Count != holdout.Examples.Count)
            throw new InvalidDataException($"{name} must contain one judgment set for every posting.");
        var allowed = _catalog.Concepts.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var example in holdout.Examples)
        {
            if (!byId.TryGetValue(example.EvaluationExampleId, out var item) ||
                item.PostingContentHash != example.PostingContentHash || item.ReviewedConceptCount != allowed.Count)
                throw new InvalidDataException($"{name} has missing or mismatched posting provenance.");
            var present = item.PresentConceptIds.ToHashSet(StringComparer.Ordinal);
            var unresolved = item.UnresolvedConceptIds.ToHashSet(StringComparer.Ordinal);
            if (present.Count != item.PresentConceptIds.Count ||
                unresolved.Count != item.UnresolvedConceptIds.Count || present.Overlaps(unresolved) ||
                !present.IsSubsetOf(allowed) || !unresolved.IsSubsetOf(allowed))
                throw new InvalidDataException($"{name} contains invalid canonical judgments.");
        }
        return byId;
    }

    private IReadOnlyList<AiDisagreementRequest> BuildDisagreements(HoldoutSampleDocument holdout,
        IReadOnlyDictionary<string, AiLabelingPassItem> passA,
        IReadOnlyDictionary<string, AiLabelingPassItem> passB)
    {
        var result = new List<AiDisagreementRequest>();
        foreach (var example in holdout.Examples)
        foreach (var concept in _catalog.Concepts)
        {
            var a = Judgment(passA[example.EvaluationExampleId], concept.Id);
            var b = Judgment(passB[example.EvaluationExampleId], concept.Id);
            if (a != b) result.Add(new(example.EvaluationExampleId, example.PostingContentHash,
                example.Title, example.DescriptionHtml, concept.Id, concept.DisplayName,
                concept.Definition, a, b));
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, AiAdjudicationItem>> ReadAdjudicationsAsync(
        IReadOnlyList<AiDisagreementRequest> disagreements, CancellationToken token)
    {
        var items = await ReadJsonLinesAsync<AiAdjudicationItem>(AdjudicationName, token);
        var result = new Dictionary<string, AiAdjudicationItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = DecisionKey(item.EvaluationExampleId, item.ConceptId);
            if (!result.TryAdd(key, item) || !AiReferenceJudgments.All.Contains(item.Judgment))
                throw new InvalidDataException("The adjudication output contains a duplicate or invalid judgment.");
        }
        foreach (var expected in disagreements)
        {
            var key = DecisionKey(expected.EvaluationExampleId, expected.ConceptId);
            if (!result.TryGetValue(key, out var actual) ||
                actual.PostingContentHash != expected.PostingContentHash ||
                actual.LabelerAJudgment != expected.LabelerAJudgment ||
                actual.LabelerBJudgment != expected.LabelerBJudgment)
                throw new InvalidDataException("The adjudication output is incomplete or mismatched.");
        }
        if (result.Count != disagreements.Count)
            throw new InvalidDataException("The adjudication output contains judgments not requested.");
        return result;
    }

    private AiReferenceDataset BuildReferenceDataset(HoldoutSampleDocument holdout,
        AiHoldoutRunManifest manifest, IReadOnlyDictionary<string, AiLabelingPassItem> passA,
        IReadOnlyDictionary<string, AiLabelingPassItem> passB,
        IReadOnlyDictionary<string, AiAdjudicationItem> adjudications)
    {
        var decisions = new List<AiReferenceDecision>();
        foreach (var example in holdout.Examples)
        foreach (var concept in _catalog.Concepts)
        {
            var a = Judgment(passA[example.EvaluationExampleId], concept.Id);
            var b = Judgment(passB[example.EvaluationExampleId], concept.Id);
            var agreed = a == b;
            var adjudicated = agreed ? null : adjudications[DecisionKey(example.EvaluationExampleId,
                concept.Id)].Judgment;
            var final = agreed ? a : adjudicated!;
            decisions.Add(new(example.EvaluationExampleId, example.PostingContentHash, concept.Id,
                a, b, agreed, adjudicated, final, agreed
                    ? "prediction-blinded-codex-a-b-agreement"
                    : "prediction-blinded-codex-disagreement-adjudication",
                final == AiReferenceJudgments.Unresolved, false));
        }
        return new(1, $"{holdout.SamplingRunId}-ai-reference-v1",
            EvaluationDatasetRoles.ProductionHoldout, "AI-ADJUDICATED PRODUCTION HOLDOUT",
            "frozen-ai-adjudicated-reference-labels", DateTimeOffset.UtcNow,
            holdout.SampleFingerprint, holdout.SamplingRunId, holdout.Plan.RandomSeed,
            holdout.Examples.Count, _catalog.Concepts.Count, decisions.Count,
            _catalog.Fingerprint, _catalog.Version, manifest.LabelerA, manifest.LabelerB,
            manifest.Adjudicator, decisions.Count(item => item.Agreed),
            decisions.Count(item => !item.Agreed), adjudications.Count,
            decisions.Count(item => item.Unresolved), manifest.Disclaimer, decisions);
    }

    private AiHoldoutEvaluationReport BuildReport(string runId, HoldoutSampleDocument holdout,
        AiHoldoutRunManifest manifest, AiReferenceDataset references,
        IReadOnlyDictionary<string, IReadOnlySet<string>> predictions)
    {
        var metricResult = HoldoutMetricCalculator.Calculate(_catalog, references, predictions);
        var concepts = metricResult.Concepts;
        var highestPostings = references.Decisions.GroupBy(item => item.EvaluationExampleId,
                StringComparer.Ordinal).Select(group => new AiHoldoutPostingDisagreement(group.Key,
                group.Count(item => !item.Agreed),
                (double)group.Count(item => !item.Agreed) / _catalog.Concepts.Count))
            .OrderByDescending(item => item.Disagreements).ThenBy(item => item.EvaluationExampleId,
                StringComparer.Ordinal).Take(20).ToArray();
        var agreement = new AiLabelingAgreement(references.ConceptDecisionCount,
            references.AgreementCount, references.DisagreementCount,
            (double)references.DisagreementCount / references.ConceptDecisionCount,
            references.AdjudicatedCount, references.UnresolvedCount, concepts, highestPostings);
        var eligibleCount = metricResult.EligibleCount;
        var positives = metricResult.PositiveCount;
        var negatives = metricResult.NegativeCount;
        return new(runId, DateTimeOffset.UtcNow, references.DatasetId,
            EvaluationDatasetRoles.ProductionHoldout, references.DatasetDisplayName,
            "Prediction-blinded machine-derived reference evaluation of current production RegEx generalization.",
            holdout.SampleFingerprint, references.ReferenceDatasetFingerprint,
            _classifier.RulesetFingerprint, _catalog.Fingerprint, _catalog.Version,
            ConfigurationFingerprint(_store.Policy), holdout.SamplingRunId,
            holdout.Plan.SamplingMethod, holdout.Plan.RandomSeed, holdout.Examples.Count,
            "AI-adjudicated reference labels; not human ground truth", manifest.Disclaimer,
            manifest.LabelerA, manifest.LabelerB, manifest.Adjudicator, agreement,
            holdout.Examples.Count, references.ConceptDecisionCount, eligibleCount,
            references.UnresolvedCount, positives, negatives,
            eligibleCount == 0 ? 0 : (double)positives / eligibleCount,
            eligibleCount == 0 ? 0 : (double)negatives / eligibleCount, metricResult.Macro,
            metricResult.Micro, concepts,
            "Not available: binary RegEx output yields one precision/recall operating point; no threshold-swept PR curve is scientifically defined.",
            "scored", manifest.Notes);
    }

    private RegexEvaluationReport ToLedgerReport(AiHoldoutEvaluationReport report) => new(
        report.EvaluationRunId, report.EvaluatedUtc, report.RulesetFingerprint,
        report.DatasetFingerprint, report.TaxonomyFingerprint, report.TaxonomyVersion,
        report.ConfigurationFingerprint, 0, _classifier.ActiveRuleCount, [],
        report.Concepts.Select(item => new RegexConceptEvaluationResult(item.ConceptId, item.Support,
            item.TruePositive, item.FalsePositive, item.FalseNegative, item.TrueNegative,
            item.Precision, item.Recall, item.F1)).ToArray(), report.Macro, report.Micro,
        new(null, null, null, 0, 0), new(null, null, null, 0, 0), report.DatasetId,
        report.DatasetRole, report.DatasetDisplayName, report.Purpose, report.LabelProvenance,
        report.SamplingMethod, report.RandomSeed, report.PostingCount, report.EligibleConceptDecisions,
        report.PositiveDecisions, report.NegativeDecisions, report.EvaluationStatus,
        $"Reference fingerprint {report.ReferenceLabelFingerprint}. {report.Disclaimer} {report.PrCurveStatus}");

    private static string Judgment(AiLabelingPassItem item, string conceptId) =>
        item.PresentConceptIds.Contains(conceptId, StringComparer.Ordinal)
            ? AiReferenceJudgments.Present
            : item.UnresolvedConceptIds.Contains(conceptId, StringComparer.Ordinal)
                ? AiReferenceJudgments.Unresolved : AiReferenceJudgments.Absent;

    private static void ValidateReviewer(AiReviewerIdentity reviewer, string role)
    {
        if (reviewer.Role != role || reviewer.Engine != "Codex" ||
            string.IsNullOrWhiteSpace(reviewer.ModelConfiguration) ||
            string.IsNullOrWhiteSpace(reviewer.PromptVersion) ||
            reviewer.PromptFingerprint.Length != 64 ||
            reviewer.PromptFingerprint.Any(value => !Uri.IsHexDigit(value)) ||
            reviewer.CompletedUtc == default)
            throw new InvalidDataException($"{role} provenance is incomplete or invalid.");
    }

    private async Task ValidatePromptAsync(AiReviewerIdentity reviewer, string fileName,
        CancellationToken token)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(path, token))).ToLowerInvariant() !=
            reviewer.PromptFingerprint)
            throw new InvalidDataException($"{reviewer.Role} prompt fingerprint does not verify.");
    }

    internal static string CalculateReferenceFingerprint(AiReferenceDataset references)
    {
        var canonical = string.Join("\n", references.Decisions.Select(item =>
            $"{item.EvaluationExampleId}|{item.PostingContentHash}|{item.ConceptId}|{item.LabelerAJudgment}|{item.LabelerBJudgment}|{item.AdjudicatedJudgment ?? ""}|{item.FinalReferenceJudgment}"));
        return Hash($"{references.HoldoutSampleFingerprint}\n{references.TaxonomyFingerprint}\n{references.LabelerA.PromptFingerprint}\n{references.LabelerB.PromptFingerprint}\n{references.Adjudicator.PromptFingerprint}\n{canonical}");
    }

    private async Task<T> ReadRequiredAsync<T>(string name, CancellationToken token)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) throw new InvalidDataException($"Required evaluation artifact '{name}' is missing.");
        return JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException($"Evaluation artifact '{name}' is invalid.");
    }

    private async Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string name, CancellationToken token)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) throw new InvalidDataException($"Required evaluation artifact '{name}' is missing.");
        var result = new List<T>();
        foreach (var line in await File.ReadAllLinesAsync(path, token))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.Add(JsonSerializer.Deserialize<T>(line, JsonOptions)
                ?? throw new InvalidDataException($"Evaluation artifact '{name}' contains invalid JSON Lines."));
        }
        return result;
    }

    private static async Task WriteJsonLinesAsync<T>(string path, IEnumerable<T> values,
        CancellationToken token)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = File.Create(temporary))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            foreach (var value in values)
                await writer.WriteLineAsync(JsonSerializer.Serialize(value,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)).AsMemory(), token);
        File.Move(temporary, path, true);
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
        File.Move(temporary, path, true);
    }

    private Task StatusAsync(string state, string display, string runId, int completed, int total,
        string message, CancellationToken token, string? referenceFingerprint = null) =>
        WriteStatusAsync(new(state, display, DateTimeOffset.UtcNow, runId, completed, total,
            message, referenceFingerprint, null), token);

    private Task WriteStatusAsync(AiHoldoutEvaluationStatus status,
        CancellationToken token = default) => WriteAtomicallyAsync(Path.Combine(_directory, StatusName),
        status, token);

    private static string DecisionKey(string exampleId, string conceptId) => $"{exampleId}:{conceptId}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static double? Divide(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;
    private static double? Harmonic(double? precision, double? recall) =>
        precision is null || recall is null || precision + recall == 0
            ? precision == 0 && recall == 0 ? 0 : null
            : 2 * precision * recall / (precision + recall);
    private static double? Average(IEnumerable<double?> values)
    {
        var defined = values.Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        return defined.Length == 0 ? null : defined.Average();
    }
    private static string ConfigurationFingerprint(SemanticRulePolicy policy) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
}
