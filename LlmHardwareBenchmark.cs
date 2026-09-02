using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public static class LlmHardwareBenchmarkFiles
{
    public const string Holdout = "holdout.json";
    public const string Progress = "rtx5080-predictions-progress-v1.json";
    public const string Predictions = "rtx5080-predictions-v1.json";
    public const string Status = "rtx5080-benchmark-status-v1.json";
    public const string Resources = "rtx5080-resource-observation-v1.json";
    public const string Report = "rtx5080-holdout-report-latest.json";
    public const string Comparison = "llm-hardware-comparison-v1.json";
}

public sealed record LlmHardwareDescriptor(
    string Key, string DisplayName, string GpuName, long VramBytes,
    string? DriverVersion = null, string? CudaVersion = null,
    string? DockerVersion = null, string? NvidiaContainerToolkitVersion = null);

public sealed record LlmHardwareResourceObservation(
    string ModelDigest, string GpuName, string DriverVersion, string CudaVersion,
    string DockerVersion, string NvidiaContainerToolkitVersion,
    DateTimeOffset ObservedUtc, int SampleCount,
    double AverageGpuUtilizationPercent, long PeakGpuMemoryUsedBytes,
    long PeakOllamaContainerRamBytes, long PeakAdapterContainerRamBytes,
    double? AverageGpuPowerWatts, double? PeakGpuPowerWatts,
    double? ApproximateGpuEnergyWattHours, string Source);

public sealed record LlmHardwareConceptDisagreement(
    string ConceptId, string ConceptName, int DisagreementCount);

public sealed record LlmHardwareSemanticAgreement(
    int TotalDecisions, int ExactAgreementCount, int DisagreementCount,
    double AgreementRate, IReadOnlyList<LlmHardwareConceptDisagreement> Concepts);

public sealed record LlmHardwarePerformanceComparison(
    double AbsoluteRuntimeReductionSeconds, double RuntimeReductionPercent,
    double SpeedupMultiplier, double ThroughputMultiplier,
    double? OutputTokensPerSecondMultiplier, long? PeakVramDifferenceBytes,
    double? AverageGpuUtilizationDifferencePercentagePoints,
    long? PeakOllamaRamDifferenceBytes);

public sealed record LlmHardwareComparisonReport(
    int Version, string ComparisonId, DateTimeOffset ComparedUtc,
    LlmHardwareDescriptor Gtx1070Hardware, LlmHardwareDescriptor Rtx5080Hardware,
    LlmHoldoutEvaluationReport Gtx1070, LlmHoldoutEvaluationReport Rtx5080,
    LlmHardwareSemanticAgreement SemanticAgreement,
    LlmHardwarePerformanceComparison Performance,
    LlmHardwareResourceObservation Rtx5080Resources,
    string HoldoutFileSha256, string ReferenceFileSha256, string Notes);

public sealed record LlmHardwarePredictionResult(
    string EvaluationRunId, string State, int Completed, int Total,
    string PredictionFingerprint, string PredictionFile);

public sealed class LlmHardwareBenchmarkRunner
{
    public const string ExpectedHoldoutFingerprint =
        "2a5f532f4241368e1b21d30ef9a50ad16886749076e9111dce354baaa045b963";
    public const string ExpectedReferenceFingerprint =
        "a0844aea385906da94073fffe76a22b03d583c21efcf7e4cf60cfd978bc86282";
    public const string ExpectedGtxPredictionFingerprint =
        "1fd46cfdab81feebe58255a6408ac9bc06a56bfda3b26a1ebe697abb9b7ccd44";
    public const string ExpectedHoldoutFileSha256 =
        "5be7fa382048eee4cd104d901c33367b522c5a4fb1a9962f63521c069bcde88b";
    public const string ExpectedReferenceFileSha256 =
        "8f35fb445c1aa13b1ef0440eca73bb35d06efaae0835d8375e08205754e9ffea";
    private const string ReferenceName = "ai-reference-labels-v1.json";
    private const string GtxPredictionsName = "llm-predictions-v2.json";
    private const string GtxReportName = "llm-holdout-report-latest.json";
    private const string RegexReportName = "ai-holdout-report-latest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };
    private readonly string _directory;
    private readonly Func<ClassifierRequest, CancellationToken, Task<QwenDeepAnalysis?>> _classify;
    private readonly JobConceptCatalog _catalog;

