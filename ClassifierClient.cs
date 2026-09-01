using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public sealed record ClassifierRequest(string JobId, string Title, string Description);
public sealed record SemanticConceptPrediction(string ConceptId, bool Matched, double? Score = null);

public sealed record SemanticClassifierResponse(
    bool Received, string JobId, string Title, int DescriptionLength,
    string ServiceVersion, string ProtocolVersion, string Revision,
    bool GpuAvailable, int DeviceCount, string? DeviceName,
    int? VramTotalMiB, int? VramUsedMiB, string? DriverVersion,
    string ModelType, string ModelId, string ModelRevision, string ModelDigest,
    int TaxonomyVersion, string TaxonomyFingerprint, int ConceptCount,
    string ClassifierConfigurationVersion, string ClassifierConfigurationFingerprint,
    double Threshold, int ChunkTokens, int ChunkOverlap, string PostingContentHash,
    string ClassificationFingerprint, DateTimeOffset ClassifiedUtc, string Device,
    int TokenCount, int ChunkCount, double InferenceMilliseconds,
    IReadOnlyList<SemanticConceptPrediction> Predictions);

public sealed record SemanticClassifierResult(
    bool Available,
    double RoundTripMilliseconds,
    SemanticClassifierResponse? Response,
    string? Error)
{
    public static SemanticClassifierResult Unavailable(double elapsed, string error) =>
        new(false, elapsed, null, error);
}

public sealed record QwenDeepAnalysisResponse(
    bool Received, string JobId, string Title, string PostingContentHash,
    string ModelId, string ModelTag, string ModelDigest,
    DateTimeOffset AnalyzedUtc, string Analysis);

public static class SemanticClassifierContract
{
    public const string ModelId = "cross-encoder/nli-deberta-v3-base";
    public const string ModelRevision = "6c749ce3425cd33b46d187e45b92bbf96ee12ec7";
    public const string ModelDigest =
        "sha256:d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa";
    public const string ConfigurationVersion = "deberta-85-nli-v1";
    public const int ChunkTokens = 384;
    public const int ChunkOverlap = 64;
    public const int MaximumLength = 512;
    public const int ConceptBatchSize = 8;
    public const double Threshold = 0.5;
    public const string HypothesisTemplate =
        "This job assigns the candidate work or conditions matching this concept: {definition}";

    public static string PostingContentHash(string title, string description) =>
        Hash($"{title}\n{description}");

    public static string ConfigurationFingerprint(JobConceptCatalog catalog) =>
        Hash(string.Join('\n', ConfigurationVersion, ModelId, ModelRevision, ModelDigest,
            ChunkTokens, ChunkOverlap, MaximumLength, ConceptBatchSize, Threshold, HypothesisTemplate,
            catalog.Fingerprint));

    public static string ClassificationFingerprint(
        string postingContentHash,
        JobConceptCatalog catalog)
    {
        var material = string.Join('\n',
            postingContentHash,
            catalog.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            catalog.Fingerprint,
            ModelId,
            ModelRevision,
            ModelDigest,
            ConfigurationVersion,
            ConfigurationFingerprint(catalog));
        return Hash(material);
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class ClassifierClient(
    HttpClient httpClient,
    JobConceptCatalog catalog,
    ILogger<ClassifierClient> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static ByteArrayContent CreateJsonContent<T>(T value)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    public async Task<SemanticClassifierResult> ClassifyAsync(
        ClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = CreateJsonContent(request);
            using var response = await httpClient.PostAsync("classify", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Semantic classifier rejected job {JobId} with HTTP {StatusCode}.",
                    LogValue(request.JobId), (int)response.StatusCode);
                return SemanticClassifierResult.Unavailable(
                    stopwatch.Elapsed.TotalMilliseconds,
                    $"Classifier returned HTTP {(int)response.StatusCode}.");
            }

            var result = await response.Content.ReadFromJsonAsync<SemanticClassifierResponse>(
                JsonOptions, cancellationToken);
            var expectedContentHash = SemanticClassifierContract.PostingContentHash(
                request.Title, request.Description);
            var expectedPredictionIds = catalog.Concepts.Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var predictionsValid = result?.Predictions is { Count: 85 } &&
                result.Predictions.Select(item => item.ConceptId)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(expectedPredictionIds);
            if (result is null || !result.Received || !predictionsValid ||
                result.ModelType != "nli-sequence-classifier" ||
                result.ModelId != SemanticClassifierContract.ModelId ||
                result.ModelRevision != SemanticClassifierContract.ModelRevision ||
                result.ModelDigest != SemanticClassifierContract.ModelDigest ||
                result.TaxonomyVersion != catalog.Version ||
                result.TaxonomyFingerprint != catalog.Fingerprint ||
                result.ConceptCount != 85 ||
                result.ClassifierConfigurationVersion != SemanticClassifierContract.ConfigurationVersion ||
                result.ClassifierConfigurationFingerprint !=
                    SemanticClassifierContract.ConfigurationFingerprint(catalog) ||
                result.Threshold != SemanticClassifierContract.Threshold ||
                result.ChunkTokens != SemanticClassifierContract.ChunkTokens ||
                result.ChunkOverlap != SemanticClassifierContract.ChunkOverlap ||
                result.PostingContentHash != expectedContentHash ||
                result.ClassificationFingerprint !=
                    SemanticClassifierContract.ClassificationFingerprint(expectedContentHash, catalog) ||
                !result.GpuAvailable || result.DeviceCount != 1 ||
                result.DeviceName != "NVIDIA GeForce GTX 1070" ||
                result.Device != "cuda:0" || result.JobId != request.JobId ||
                result.Title != request.Title ||
                result.DescriptionLength != request.Description.EnumerateRunes().Count())
            {
                return SemanticClassifierResult.Unavailable(
                    stopwatch.Elapsed.TotalMilliseconds,
                    "Classifier semantic response validation failed.");
            }

            return new(true, stopwatch.Elapsed.TotalMilliseconds, result, null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Semantic classifier unavailable for job {JobId}: {FailureType}.",
                LogValue(request.JobId), exception.GetType().Name);
            return SemanticClassifierResult.Unavailable(
                stopwatch.Elapsed.TotalMilliseconds,
                "Classifier service is unavailable.");
        }
    }

