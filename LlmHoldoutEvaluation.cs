using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public static class LlmHoldoutEvaluationStates
{
    public const string NotStarted = "not-started";
    public const string Preparing = "preparing-frozen-holdout";
    public const string Predicting = "running-llm-predictions";
    public const string Freezing = "freezing-predictions";
    public const string Scoring = "scoring-reference-labels";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

public sealed record LlmGenerationConfiguration(
    double Temperature, int Seed, int ContextLength, int MaximumOutputTokens,
    string OutputContract);

public sealed record LlmHoldoutPredictionItem(
    string EvaluationExampleId, string PostingContentHash, DateTimeOffset AnalyzedUtc,
    string ClassificationFingerprint, IReadOnlyList<SemanticConceptPrediction> Predictions,
    double LatencyMilliseconds, QwenInferenceMetrics Inference);

public sealed record LlmHoldoutPredictionDataset(
    int Version, string DatasetId, string DatasetStatus, DateTimeOffset FrozenUtc,
    string HoldoutFingerprint, string TaxonomyFingerprint, int TaxonomyVersion,
    string ModelId, string ModelTag, string ModelDigest, string PromptVersion,
    string PromptHash, LlmGenerationConfiguration GenerationConfiguration,
    int PostingCount, int ConceptCount, IReadOnlyList<LlmHoldoutPredictionItem> Items,
    string PredictionDatasetFingerprint = "");

public sealed record LlmRuntimeMetrics(
    double TotalElapsedSeconds, double AverageLatencyMilliseconds,
    double MedianLatencyMilliseconds, double P95LatencyMilliseconds,
    double PostingsPerMinute, double? WeightedOutputTokensPerSecond,
    long PromptTokenCount, long OutputTokenCount, int ApproximateInferenceCount,
    double? AverageGpuUtilizationPercent, long? PeakModelVramBytes,
    long? PeakModelResidentBytes, long? PeakOllamaContainerRamBytes,
    long? PeakAdapterContainerRamBytes,
    string GpuMeasurementStatus, string ContainerRamMeasurementScope);

public sealed record ExternalLlmResourceObservation(
    string ModelDigest, DateTimeOffset ObservedUtc, int SampleCount,
    double AverageGpuUtilizationPercent, long PeakGpuMemoryUsedBytes,
    long PeakOllamaContainerRamBytes, long PeakAdapterContainerRamBytes,
    string Source);

public sealed record LlmConceptComparison(
    string ConceptId, string ConceptName, int Support, int EligibleDecisions,
    int UnresolvedDecisions, double? RegexPrecision, double? RegexRecall,
    double? RegexF1, double? LlmPrecision, double? LlmRecall, double? LlmF1,
    double? F1Difference, double ReferenceLabelDisagreementRate,
    int LlmTruePositive, int LlmFalsePositive, int LlmFalseNegative, int LlmTrueNegative);

public sealed record LlmMetricDifference(
    double? MacroPrecision, double? MacroRecall, double? MacroF1,
    double? MicroPrecision, double? MicroRecall, double? MicroF1);

public sealed record LlmHoldoutEvaluationReport(
    string EvaluationRunId, DateTimeOffset EvaluatedUtc, string DatasetId,
    string DatasetRole, string DatasetDisplayName, string HoldoutFingerprint,
    string ReferenceLabelFingerprint, string PredictionFingerprint,
    string TaxonomyFingerprint, int TaxonomyVersion, string ModelId,
    string ModelTag, string ModelDigest, string PromptVersion, string PromptHash,
    LlmGenerationConfiguration GenerationConfiguration, int PostingCount,
    int TotalConceptDecisions, int EligibleConceptDecisions,
    int UnresolvedExcludedDecisions, int PositiveDecisions, int NegativeDecisions,
    RegexAggregateEvaluation RegexMacro, RegexAggregateEvaluation RegexMicro,
    RegexAggregateEvaluation LlmMacro, RegexAggregateEvaluation LlmMicro,
    LlmMetricDifference AbsoluteDifference,
    IReadOnlyList<LlmConceptComparison> Concepts, LlmRuntimeMetrics Runtime,
    string Disclaimer, string Interpretation, string EvaluationStatus, string Notes);

public sealed record LlmHoldoutEvaluationStatus(
    string State, string DisplayState, DateTimeOffset UpdatedUtc,
    string? EvaluationRunId, int Completed, int Total, string? Message,
    string? PredictionFingerprint, string? ReportFile, DateTimeOffset? StartedUtc = null);

public sealed record LlmEvaluationLedgerDetails(
    string EvaluationRunId, string HoldoutFingerprint, string ReferenceLabelFingerprint,
    string PredictionFingerprint, string ClassifierType, string ModelId, string ModelTag,
    string ModelDigest, string PromptVersion, string PromptHash,
    string GenerationConfigurationJson, string RuntimeMetricsJson,
    string ComparisonMetricsJson, string Limitations);

internal sealed record HoldoutMetricResult(
    IReadOnlyList<AiHoldoutConceptMetric> Concepts,
    RegexAggregateEvaluation Macro, RegexAggregateEvaluation Micro,
    int EligibleCount, int PositiveCount, int NegativeCount);

internal static class HoldoutMetricCalculator
{
    public static HoldoutMetricResult Calculate(JobConceptCatalog catalog,
        AiReferenceDataset references,
        IReadOnlyDictionary<string, IReadOnlySet<string>> predictions)
    {
        var concepts = new List<AiHoldoutConceptMetric>();
        foreach (var concept in catalog.Concepts)
        {
            var values = references.Decisions.Where(item => item.ConceptId == concept.Id).ToArray();
            var eligible = values.Where(item => !item.Unresolved).ToArray();
            var tp = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Present &&
                predictions[item.EvaluationExampleId].Contains(concept.Id));
            var fp = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Absent &&
                predictions[item.EvaluationExampleId].Contains(concept.Id));
            var fn = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Present &&
                !predictions[item.EvaluationExampleId].Contains(concept.Id));
            var tn = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Absent &&
                !predictions[item.EvaluationExampleId].Contains(concept.Id));
            var precision = Divide(tp, tp + fp);
            var recall = Divide(tp, tp + fn);
            concepts.Add(new(concept.Id, concept.DisplayName, tp + fn, eligible.Length,
                values.Length - eligible.Length, tp, fp, fn, tn, precision, recall,
                Harmonic(precision, recall), values.Count(item => !item.Agreed),
                values.Length == 0 ? 0 : (double)values.Count(item => !item.Agreed) / values.Length));
        }
        var macro = new RegexAggregateEvaluation(Average(concepts.Select(item => item.Precision)),
            Average(concepts.Select(item => item.Recall)), Average(concepts.Select(item => item.F1)),
            concepts.Count, concepts.Sum(item => item.EligibleDecisions));
        var totalTp = concepts.Sum(item => item.TruePositive);
        var totalFp = concepts.Sum(item => item.FalsePositive);
        var totalFn = concepts.Sum(item => item.FalseNegative);
        var microPrecision = Divide(totalTp, totalTp + totalFp);
        var microRecall = Divide(totalTp, totalTp + totalFn);
        var micro = new RegexAggregateEvaluation(microPrecision, microRecall,
            Harmonic(microPrecision, microRecall), concepts.Count,
            concepts.Sum(item => item.EligibleDecisions));
        var eligibleCount = concepts.Sum(item => item.EligibleDecisions);
        var positives = concepts.Sum(item => item.Support);
        return new(concepts, macro, micro, eligibleCount, positives, eligibleCount - positives);
    }

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
}

