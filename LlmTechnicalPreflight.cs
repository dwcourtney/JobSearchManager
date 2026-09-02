using System.Text.Json;

namespace JobSearchManager;

public sealed record LlmTechnicalPreflightCheckpoint(
    int Version, string PreflightId, DateTimeOffset WrittenUtc,
    string InputFingerprint, QwenDeepAnalysis FirstObservation);

public sealed record LlmTechnicalPreflightReport(
    int Version, string PreflightId, DateTimeOffset CompletedUtc, string Status,
    string ModelId, string ModelTag, string ModelDigest, string PromptVersion,
    string PromptHash, string OutputContractVersion, string OutputSchemaHash,
    int ConceptCount, int ObservationCount, bool CompleteStructuredOutput,
    bool StablePredictions, bool BoundedOutput, bool CheckpointRoundTrip,
    IReadOnlyList<long?> OutputTokenCounts, string InputFingerprint,
    int SemanticDisagreementCount,
    string Notes);

public static class LlmTechnicalPreflight
{
    private const string CheckpointName = "llm-technical-preflight-v2-checkpoint.json";
    private const string LatestName = "llm-technical-preflight-v2-latest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };

    public static async Task<LlmTechnicalPreflightReport> RunAsync(string directory,
        Func<ClassifierRequest, CancellationToken, Task<QwenDeepAnalysis?>> classify,
        JobConceptCatalog catalog, CancellationToken token = default,
        bool requireStablePredictions = true)
    {
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var request = new ClassifierRequest("technical-preflight-v2",
            "Technical preflight platform engineer",
            "The engineer builds and maintains backend APIs, automates deployment checks, " +
            "monitors production services, and documents operational incidents. This synthetic " +
            "input is not sampled from the evaluation holdout.");
        var inputFingerprint = SemanticRulesetFingerprint.PostingContentHash(
            request.Title, request.Description);
        var first = await RequireValidAsync(await classify(request, token), inputFingerprint,
            catalog);
        var checkpoint = new LlmTechnicalPreflightCheckpoint(2, id, DateTimeOffset.UtcNow,
            inputFingerprint, first);
        var checkpointPath = Path.Combine(directory, CheckpointName);
        await WriteAtomicallyAsync(checkpointPath, checkpoint, token);
        var roundTrip = JsonSerializer.Deserialize<LlmTechnicalPreflightCheckpoint>(
            await File.ReadAllBytesAsync(checkpointPath, token), JsonOptions)
            ?? throw new InvalidDataException("The technical preflight checkpoint did not round-trip.");
        var second = await RequireValidAsync(await classify(request, token), inputFingerprint,
            catalog);
        var checkpointRoundTrip = roundTrip.Version == 2 && roundTrip.PreflightId == id &&
            roundTrip.InputFingerprint == inputFingerprint &&
            roundTrip.FirstObservation.ClassificationFingerprint == first.ClassificationFingerprint &&
            PredictionsEqual(roundTrip.FirstObservation.Predictions, first.Predictions);
        if (!checkpointRoundTrip)
            throw new InvalidDataException("The technical preflight checkpoint changed during its durable round-trip.");
        var stable = PredictionsEqual(roundTrip.FirstObservation.Predictions, second.Predictions);
        if (!stable && requireStablePredictions)
            throw new InvalidDataException("The deterministic technical preflight produced unstable predictions.");
        var semanticDisagreements = roundTrip.FirstObservation.Predictions.Zip(second.Predictions)
            .Count(pair => pair.First.ConceptId != pair.Second.ConceptId ||
                pair.First.Matched != pair.Second.Matched);
        var counts = new[] { first.Inference!.OutputTokenCount, second.Inference!.OutputTokenCount };
        var bounded = counts.All(value => value.HasValue && value.Value > 0 &&
            value.Value < QwenDeepAnalysisContract.MaximumOutputTokens);
        if (!bounded)
            throw new InvalidDataException("The technical preflight reached or exceeded the output-token bound.");
        var report = new LlmTechnicalPreflightReport(2, id, DateTimeOffset.UtcNow,
            stable ? "passed" : "passed-with-observed-semantic-variation",
            QwenDeepAnalysisContract.ModelId, QwenDeepAnalysisContract.ModelTag,
            QwenDeepAnalysisContract.ModelDigest, QwenDeepAnalysisContract.PromptVersion,
            QwenDeepAnalysisContract.PromptHash, QwenDeepAnalysisContract.OutputContractVersion,
            QwenDeepAnalysisContract.OutputSchemaHash, catalog.Concepts.Count, 2, true,
            stable, bounded, checkpointRoundTrip, counts, inputFingerprint,
            semanticDisagreements,
            "Two predeclared observations of one synthetic technical input; semantic repeat " +
            "variation is recorded rather than tuned on hardware-comparison runs. No holdout " +
            "posting, reference label, RegEx prediction, rule, or score was loaded or inspected.");
        await WriteAtomicallyAsync(Path.Combine(directory,
            $"llm-technical-preflight-v2-{id}.json"), report, token);
        await WriteAtomicallyAsync(Path.Combine(directory, LatestName), report, token);
        return report;
    }

    private static Task<QwenDeepAnalysis> RequireValidAsync(QwenDeepAnalysis? value,
        string inputFingerprint, JobConceptCatalog catalog)
    {
        if (value?.Inference is null || value.PostingContentHash != inputFingerprint ||
            value.ModelId != QwenDeepAnalysisContract.ModelId ||
            value.ModelTag != QwenDeepAnalysisContract.ModelTag ||
            value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
            value.PromptVersion != QwenDeepAnalysisContract.PromptVersion ||
            value.PromptHash != QwenDeepAnalysisContract.PromptHash ||
            value.ClassificationFingerprint != QwenDeepAnalysisContract.ClassificationFingerprint(
                inputFingerprint, catalog) || value.Predictions.Count != catalog.Concepts.Count ||
            !value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalog.Concepts.Select(item => item.Id)))
            throw new InvalidDataException("The technical preflight did not return one valid structured judgment for every concept.");
        return Task.FromResult(value);
    }

    private static bool PredictionsEqual(IReadOnlyList<SemanticConceptPrediction> left,
        IReadOnlyList<SemanticConceptPrediction> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First.ConceptId == pair.Second.ConceptId &&
            pair.First.Matched == pair.Second.Matched);

    private static async Task WriteAtomicallyAsync<T>(string path, T value,
        CancellationToken token)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, JsonOptions), token);
        File.Move(temporary, path, true);
    }
}