    public async Task<QwenDeepAnalysis?> DeepAnalyzeAsync(
        ClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = CreateJsonContent(request);
            using var response = await httpClient.PostAsync("deep-analyze", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var value = await response.Content.ReadFromJsonAsync<QwenDeepAnalysisResponse>(
                JsonOptions, cancellationToken);
            var contentHash = SemanticClassifierContract.PostingContentHash(
                request.Title, request.Description);
            if (value is null || !value.Received || value.JobId != request.JobId ||
                value.Title != request.Title || value.PostingContentHash != contentHash ||
                value.ModelId != "Qwen/Qwen3-4B-Instruct-2507" ||
                value.ModelTag != "qwen3:4b-instruct-2507-q4_K_M" ||
                value.ModelDigest !=
                    "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0" ||
                string.IsNullOrWhiteSpace(value.Analysis))
                return null;
            return new QwenDeepAnalysis(value.PostingContentHash, value.ModelId,
                value.ModelTag, value.ModelDigest, value.AnalyzedUtc, value.Analysis);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Optional Qwen deep analysis is unavailable for job {JobId}: {FailureType}.",
                LogValue(request.JobId), exception.GetType().Name);
            return null;
        }
    }

    private static string LogValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 160)];
}

public sealed record SemanticClassificationAttempt(
    bool Available,
    SemanticJobClassification? Classification,
    string? Error);

public sealed class SemanticClassificationService(
    ClassifierClient classifier,
    JobConceptCatalog catalog)
{
    private const int MaximumProcessCacheEntries = 4096;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Lazy<Task<SemanticClassificationAttempt>>> _inFlight =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemanticJobClassification> _completed =
        new(StringComparer.Ordinal);

    public string ExpectedConfigurationFingerprint =>
        SemanticClassifierContract.ConfigurationFingerprint(catalog);

    public bool IsCurrent(JobRecord job)
    {
        if (string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.SemanticClassification is null)
            return false;
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = SemanticClassifierContract.PostingContentHash(job.Title, description);
        var value = job.SemanticClassification;
        return value.PostingContentHash == contentHash &&
            value.TaxonomyVersion == catalog.Version &&
            value.TaxonomyFingerprint == catalog.Fingerprint &&
            value.ModelId == SemanticClassifierContract.ModelId &&
            value.ModelDigest == SemanticClassifierContract.ModelDigest &&
            value.ClassifierConfigurationVersion == SemanticClassifierContract.ConfigurationVersion &&
            value.ClassifierConfigurationFingerprint == ExpectedConfigurationFingerprint &&
            value.ClassificationFingerprint ==
                SemanticClassifierContract.ClassificationFingerprint(contentHash, catalog) &&
            value.Predictions.Count == 85 &&
            value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalog.Concepts.Select(item => item.Id));
    }

    public Task<SemanticClassificationAttempt> ClassifyAsync(
        JobRecord job,
        CancellationToken cancellationToken = default)
    {
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = SemanticClassifierContract.PostingContentHash(job.Title, description);
        var fingerprint = SemanticClassifierContract.ClassificationFingerprint(contentHash, catalog);
        if (_completed.TryGetValue(fingerprint, out var cached))
            return Task.FromResult(new SemanticClassificationAttempt(true, cached, null));
        var lazy = _inFlight.GetOrAdd(fingerprint, _ => new Lazy<Task<SemanticClassificationAttempt>>(
            () => ClassifyCoreAsync(job, description, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndRemoveAsync(fingerprint, lazy);
    }

    public Task<QwenDeepAnalysis?> DeepAnalyzeAsync(
        JobRecord job,
        CancellationToken cancellationToken = default)
    {
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        return classifier.DeepAnalyzeAsync(
            new ClassifierRequest(job.StableId, job.Title, description), cancellationToken);
    }

    private async Task<SemanticClassificationAttempt> AwaitAndRemoveAsync(
        string fingerprint,
        Lazy<Task<SemanticClassificationAttempt>> lazy)
    {
        try { return await lazy.Value; }
        finally { _inFlight.TryRemove(new(fingerprint, lazy)); }
    }

    private async Task<SemanticClassificationAttempt> ClassifyCoreAsync(
        JobRecord job,
        string description,
        CancellationToken cancellationToken)
    {
        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            var result = await classifier.ClassifyAsync(
                new ClassifierRequest(job.StableId, job.Title, description), cancellationToken);
            if (!result.Available || result.Response is null)
                return new(false, null, result.Error);
            var response = result.Response;
            var classification = new SemanticJobClassification(
                response.PostingContentHash,
                response.TaxonomyVersion,
                response.TaxonomyFingerprint,
                response.ModelType,
                response.ModelId,
                response.ModelRevision,
                response.ModelDigest,
                "",
                "",
                "",
                response.ClassifiedUtc,
                response.ClassificationFingerprint,
                response.Predictions,
                response.ClassifierConfigurationVersion,
                response.ClassifierConfigurationFingerprint);
            if (_completed.Count >= MaximumProcessCacheEntries)
                _completed.Clear();
            _completed[response.ClassificationFingerprint] = classification;
            return new(true, classification, null);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }
}