public sealed class LlmHoldoutEvaluationService
{
    public const string ExactDisclaimer = AiHoldoutEvaluationService.ExactDisclaimer;
    private const string HoldoutName = "holdout.json";
    private const string ReferenceName = "ai-reference-labels-v1.json";
    private const string ProgressName = "llm-predictions-progress-v2.json";
    private const string PredictionsName = "llm-predictions-v2.json";
    private const string StatusName = "llm-holdout-status.json";
    private const string LatestReportName = "llm-holdout-report-latest.json";
    private const string ExternalResourceName = "llm-holdout-resource-observation-v2.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    private readonly string _directory;
    private readonly Func<ClassifierRequest, CancellationToken, Task<QwenDeepAnalysis?>> _classify;
    private readonly SqliteSemanticRuleStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly Func<AiHoldoutEvaluationReport?> _regexReport;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public LlmHoldoutEvaluationService(IConfiguration configuration, IHostEnvironment environment,
        HostingConfiguration hosting, ClassifierClient classifier, SqliteSemanticRuleStore store,
        JobConceptCatalog catalog, AiHoldoutEvaluationService regexHoldout)
        : this(Path.GetFullPath(configuration["Evaluation:Directory"] ?? Path.Combine(
                hosting.IsContainer ? "/app/data" : environment.ContentRootPath, "evaluation")),
            classifier.DeepAnalyzeAsync, store, catalog, regexHoldout.GetLatestReport)
    { }