    public LlmHardwareBenchmarkRunner(string directory,
        Func<ClassifierRequest, CancellationToken, Task<QwenDeepAnalysis?>> classify,
        JobConceptCatalog catalog)
    {
        _directory = Path.GetFullPath(directory);
        _classify = classify;
        _catalog = catalog;
    }

    public async Task<LlmHardwarePredictionResult> RunPredictionsAsync(
        CancellationToken token = default)
    {
        Directory.CreateDirectory(_directory);
        var forbiddenArtifacts = Directory.EnumerateFiles(_directory, "*",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Contains("reference", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("regex", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("gtx", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(GtxPredictionsName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(GtxReportName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(RegexReportName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(LlmHardwareBenchmarkFiles.Report,
                        StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(LlmHardwareBenchmarkFiles.Comparison,
                        StringComparison.OrdinalIgnoreCase);
            }).Select(Path.GetFileName).ToArray();
        if (forbiddenArtifacts.Length > 0)
            throw new InvalidDataException(
                $"Scoring artifacts must not be present on the prediction-blinded benchmark node: {string.Join(", ", forbiddenArtifacts)}.");
        var holdoutPath = Path.Combine(_directory, LlmHardwareBenchmarkFiles.Holdout);
        if (FileHash(holdoutPath) != ExpectedHoldoutFileSha256)
            throw new InvalidDataException("The benchmark holdout file hash is not canonical.");
        var holdout = await ReadRequiredAsync<HoldoutSampleDocument>(holdoutPath, token);
        AiHoldoutEvaluationService.ValidateFrozenHoldout(holdout, _catalog);
        if (holdout.Examples.Count != 200 ||
            holdout.SampleFingerprint != ExpectedHoldoutFingerprint)
            throw new InvalidDataException("The benchmark requires the exact frozen 200-posting holdout.");

        var status = ReadStatus();
        var runId = status?.EvaluationRunId ?? Guid.NewGuid().ToString("N");
        var startedUtc = status?.StartedUtc ?? DateTimeOffset.UtcNow;
        var frozenPath = Path.Combine(_directory, LlmHardwareBenchmarkFiles.Predictions);
        if (File.Exists(frozenPath))
        {
            var existing = await ReadRequiredAsync<LlmHoldoutPredictionDataset>(frozenPath, token);
            ValidateFrozenPredictions(existing, holdout);
            await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Complete, "Predictions frozen",
                DateTimeOffset.UtcNow, runId, 200, 200,
                "The complete RTX 5080 prediction set is frozen; reference labels were never present.",
                existing.PredictionDatasetFingerprint, LlmHardwareBenchmarkFiles.Predictions,
                startedUtc), token);
            return new(runId, "predictions-frozen", 200, 200,
                existing.PredictionDatasetFingerprint, LlmHardwareBenchmarkFiles.Predictions);
        }

        var progress = await ReadProgressAsync(holdout, token);
        await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Predicting,
            "Running RTX 5080 LLM predictions", DateTimeOffset.UtcNow, runId,
            progress.Count, 200,
            "Reference labels, RegEx output, rules, scores, and GTX predictions are unavailable to this process.",
            null, null, startedUtc), token);
        foreach (var example in holdout.Examples.Where(item =>
                     !progress.ContainsKey(item.EvaluationExampleId)))
        {
            token.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            var result = await _classify(new(example.EvaluationExampleId, example.Title,
                example.DescriptionHtml), token);
            timer.Stop();
            if (result?.Inference is null)
                throw new InvalidDataException(
                    $"The first RTX 5080 prediction failed strict validation for {example.EvaluationExampleId}; no retry or alternate configuration was used.");
            ValidatePrediction(result, example);
            progress.Add(example.EvaluationExampleId, new(example.EvaluationExampleId,
                example.PostingContentHash, result.AnalyzedUtc, result.ClassificationFingerprint,
                result.Predictions, timer.Elapsed.TotalMilliseconds, result.Inference));
            await WriteAtomicallyAsync(Path.Combine(_directory,
                    LlmHardwareBenchmarkFiles.Progress),
                holdout.Examples.Where(item => progress.ContainsKey(item.EvaluationExampleId))
                    .Select(item => progress[item.EvaluationExampleId]).ToArray(), token);
            await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Predicting,
                "Running RTX 5080 LLM predictions", DateTimeOffset.UtcNow, runId,
                progress.Count, 200,
                "First valid prediction per posting is retained; no retry, tuning, or alternate prompt is allowed.",
                null, null, startedUtc), token);
        }

        await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Freezing,
            "Freezing RTX 5080 predictions", DateTimeOffset.UtcNow, runId, 200, 200,
            "All predictions are complete; freezing before any reference comparison.",
            null, null, startedUtc), token);
        var frozen = new LlmHoldoutPredictionDataset(2,
            $"{holdout.SamplingRunId}-qwen-rtx5080-predictions-v1",
            "frozen-llm-hardware-benchmark-predictions", DateTimeOffset.UtcNow,
            holdout.SampleFingerprint, _catalog.Fingerprint, _catalog.Version,
            QwenDeepAnalysisContract.ModelId, QwenDeepAnalysisContract.ModelTag,
            QwenDeepAnalysisContract.ModelDigest, QwenDeepAnalysisContract.PromptVersion,
            QwenDeepAnalysisContract.PromptHash, GenerationConfiguration(), 200,
            _catalog.Concepts.Count,
            holdout.Examples.Select(item => progress[item.EvaluationExampleId]).ToArray());
        frozen = frozen with { PredictionDatasetFingerprint = PredictionFingerprint(frozen) };
        await WriteAtomicallyAsync(frozenPath, frozen, token);
        await WriteStatusAsync(new(LlmHoldoutEvaluationStates.Complete, "Predictions frozen",
            DateTimeOffset.UtcNow, runId, 200, 200,
            "The complete RTX 5080 prediction set is frozen; reference labels were never present.",
            frozen.PredictionDatasetFingerprint, LlmHardwareBenchmarkFiles.Predictions,
            startedUtc), token);
        return new(runId, "predictions-frozen", 200, 200,
            frozen.PredictionDatasetFingerprint, LlmHardwareBenchmarkFiles.Predictions);
    }

    public static async Task<LlmHardwareComparisonReport> ScoreAsync(
        string benchmarkDirectory, string productionEvaluationDirectory,
        JobConceptCatalog catalog, CancellationToken token = default)
    {
        benchmarkDirectory = Path.GetFullPath(benchmarkDirectory);
        productionEvaluationDirectory = Path.GetFullPath(productionEvaluationDirectory);
        var benchmarkHoldoutPath = Path.Combine(benchmarkDirectory,
            LlmHardwareBenchmarkFiles.Holdout);
        var productionHoldoutPath = Path.Combine(productionEvaluationDirectory,
            LlmHardwareBenchmarkFiles.Holdout);
        var referencePath = Path.Combine(productionEvaluationDirectory, ReferenceName);
        if (FileHash(benchmarkHoldoutPath) != ExpectedHoldoutFileSha256 ||
            FileHash(productionHoldoutPath) != ExpectedHoldoutFileSha256 ||
            FileHash(referencePath) != ExpectedReferenceFileSha256)
            throw new InvalidDataException("Frozen benchmark input file hashes changed.");

        var holdout = await ReadAsync<HoldoutSampleDocument>(benchmarkHoldoutPath, token);
        var productionHoldout = await ReadAsync<HoldoutSampleDocument>(productionHoldoutPath, token);
        if (JsonSerializer.Serialize(holdout, JsonOptions) !=
            JsonSerializer.Serialize(productionHoldout, JsonOptions))
            throw new InvalidDataException("Tinker and curiosity holdouts differ.");
        AiHoldoutEvaluationService.ValidateFrozenHoldout(holdout, catalog);
        if (holdout.SampleFingerprint != ExpectedHoldoutFingerprint)
            throw new InvalidDataException("The holdout fingerprint changed.");
        var references = await ReadAsync<AiReferenceDataset>(referencePath, token);
        ValidateReferences(references, holdout, catalog);
        var rtxPredictions = await ReadAsync<LlmHoldoutPredictionDataset>(
            Path.Combine(benchmarkDirectory, LlmHardwareBenchmarkFiles.Predictions), token);
        ValidateFrozenPredictions(rtxPredictions, holdout, catalog,
            "frozen-llm-hardware-benchmark-predictions");
        var gtxPredictions = await ReadAsync<LlmHoldoutPredictionDataset>(
            Path.Combine(productionEvaluationDirectory, GtxPredictionsName), token);
        ValidateFrozenPredictions(gtxPredictions, holdout, catalog,
            "frozen-llm-predictions");
        if (gtxPredictions.PredictionDatasetFingerprint != ExpectedGtxPredictionFingerprint)
            throw new InvalidDataException("The canonical GTX 1070 prediction set changed.");
        var gtxReport = await ReadAsync<LlmHoldoutEvaluationReport>(
            Path.Combine(productionEvaluationDirectory, GtxReportName), token);
        var regexReport = await ReadAsync<AiHoldoutEvaluationReport>(
            Path.Combine(productionEvaluationDirectory, RegexReportName), token);
        if (gtxReport.HoldoutFingerprint != ExpectedHoldoutFingerprint ||
            gtxReport.ReferenceLabelFingerprint != ExpectedReferenceFingerprint ||
            gtxReport.PredictionFingerprint != ExpectedGtxPredictionFingerprint ||
            regexReport.DatasetFingerprint != ExpectedHoldoutFingerprint ||
            regexReport.ReferenceLabelFingerprint != ExpectedReferenceFingerprint)
            throw new InvalidDataException("The canonical GTX 1070 or RegEx baseline changed.");
        var resources = await ReadAsync<LlmHardwareResourceObservation>(
            Path.Combine(benchmarkDirectory, LlmHardwareBenchmarkFiles.Resources), token);
        ValidateResources(resources, rtxPredictions);

        var rtxSets = PredictionSets(rtxPredictions);
        var metrics = HoldoutMetricCalculator.Calculate(catalog, references, rtxSets);
        var regexConcepts = regexReport.Concepts.ToDictionary(item => item.ConceptId,
            StringComparer.Ordinal);
        var comparisons = metrics.Concepts.Select(item =>
        {
            var baseline = regexConcepts[item.ConceptId];
            return new LlmConceptComparison(item.ConceptId, item.ConceptName, item.Support,
                item.EligibleDecisions, item.UnresolvedDecisions, baseline.Precision,
                baseline.Recall, baseline.F1, item.Precision, item.Recall, item.F1,
                Difference(item.F1, baseline.F1), item.DisagreementRate,
                item.TruePositive, item.FalsePositive, item.FalseNegative, item.TrueNegative);
        }).ToArray();
        var status = await ReadAsync<LlmHoldoutEvaluationStatus>(Path.Combine(
            benchmarkDirectory, LlmHardwareBenchmarkFiles.Status), token);
        var runtime = Runtime(rtxPredictions, resources);
        var rtxReport = new LlmHoldoutEvaluationReport(
            status.EvaluationRunId ?? Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            references.DatasetId, EvaluationDatasetRoles.ProductionHoldout,
            "RTX 5080 · AI-ADJUDICATED PRODUCTION HOLDOUT",
            holdout.SampleFingerprint, references.ReferenceDatasetFingerprint,
            rtxPredictions.PredictionDatasetFingerprint, catalog.Fingerprint,
            catalog.Version, rtxPredictions.ModelId, rtxPredictions.ModelTag,
            rtxPredictions.ModelDigest, rtxPredictions.PromptVersion,
            rtxPredictions.PromptHash, rtxPredictions.GenerationConfiguration,
            200, references.ConceptDecisionCount, metrics.EligibleCount,
            references.UnresolvedCount, metrics.PositiveCount, metrics.NegativeCount,
            regexReport.Macro, regexReport.Micro, metrics.Macro, metrics.Micro,
            new(AbsDifference(metrics.Macro.Precision, regexReport.Macro.Precision),
                AbsDifference(metrics.Macro.Recall, regexReport.Macro.Recall),
                AbsDifference(metrics.Macro.F1, regexReport.Macro.F1),
                AbsDifference(metrics.Micro.Precision, regexReport.Micro.Precision),
                AbsDifference(metrics.Micro.Recall, regexReport.Micro.Recall),
                AbsDifference(metrics.Micro.F1, regexReport.Micro.F1)),
            comparisons, runtime, AiHoldoutEvaluationService.ExactDisclaimer,
            "This is the same frozen semantic experiment on different hardware. Any prediction disagreement must be reported rather than tuned away.",
            "scored",
            "RTX 5080 predictions were generated on isolated tinker without references, RegEx data, GTX predictions, or production application data. Scoring occurred only after the complete prediction set was frozen and transferred to curiosity.");
        var agreement = Agreement(gtxPredictions, rtxPredictions, catalog);
        var performance = Performance(gtxReport.Runtime, rtxReport.Runtime);
        var report = new LlmHardwareComparisonReport(1, Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            new("gtx1070", "GTX 1070", "NVIDIA GeForce GTX 1070", 8L * 1024 * 1024 * 1024),
            new("rtx5080", "RTX 5080", resources.GpuName, 16L * 1024 * 1024 * 1024,
                resources.DriverVersion, resources.CudaVersion, resources.DockerVersion,
                resources.NvidiaContainerToolkitVersion),
            gtxReport, rtxReport, agreement, performance, resources,
            ExpectedHoldoutFileSha256, ExpectedReferenceFileSha256,
            "Only benchmark hardware and unavoidable NVIDIA driver/runtime layers differ. Model, quantization, prompt, schema, generation settings, taxonomy, sample, references, exclusions, and metrics are unchanged.");
        await WriteAtomicallyAsync(Path.Combine(productionEvaluationDirectory,
            LlmHardwareBenchmarkFiles.Report), rtxReport, token);
        await WriteAtomicallyAsync(Path.Combine(productionEvaluationDirectory,
            LlmHardwareBenchmarkFiles.Comparison), report, token);
        await WriteAtomicallyAsync(Path.Combine(productionEvaluationDirectory,
            LlmHardwareBenchmarkFiles.Status), status with
        {
            State = LlmHoldoutEvaluationStates.Complete,
            DisplayState = "Complete",
            UpdatedUtc = DateTimeOffset.UtcNow,
            Message = "RTX 5080 hardware benchmark complete.",
            PredictionFingerprint = rtxPredictions.PredictionDatasetFingerprint,
            ReportFile = LlmHardwareBenchmarkFiles.Comparison
        }, token);
        return report;
    }

    public static LlmHardwareComparisonReport? ReadComparison(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory),
            LlmHardwareBenchmarkFiles.Comparison);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<LlmHardwareComparisonReport>(
                File.ReadAllBytes(path), JsonOptions) : null;
    }

    public static LlmHoldoutEvaluationStatus GetStatus(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory),
            LlmHardwareBenchmarkFiles.Status);
        if (!File.Exists(path)) return new(LlmHoldoutEvaluationStates.NotStarted,
            "Not started", DateTimeOffset.MinValue, null, 0, 200,
            "The isolated RTX 5080 benchmark has not been imported.", null, null);
        try
        {
            return JsonSerializer.Deserialize<LlmHoldoutEvaluationStatus>(
                File.ReadAllBytes(path), JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return new(LlmHoldoutEvaluationStates.Failed, "Failed",
                DateTimeOffset.UtcNow, null, 0, 200,
                "The RTX 5080 benchmark status artifact is invalid.", null, null);
        }
    }

    private LlmHoldoutEvaluationStatus? ReadStatus()
    {
        var path = Path.Combine(_directory, LlmHardwareBenchmarkFiles.Status);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<LlmHoldoutEvaluationStatus>(
                File.ReadAllBytes(path), JsonOptions) : null;
    }

    private async Task<Dictionary<string, LlmHoldoutPredictionItem>> ReadProgressAsync(
        HoldoutSampleDocument holdout, CancellationToken token)
    {
        var path = Path.Combine(_directory, LlmHardwareBenchmarkFiles.Progress);
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        var items = await ReadAsync<IReadOnlyList<LlmHoldoutPredictionItem>>(path, token);
        var examples = holdout.Examples.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        var result = new Dictionary<string, LlmHoldoutPredictionItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!examples.TryGetValue(item.EvaluationExampleId, out var example) ||
                !result.TryAdd(item.EvaluationExampleId, item))
                throw new InvalidDataException(
                    "RTX benchmark progress is duplicated or outside the holdout.");
            ValidatePrediction(item, example, _catalog);
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
                example.PostingContentHash, _catalog) ||
            !ValidPredictions(result.Predictions, _catalog))
            throw new InvalidDataException(
                "An RTX benchmark prediction failed frozen contract validation.");
    }

    private void ValidateFrozenPredictions(LlmHoldoutPredictionDataset value,
        HoldoutSampleDocument holdout) => ValidateFrozenPredictions(value, holdout,
        _catalog, "frozen-llm-hardware-benchmark-predictions");

    private static void ValidateFrozenPredictions(LlmHoldoutPredictionDataset value,
        HoldoutSampleDocument holdout, JobConceptCatalog catalog, string expectedStatus)
    {
        if (value.Version != 2 || value.DatasetStatus != expectedStatus ||
            value.HoldoutFingerprint != holdout.SampleFingerprint ||
            value.TaxonomyFingerprint != catalog.Fingerprint ||
            value.TaxonomyVersion != catalog.Version ||
            value.ModelId != QwenDeepAnalysisContract.ModelId ||
            value.ModelTag != QwenDeepAnalysisContract.ModelTag ||
            value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            value.PromptVersion != QwenDeepAnalysisContract.PromptVersion ||
            value.PromptHash != QwenDeepAnalysisContract.PromptHash ||
            value.GenerationConfiguration != GenerationConfiguration() ||
            value.PostingCount != 200 || value.ConceptCount != catalog.Concepts.Count ||
            value.Items.Count != 200 ||
            value.PredictionDatasetFingerprint != PredictionFingerprint(value))
            throw new InvalidDataException(
                "A frozen hardware prediction dataset changed or violates the pinned contract.");
        var examples = holdout.Examples.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        if (value.Items.Select(item => item.EvaluationExampleId).Distinct(
                StringComparer.Ordinal).Count() != 200)
            throw new InvalidDataException("The hardware prediction dataset is duplicated.");
        foreach (var item in value.Items)
        {
            if (!examples.TryGetValue(item.EvaluationExampleId, out var example))
                throw new InvalidDataException("A hardware prediction is outside the holdout.");
            ValidatePrediction(item, example, catalog);
        }
    }

    private static void ValidatePrediction(LlmHoldoutPredictionItem item,
        EvaluationExample example, JobConceptCatalog catalog)
    {
        if (item.PostingContentHash != example.PostingContentHash || item.Inference is null ||
            item.ClassificationFingerprint != QwenDeepAnalysisContract.ClassificationFingerprint(
                example.PostingContentHash, catalog) ||
            !ValidPredictions(item.Predictions, catalog) || item.LatencyMilliseconds < 0)
            throw new InvalidDataException(
                "A durable hardware prediction failed provenance validation.");
    }

    private static bool ValidPredictions(
        IReadOnlyList<SemanticConceptPrediction> predictions, JobConceptCatalog catalog) =>
        predictions.Count == catalog.Concepts.Count &&
        predictions.Select(item => item.ConceptId).Distinct(StringComparer.Ordinal).Count() ==
            catalog.Concepts.Count && predictions.All(item => catalog.Contains(item.ConceptId));

    private static void ValidateReferences(AiReferenceDataset references,
        HoldoutSampleDocument holdout, JobConceptCatalog catalog)
    {
        if (references.DatasetRole != EvaluationDatasetRoles.ProductionHoldout ||
            references.DatasetStatus != "frozen-ai-adjudicated-reference-labels" ||
            references.HoldoutSampleFingerprint != holdout.SampleFingerprint ||
            references.TaxonomyFingerprint != catalog.Fingerprint ||
            references.TaxonomyVersion != catalog.Version || references.PostingCount != 200 ||
            references.ConceptCount != catalog.Concepts.Count ||
            references.ConceptDecisionCount != 200 * catalog.Concepts.Count ||
            references.Decisions.Count != references.ConceptDecisionCount ||
            references.ReferenceDatasetFingerprint != ExpectedReferenceFingerprint ||
            references.ReferenceDatasetFingerprint !=
                AiHoldoutEvaluationService.CalculateReferenceFingerprint(references) ||
            references.Decisions.Any(item => item.Contaminated ||
                item.LabelProvenance.Contains("qwen", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "Frozen references changed, are incompatible, or are contaminated by Qwen.");
    }

    private static void ValidateResources(LlmHardwareResourceObservation value,
        LlmHoldoutPredictionDataset predictions)
    {
        if (value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            value.GpuName != "NVIDIA GeForce RTX 5080" || value.SampleCount <= 0 ||
            value.AverageGpuUtilizationPercent is < 0 or > 100 ||
            value.PeakGpuMemoryUsedBytes <= 0 || value.PeakOllamaContainerRamBytes <= 0 ||
            value.PeakAdapterContainerRamBytes <= 0 ||
            value.AverageGpuPowerWatts is < 0 or > 1000 ||
            value.PeakGpuPowerWatts is < 0 or > 1000 ||
            value.ApproximateGpuEnergyWattHours is < 0 ||
            string.IsNullOrWhiteSpace(value.Source) ||
            value.ObservedUtc < predictions.Items.Min(item => item.AnalyzedUtc) ||
            value.ObservedUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new InvalidDataException("RTX 5080 resource telemetry is invalid.");
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> PredictionSets(
        LlmHoldoutPredictionDataset value) => value.Items.ToDictionary(
            item => item.EvaluationExampleId,
            item => (IReadOnlySet<string>)item.Predictions.Where(prediction => prediction.Matched)
                .Select(prediction => prediction.ConceptId).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static LlmHardwareSemanticAgreement Agreement(
        LlmHoldoutPredictionDataset gtx, LlmHoldoutPredictionDataset rtx,
        JobConceptCatalog catalog)
    {
        var gtxItems = gtx.Items.ToDictionary(item => item.EvaluationExampleId,
            StringComparer.Ordinal);
        var conceptCounts = catalog.Concepts.ToDictionary(item => item.Id, _ => 0,
            StringComparer.Ordinal);
        var disagreements = 0;
        foreach (var rtxItem in rtx.Items)
        {
            var gtxValues = gtxItems[rtxItem.EvaluationExampleId].Predictions.ToDictionary(
                item => item.ConceptId, item => item.Matched, StringComparer.Ordinal);
            foreach (var value in rtxItem.Predictions)
            {
                if (gtxValues[value.ConceptId] == value.Matched) continue;
                disagreements++;
                conceptCounts[value.ConceptId]++;
            }
        }
        var total = 200 * catalog.Concepts.Count;
        var names = catalog.Concepts.ToDictionary(item => item.Id, item => item.DisplayName,
            StringComparer.Ordinal);
        return new(total, total - disagreements, disagreements,
            (double)(total - disagreements) / total,
            conceptCounts.Where(item => item.Value > 0)
                .OrderByDescending(item => item.Value).ThenBy(item => item.Key,
                    StringComparer.Ordinal)
                .Select(item => new LlmHardwareConceptDisagreement(item.Key,
                    names[item.Key], item.Value)).ToArray());
    }

    private static LlmRuntimeMetrics Runtime(LlmHoldoutPredictionDataset frozen,
        LlmHardwareResourceObservation resources)
    {
        var items = frozen.Items;
        var latencies = items.Select(item => item.LatencyMilliseconds).Order().ToArray();
        var startedUtc = items.Min(item => item.AnalyzedUtc -
            TimeSpan.FromMilliseconds(item.LatencyMilliseconds));
        var totalSeconds = Math.Max(0, (frozen.FrozenUtc - startedUtc).TotalSeconds);
        var outputTokens = items.Sum(item => item.Inference.OutputTokenCount ?? 0);
        var outputDuration = items.Sum(item => item.Inference.OutputDurationNanoseconds ?? 0);
        return new(totalSeconds, latencies.Average(), Percentile(latencies, 0.5),
            Percentile(latencies, 0.95), totalSeconds <= 0 ? 0 : 200 / totalSeconds * 60,
            outputDuration <= 0 ? null : outputTokens * 1_000_000_000d / outputDuration,
            items.Sum(item => item.Inference.PromptTokenCount ?? 0), outputTokens, 200,
            resources.AverageGpuUtilizationPercent,
            Max(Max(items.Select(item => item.Inference.ModelVramBytes)),
                resources.PeakGpuMemoryUsedBytes),
            Max(items.Select(item => item.Inference.ModelResidentBytes)),
            resources.PeakOllamaContainerRamBytes,
            Max(Max(items.Select(item => item.Inference.AdapterPeakResidentBytes)),
                resources.PeakAdapterContainerRamBytes),
            $"Externally sampled on isolated tinker: {resources.Source} ({resources.SampleCount} samples).",
            "Peak private-container RAM from one-second docker stats; model residency and GPU memory remain separately identified.");
    }

    private static LlmHardwarePerformanceComparison Performance(
        LlmRuntimeMetrics gtx, LlmRuntimeMetrics rtx)
    {
        var reduction = gtx.TotalElapsedSeconds - rtx.TotalElapsedSeconds;
        return new(reduction,
            gtx.TotalElapsedSeconds == 0 ? 0 : reduction / gtx.TotalElapsedSeconds * 100,
            rtx.TotalElapsedSeconds == 0 ? 0 : gtx.TotalElapsedSeconds / rtx.TotalElapsedSeconds,
            gtx.PostingsPerMinute == 0 ? 0 : rtx.PostingsPerMinute / gtx.PostingsPerMinute,
            gtx.WeightedOutputTokensPerSecond is > 0 &&
            rtx.WeightedOutputTokensPerSecond.HasValue
                ? rtx.WeightedOutputTokensPerSecond / gtx.WeightedOutputTokensPerSecond : null,
            Difference(rtx.PeakModelVramBytes, gtx.PeakModelVramBytes),
            Difference(rtx.AverageGpuUtilizationPercent,
                gtx.AverageGpuUtilizationPercent),
            Difference(rtx.PeakOllamaContainerRamBytes,
                gtx.PeakOllamaContainerRamBytes));
    }

    private Task WriteStatusAsync(LlmHoldoutEvaluationStatus status,
        CancellationToken token) => WriteAtomicallyAsync(Path.Combine(_directory,
            LlmHardwareBenchmarkFiles.Status), status, token);

    private async Task<T> ReadRequiredAsync<T>(string path, CancellationToken token) =>
        await ReadAsync<T>(path, token);

    private static async Task<T> ReadAsync<T>(string path, CancellationToken token)
    {
        if (!File.Exists(path))
            throw new InvalidDataException($"Required benchmark artifact '{Path.GetFileName(path)}' is missing.");
        return JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(path, token),
                   JsonOptions) ??
               throw new InvalidDataException($"Benchmark artifact '{Path.GetFileName(path)}' is invalid.");
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T value,
        CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary,
            JsonSerializer.Serialize(value, JsonOptions), token);
        File.Move(temporary, path, true);
    }

    private static LlmGenerationConfiguration GenerationConfiguration() => new(
        QwenDeepAnalysisContract.Temperature, QwenDeepAnalysisContract.Seed,
        QwenDeepAnalysisContract.ContextLength,
        QwenDeepAnalysisContract.MaximumOutputTokens,
        $"{QwenDeepAnalysisContract.OutputContractVersion}:{QwenDeepAnalysisContract.OutputSchemaHash}");

    private static string PredictionFingerprint(LlmHoldoutPredictionDataset value) =>
        Hash(JsonSerializer.Serialize(value with { PredictionDatasetFingerprint = "" },
            JsonOptions).Replace("\r\n", "\n"));
    private static string FileHash(string path) => File.Exists(path)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        : "";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static double? Difference(double? left, double? right) =>
        left.HasValue && right.HasValue ? left.Value - right.Value : null;
    private static long? Difference(long? left, long? right) =>
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