    internal LlmHoldoutEvaluationService(string directory,
        Func<ClassifierRequest, CancellationToken, Task<QwenDeepAnalysis?>> classify,
        SqliteSemanticRuleStore store, JobConceptCatalog catalog,
        Func<AiHoldoutEvaluationReport?> regexReport)
    {
        _directory = Path.GetFullPath(directory);
        _classify = classify;
        _store = store;
        _catalog = catalog;
        _regexReport = regexReport;
        RecoverInterruptedRun();
    }

    public object GetCurrentModelInfo() => new
    {
        concept = "LLM",
        modelId = QwenDeepAnalysisContract.ModelId,
        modelTag = QwenDeepAnalysisContract.ModelTag,
        modelDigest = QwenDeepAnalysisContract.ModelDigest,
        promptVersion = QwenDeepAnalysisContract.PromptVersion,
        promptHash = QwenDeepAnalysisContract.PromptHash,
        outputContractVersion = QwenDeepAnalysisContract.OutputContractVersion,
        outputSchemaHash = QwenDeepAnalysisContract.OutputSchemaHash,
        taxonomyVersion = _catalog.Version,
        taxonomyFingerprint = _catalog.Fingerprint,
        generationConfiguration = GenerationConfiguration()
    };

    public LlmHoldoutEvaluationStatus GetStatus()
    {
        var path = Path.Combine(_directory, StatusName);
        if (!File.Exists(path)) return new(LlmHoldoutEvaluationStates.NotStarted, "Not started",
            DateTimeOffset.MinValue, null, 0, 200, null, null, null);
        try
        {
            return JsonSerializer.Deserialize<LlmHoldoutEvaluationStatus>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new(LlmHoldoutEvaluationStates.Failed, "Failed", DateTimeOffset.UtcNow,
                null, 0, 200, "The durable LLM evaluation status file is invalid.", null, null);
        }
    }

    public LlmHoldoutEvaluationReport? GetLatestReport()
    {
        var path = Path.Combine(_directory, LatestReportName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<LlmHoldoutEvaluationReport>(File.ReadAllBytes(path), JsonOptions)
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
                await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Failed, "Failed",
                    DateTimeOffset.UtcNow, GetStatus().EvaluationRunId, GetStatus().Completed, 200,
                    exception.Message, null, null, GetStatus().StartedUtc));
            }
            finally { _runGate.Release(); }
        });
        return true;
    }

    public async Task<LlmHoldoutEvaluationReport> RunAsync(CancellationToken token = default)
    {
        try { return await RunCoreAsync(token); }
        catch (Exception exception)
        {
            var current = GetStatus();
            await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Failed, "Failed",
                DateTimeOffset.UtcNow, current.EvaluationRunId, current.Completed,
                current.Total, exception.Message, null, null, current.StartedUtc),
                CancellationToken.None);
            throw;
        }
    }

    private async Task<LlmHoldoutEvaluationReport> RunCoreAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        var runId = Guid.NewGuid().ToString("N");
        await StatusAsync(LlmHoldoutEvaluationStates.Preparing, "Preparing frozen holdout",
            runId, 0, 200, "Validating the unchanged unlabeled holdout without opening references.", token);
        var holdout = await ReadRequiredAsync<HoldoutSampleDocument>(HoldoutName, token);
        AiHoldoutEvaluationService.ValidateFrozenHoldout(holdout, _catalog);
        if (holdout.Examples.Count != 200)
            throw new InvalidDataException("The apples-to-apples LLM evaluation requires the exact frozen 200-posting holdout.");

        var frozenPath = Path.Combine(_directory, PredictionsName);
        LlmHoldoutPredictionDataset frozen;
        if (File.Exists(frozenPath))
        {
            frozen = await ReadRequiredAsync<LlmHoldoutPredictionDataset>(PredictionsName, token);
            ValidateFrozenPredictions(frozen, holdout);
        }
        else
        {
            var progress = await ReadProgressAsync(holdout, token);
            await StatusAsync(LlmHoldoutEvaluationStates.Predicting, "Running LLM predictions",
                runId, progress.Count, holdout.Examples.Count,
                "Reference labels, RegEx output, rules, scores, and Codex judgments are not loaded or sent.", token);
            foreach (var example in holdout.Examples.Where(item =>
                         !progress.ContainsKey(item.EvaluationExampleId)))
            {
                token.ThrowIfCancellationRequested();
                var timer = Stopwatch.StartNew();
                var result = await _classify(new(example.EvaluationExampleId, example.Title,
                    example.DescriptionHtml), token);
                timer.Stop();
                if (result?.Inference is null)
                    throw new InvalidDataException($"The first LLM prediction failed strict validation for {example.EvaluationExampleId}; no retry or alternate prompt was used.");
                ValidatePrediction(result, example);
                progress.Add(example.EvaluationExampleId, new(example.EvaluationExampleId,
                    example.PostingContentHash, result.AnalyzedUtc, result.ClassificationFingerprint,
                    result.Predictions, timer.Elapsed.TotalMilliseconds, result.Inference));
                await WriteAtomicallyAsync(Path.Combine(_directory, ProgressName),
                    holdout.Examples.Where(item => progress.ContainsKey(item.EvaluationExampleId))
                        .Select(item => progress[item.EvaluationExampleId]).ToArray(), token);
                await StatusAsync(LlmHoldoutEvaluationStates.Predicting, "Running LLM predictions",
                    runId, progress.Count, holdout.Examples.Count,
                    "First valid prediction per posting is retained; no holdout-driven retries or tuning.", token);
            }
            await StatusAsync(LlmHoldoutEvaluationStates.Freezing, "Freezing predictions",
                runId, progress.Count, holdout.Examples.Count,
                "All predictions are complete; freezing them before reference comparison.", token);
            frozen = new(2, $"{holdout.SamplingRunId}-qwen-predictions-v2", "frozen-llm-predictions",
                DateTimeOffset.UtcNow, holdout.SampleFingerprint, _catalog.Fingerprint,
                _catalog.Version, QwenDeepAnalysisContract.ModelId, QwenDeepAnalysisContract.ModelTag,
                QwenDeepAnalysisContract.ModelDigest, QwenDeepAnalysisContract.PromptVersion,
                QwenDeepAnalysisContract.PromptHash, GenerationConfiguration(),
                holdout.Examples.Count, _catalog.Concepts.Count,
                holdout.Examples.Select(item => progress[item.EvaluationExampleId]).ToArray());
            frozen = frozen with { PredictionDatasetFingerprint = PredictionFingerprint(frozen) };
            await WriteAtomicallyAsync(frozenPath, frozen, token);
        }

        // References are deliberately opened only after the complete prediction dataset is frozen.
        await StatusAsync(LlmHoldoutEvaluationStates.Scoring, "Scoring against reference labels",
            runId, holdout.Examples.Count, holdout.Examples.Count,
            "Frozen LLM predictions are now being compared with the existing frozen references.", token,
            frozen.PredictionDatasetFingerprint);
        var references = await ReadRequiredAsync<AiReferenceDataset>(ReferenceName, token);
        ValidateReferences(references, holdout);
        var regex = _regexReport() ?? throw new InvalidDataException(
            "The persisted RegEx production-holdout baseline is required for apples-to-apples comparison.");
        if (regex.DatasetFingerprint != holdout.SampleFingerprint ||
            regex.ReferenceLabelFingerprint != references.ReferenceDatasetFingerprint ||
            regex.EligibleConceptDecisions != references.ConceptDecisionCount - references.UnresolvedCount)
            throw new InvalidDataException("The persisted RegEx baseline does not use the same frozen experiment.");

        var predictions = frozen.Items.ToDictionary(item => item.EvaluationExampleId,
            item => (IReadOnlySet<string>)item.Predictions.Where(value => value.Matched)
                .Select(value => value.ConceptId).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var metrics = HoldoutMetricCalculator.Calculate(_catalog, references, predictions);
        var regexConcepts = regex.Concepts.ToDictionary(item => item.ConceptId, StringComparer.Ordinal);
        var comparisons = metrics.Concepts.Select(item =>
        {
            var baseline = regexConcepts[item.ConceptId];
            return new LlmConceptComparison(item.ConceptId, item.ConceptName, item.Support,
                item.EligibleDecisions, item.UnresolvedDecisions, baseline.Precision, baseline.Recall,
                baseline.F1, item.Precision, item.Recall, item.F1,
                Difference(item.F1, baseline.F1), item.DisagreementRate,
                item.TruePositive, item.FalsePositive, item.FalseNegative, item.TrueNegative);
        }).ToArray();
        var runtime = Runtime(frozen,
            await ReadExternalResourceObservationAsync(frozen.Items, token));
        var report = new LlmHoldoutEvaluationReport(runId, DateTimeOffset.UtcNow,
            references.DatasetId, EvaluationDatasetRoles.ProductionHoldout,
            "LLM · AI-ADJUDICATED PRODUCTION HOLDOUT", holdout.SampleFingerprint,
            references.ReferenceDatasetFingerprint, frozen.PredictionDatasetFingerprint,
            _catalog.Fingerprint, _catalog.Version, frozen.ModelId, frozen.ModelTag,
            frozen.ModelDigest, frozen.PromptVersion, frozen.PromptHash,
            frozen.GenerationConfiguration, holdout.Examples.Count,
            references.ConceptDecisionCount, metrics.EligibleCount, references.UnresolvedCount,
            metrics.PositiveCount, metrics.NegativeCount, regex.Macro, regex.Micro,
            metrics.Macro, metrics.Micro, new(
                AbsDifference(metrics.Macro.Precision, regex.Macro.Precision),
                AbsDifference(metrics.Macro.Recall, regex.Macro.Recall),
                AbsDifference(metrics.Macro.F1, regex.Macro.F1),
                AbsDifference(metrics.Micro.Precision, regex.Micro.Precision),
                AbsDifference(metrics.Micro.Recall, regex.Micro.Recall),
                AbsDifference(metrics.Micro.F1, regex.Micro.F1)), comparisons, runtime,
            ExactDisclaimer,
            "Apples-to-apples agreement with one AI-derived reference standard is not absolute human-grounded truth. Interpret high-disagreement and low-support concepts cautiously.",
            "scored",
            "Qwen did not participate in reference creation. Predictions were frozen before references were opened. The versioned v2 compact structured-output contract was fixed before this fresh run; no holdout label, score, RegEx rule, taxonomy, model, or production Job Fit behavior informed or changed it.");
        var immutableName = $"llm-holdout-report-{runId}.json";
        await WriteAtomicallyAsync(Path.Combine(_directory, immutableName), report, token);
        await WriteAtomicallyAsync(Path.Combine(_directory, LatestReportName), report, token);
        await _store.SaveLlmEvaluationAsync(ToLedgerReport(report), ToLedgerDetails(report), token);
        await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Complete, "Complete",
            DateTimeOffset.UtcNow, runId, holdout.Examples.Count, holdout.Examples.Count,
            "LLM production-holdout evaluation complete.", frozen.PredictionDatasetFingerprint,
            immutableName, GetStatus().StartedUtc), token);
        return report;
    }

    private async Task<Dictionary<string, LlmHoldoutPredictionItem>> ReadProgressAsync(
        HoldoutSampleDocument holdout, CancellationToken token)
    {
        var path = Path.Combine(_directory, ProgressName);
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        var items = JsonSerializer.Deserialize<IReadOnlyList<LlmHoldoutPredictionItem>>(
            await File.ReadAllBytesAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException("The durable LLM prediction progress is invalid.");
        var examples = holdout.Examples.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        var result = new Dictionary<string, LlmHoldoutPredictionItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!examples.TryGetValue(item.EvaluationExampleId, out var example) ||
                !result.TryAdd(item.EvaluationExampleId, item))
                throw new InvalidDataException("The durable LLM prediction progress is duplicated or outside the holdout.");
            ValidatePrediction(item, example);
        }
        return result;
    }

    private void ValidatePrediction(QwenDeepAnalysis result, EvaluationExample example)
    {
        if (result.PostingContentHash != example.PostingContentHash ||
            result.ModelId != QwenDeepAnalysisContract.ModelId ||
            result.ModelTag != QwenDeepAnalysisContract.ModelTag ||
            result.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            result.TaxonomyVersion != _catalog.Version ||
            result.TaxonomyFingerprint != _catalog.Fingerprint ||
            result.PromptVersion != QwenDeepAnalysisContract.PromptVersion ||
            result.PromptHash != QwenDeepAnalysisContract.PromptHash ||
            result.ClassificationFingerprint != QwenDeepAnalysisContract.ClassificationFingerprint(
                example.PostingContentHash, _catalog) || !ValidPredictions(result.Predictions))
            throw new InvalidDataException("An LLM prediction failed frozen identity, taxonomy, prompt, or posting validation.");
    }

    private void ValidatePrediction(LlmHoldoutPredictionItem item, EvaluationExample example)
    {
        if (item.PostingContentHash != example.PostingContentHash || item.Inference is null ||
            item.ClassificationFingerprint != QwenDeepAnalysisContract.ClassificationFingerprint(
                example.PostingContentHash, _catalog) || !ValidPredictions(item.Predictions) ||
            item.LatencyMilliseconds < 0)
            throw new InvalidDataException("A durable LLM prediction failed provenance validation.");
    }

    private bool ValidPredictions(IReadOnlyList<SemanticConceptPrediction> predictions) =>
        predictions.Count == _catalog.Concepts.Count &&
        predictions.Select(item => item.ConceptId).Distinct(StringComparer.Ordinal).Count() ==
            _catalog.Concepts.Count && predictions.All(item => _catalog.Contains(item.ConceptId));

    private void ValidateFrozenPredictions(LlmHoldoutPredictionDataset value,
        HoldoutSampleDocument holdout)
    {
        if (value.Version != 2 || value.DatasetStatus != "frozen-llm-predictions" ||
            value.HoldoutFingerprint != holdout.SampleFingerprint ||
            value.TaxonomyFingerprint != _catalog.Fingerprint || value.TaxonomyVersion != _catalog.Version ||
            value.ModelId != QwenDeepAnalysisContract.ModelId || value.ModelTag != QwenDeepAnalysisContract.ModelTag ||
            value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            value.PromptVersion != QwenDeepAnalysisContract.PromptVersion ||
            value.PromptHash != QwenDeepAnalysisContract.PromptHash ||
            value.GenerationConfiguration != GenerationConfiguration() ||
            value.PostingCount != holdout.Examples.Count || value.ConceptCount != _catalog.Concepts.Count ||
            value.Items.Count != holdout.Examples.Count ||
            value.PredictionDatasetFingerprint != PredictionFingerprint(value))
            throw new InvalidDataException("The frozen LLM prediction dataset changed or does not match the current pinned contract.");
        var items = value.Items.ToDictionary(item => item.EvaluationExampleId, StringComparer.Ordinal);
        if (items.Count != holdout.Examples.Count)
            throw new InvalidDataException("The frozen LLM prediction dataset is incomplete or duplicated.");
        foreach (var example in holdout.Examples)
            if (!items.TryGetValue(example.EvaluationExampleId, out var item))
                throw new InvalidDataException("The frozen LLM prediction dataset omitted a holdout posting.");
            else ValidatePrediction(item, example);
    }

    private void ValidateReferences(AiReferenceDataset references, HoldoutSampleDocument holdout)
    {
        if (references.DatasetRole != EvaluationDatasetRoles.ProductionHoldout ||
            references.DatasetStatus != "frozen-ai-adjudicated-reference-labels" ||
            references.HoldoutSampleFingerprint != holdout.SampleFingerprint ||
            references.TaxonomyFingerprint != _catalog.Fingerprint ||
            references.TaxonomyVersion != _catalog.Version ||
            references.PostingCount != holdout.Examples.Count ||
            references.ConceptCount != _catalog.Concepts.Count ||
            references.ConceptDecisionCount != holdout.Examples.Count * _catalog.Concepts.Count ||
            references.Decisions.Count != references.ConceptDecisionCount ||
            references.ReferenceDatasetFingerprint !=
                AiHoldoutEvaluationService.CalculateReferenceFingerprint(references) ||
            references.Decisions.Any(item => item.Contaminated ||
                item.LabelProvenance.Contains("qwen", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The frozen references are changed, incompatible, or contaminated by Qwen.");
    }

    private async Task<ExternalLlmResourceObservation?> ReadExternalResourceObservationAsync(
        IReadOnlyList<LlmHoldoutPredictionItem> items, CancellationToken token)
    {
        var path = Path.Combine(_directory, ExternalResourceName);
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<ExternalLlmResourceObservation>(
            await File.ReadAllBytesAsync(path, token), JsonOptions);
        if (value is null || value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            value.SampleCount <= 0 || value.AverageGpuUtilizationPercent is < 0 or > 100 ||
            value.PeakGpuMemoryUsedBytes < 0 || value.PeakOllamaContainerRamBytes < 0 ||
            value.PeakAdapterContainerRamBytes < 0 || string.IsNullOrWhiteSpace(value.Source) ||
            value.ObservedUtc < items.Min(item => item.AnalyzedUtc) ||
            value.ObservedUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new InvalidDataException("The external LLM resource observation is invalid.");
        return value;
    }

    private static LlmRuntimeMetrics Runtime(LlmHoldoutPredictionDataset frozen,
        ExternalLlmResourceObservation? external)
    {
        var items = frozen.Items;
        var latencies = items.Select(item => item.LatencyMilliseconds).Order().ToArray();
        var startedUtc = items.Min(item => item.AnalyzedUtc -
            TimeSpan.FromMilliseconds(item.LatencyMilliseconds));
        var totalSeconds = Math.Max(0, (frozen.FrozenUtc - startedUtc).TotalSeconds);
        var outputTokens = items.Sum(item => item.Inference.OutputTokenCount ?? 0);
        var outputDuration = items.Sum(item => item.Inference.OutputDurationNanoseconds ?? 0);
        return new(totalSeconds, latencies.Average(), Percentile(latencies, 0.5),
            Percentile(latencies, 0.95), totalSeconds <= 0 ? 0 : items.Count / totalSeconds * 60,
            outputDuration <= 0 ? null : outputTokens * 1_000_000_000d / outputDuration,
            items.Sum(item => item.Inference.PromptTokenCount ?? 0), outputTokens, items.Count,
            external?.AverageGpuUtilizationPercent,
            Max(Max(items.Select(item => item.Inference.ModelVramBytes)),
                external?.PeakGpuMemoryUsedBytes),
            Max(items.Select(item => item.Inference.ModelResidentBytes)),
            external?.PeakOllamaContainerRamBytes,
            Max(Max(items.Select(item => item.Inference.AdapterPeakResidentBytes)),
                external?.PeakAdapterContainerRamBytes),
            external is null
                ? "Not visible from the hardened application container; no validated external nvidia-smi observation was supplied."
                : $"Externally sampled during the frozen run: {external.Source} ({external.SampleCount} samples).",
            "Peak private-container RAM from an external docker-stats observation when supplied; adapter peak RSS and Ollama model residency remain separately identified.");
    }

    private RegexEvaluationReport ToLedgerReport(LlmHoldoutEvaluationReport report) => new(
        report.EvaluationRunId, report.EvaluatedUtc, "not-applicable-llm",
        report.HoldoutFingerprint, report.TaxonomyFingerprint, report.TaxonomyVersion,
        report.PromptHash, 0, 0, [], report.Concepts.Select(item =>
            new RegexConceptEvaluationResult(item.ConceptId, item.Support, item.LlmTruePositive,
                item.LlmFalsePositive, item.LlmFalseNegative, item.LlmTrueNegative,
                item.LlmPrecision, item.LlmRecall, item.LlmF1)).ToArray(), report.LlmMacro,
        report.LlmMicro, new(null, null, null, 0, 0), new(null, null, null, 0, 0),
        report.DatasetId + "-llm", report.DatasetRole, report.DatasetDisplayName,
        "Prediction-blinded current local LLM evaluation against the frozen production holdout.",
        "frozen-llm-predictions-vs-ai-adjudicated-references", "same-frozen-simple-random-holdout",
        20260902, report.PostingCount, report.EligibleConceptDecisions,
        report.PositiveDecisions, report.NegativeDecisions, report.EvaluationStatus,
        $"Prediction {report.PredictionFingerprint}. Reference {report.ReferenceLabelFingerprint}. {report.Disclaimer}");

    private static LlmEvaluationLedgerDetails ToLedgerDetails(LlmHoldoutEvaluationReport report) => new(
        report.EvaluationRunId, report.HoldoutFingerprint, report.ReferenceLabelFingerprint,
        report.PredictionFingerprint, "local-llm", report.ModelId, report.ModelTag,
        report.ModelDigest, report.PromptVersion, report.PromptHash,
        JsonSerializer.Serialize(report.GenerationConfiguration, JsonOptions),
        JsonSerializer.Serialize(report.Runtime, JsonOptions),
        JsonSerializer.Serialize(new { report.RegexMacro, report.RegexMicro, report.LlmMacro,
            report.LlmMicro, report.AbsoluteDifference }, JsonOptions),
        $"{report.Disclaimer} {report.Interpretation}");

    private void RecoverInterruptedRun()
    {
        var status = GetStatus();
        if (status.State is LlmHoldoutEvaluationStates.NotStarted or LlmHoldoutEvaluationStates.Complete
            or LlmHoldoutEvaluationStates.Failed) return;
        Directory.CreateDirectory(_directory);
        WriteAtomicallyAsync(Path.Combine(_directory, StatusName), status with
        {
            State = LlmHoldoutEvaluationStates.Failed,
            DisplayState = "Failed",
            UpdatedUtc = DateTimeOffset.UtcNow,
            Message = "The application restarted. Validated prediction progress remains durable and a new run resumes without replacing completed predictions."
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task<T> ReadRequiredAsync<T>(string name, CancellationToken token)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) throw new InvalidDataException($"Required evaluation artifact '{name}' is missing.");
        return JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException($"Evaluation artifact '{name}' is invalid.");
    }

    private Task StatusAsync(string state, string display, string runId, int completed, int total,
        string message, CancellationToken token, string? predictionFingerprint = null)
    {
        var current = GetStatus();
        var startedUtc = current.EvaluationRunId == runId && current.StartedUtc is not null
            ? current.StartedUtc : DateTimeOffset.UtcNow;
        return WriteStatusAsync(new(state, display, DateTimeOffset.UtcNow, runId, completed, total,
            message, predictionFingerprint, null, startedUtc), token);
    }

    private Task WriteStatusAsync(LlmHoldoutEvaluationStatus status,
        CancellationToken token = default) => WriteAtomicallyAsync(
            Path.Combine(_directory, StatusName), status, token);

    private static async Task WriteAtomicallyAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
        File.Move(temporary, path, true);
    }

    private static LlmGenerationConfiguration GenerationConfiguration() => new(
        QwenDeepAnalysisContract.Temperature, QwenDeepAnalysisContract.Seed,
        QwenDeepAnalysisContract.ContextLength, QwenDeepAnalysisContract.MaximumOutputTokens,
        $"{QwenDeepAnalysisContract.OutputContractVersion}:{QwenDeepAnalysisContract.OutputSchemaHash}");
    private static string PredictionFingerprint(LlmHoldoutPredictionDataset value) => Hash(
        JsonSerializer.Serialize(value with { PredictionDatasetFingerprint = "" }, JsonOptions)
            .Replace("\r\n", "\n"));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static double? Difference(double? left, double? right) =>
        left.HasValue && right.HasValue ? left.Value - right.Value : null;
    private static double? AbsDifference(double? left, double? right) =>
        left.HasValue && right.HasValue ? Math.Abs(left.Value - right.Value) : null;
    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;
        var position = (values.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return values[lower] + (values[upper] - values[lower]) * (position - lower);
    }
    private static long? Max(IEnumerable<long?> values)
    {
        var defined = values.Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        return defined.Length == 0 ? null : defined.Max();
    }
    private static long? Max(long? left, long? right) => left.HasValue && right.HasValue
        ? Math.Max(left.Value, right.Value) : left ?? right;
}
