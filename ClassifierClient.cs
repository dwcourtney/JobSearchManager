using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JobSearchManager;

public sealed record ClassifierRequest(string JobId, string Title, string Description);

public sealed record ClassifierResponse(
    bool Received,
    string JobId,
    string Title,
    int DescriptionLength,
    string ServiceVersion,
    string ProtocolVersion,
    string Revision,
    bool GpuAvailable,
    int DeviceCount,
    string? DeviceName,
    int? VramTotalMiB,
    int? VramUsedMiB,
    string? DriverVersion);

public sealed record ClassifierDiagnosticResult(
    bool Available,
    double RoundTripMilliseconds,
    ClassifierResponse? Response,
    string? Error)
{
    public static ClassifierDiagnosticResult Unavailable(double elapsed, string error) =>
        new(false, elapsed, null, error);
}

public sealed record EmbeddingPrediction(string ConceptId, double Similarity, bool Matched);
public sealed record EmbeddingClassifierResponse(
    bool Received, string JobId, string Title, int DescriptionLength,
    string ServiceVersion, string ProtocolVersion, string Revision,
    bool GpuAvailable, int DeviceCount, string? DeviceName,
    int? VramTotalMiB, int? VramUsedMiB, string? DriverVersion,
    string ModelType, string ModelId, string ModelRevision, string Device,
    int EmbeddingDimension, string ConceptEmbeddingCacheKey,
    int ConceptEmbeddingMemoryBytes, double ConceptEmbeddingNormMin,
    double ConceptEmbeddingNormMax, double ModelLoadMilliseconds,
    double ConceptEmbeddingInitializationMilliseconds, string Aggregation, double Threshold,
    int TokenCount, int ChunkCount, double InferenceMilliseconds,
    IReadOnlyList<EmbeddingPrediction> Predictions);
public sealed record EmbeddingClassifierResult(
    bool Available, double RoundTripMilliseconds,
    EmbeddingClassifierResponse? Response, string? Error);

public sealed class ClassifierClient(HttpClient httpClient, ILogger<ClassifierClient> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static ByteArrayContent CreateJsonContent<T>(T value)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    public async Task<ClassifierDiagnosticResult> ClassifyAsync(
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
                logger.LogWarning("Classifier rejected job {JobId} with HTTP {StatusCode}.",
                    LogValue(request.JobId), (int)response.StatusCode);
                return ClassifierDiagnosticResult.Unavailable(
                    stopwatch.Elapsed.TotalMilliseconds,
                    $"Classifier returned HTTP {(int)response.StatusCode}.");
            }
            var result = await response.Content.ReadFromJsonAsync<ClassifierResponse>(
                JsonOptions, cancellationToken);
            if (result is null || !result.Received ||
                !string.Equals(result.JobId, request.JobId, StringComparison.Ordinal) ||
                !string.Equals(result.Title, request.Title, StringComparison.Ordinal) ||
                result.DescriptionLength != request.Description.EnumerateRunes().Count())
            {
                logger.LogWarning("Classifier returned an invalid contract response for job {JobId}.",
                    LogValue(request.JobId));
                return ClassifierDiagnosticResult.Unavailable(
                    stopwatch.Elapsed.TotalMilliseconds, "Classifier response validation failed.");
            }
            logger.LogInformation(
                "Classifier round trip succeeded for job {JobId} in {DurationMs:F1} ms; gpuAvailable={GpuAvailable}.",
                LogValue(request.JobId), stopwatch.Elapsed.TotalMilliseconds, result.GpuAvailable);
            return new(true, stopwatch.Elapsed.TotalMilliseconds, result, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Classifier unavailable for job {JobId}: {FailureType}.",
                LogValue(request.JobId), exception.GetType().Name);
            return ClassifierDiagnosticResult.Unavailable(
                stopwatch.Elapsed.TotalMilliseconds, "Classifier service is unavailable.");
        }
    }

    public async Task<EmbeddingClassifierResult> ClassifyEmbeddingAsync(
        ClassifierRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = CreateJsonContent(request);
            using var response = await httpClient.PostAsync(
                "classify-embedding", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                    $"Classifier returned HTTP {(int)response.StatusCode}.");
            var result = await response.Content.ReadFromJsonAsync<EmbeddingClassifierResponse>(
                JsonOptions, cancellationToken);
            var validPredictions = result?.Predictions is { Count: 8 } &&
                result.Predictions.Select(item => item.ConceptId).Distinct(StringComparer.Ordinal).Count() == 8 &&
                result.Predictions.All(item => item.Similarity is >= -1 and <= 1 &&
                    item.Matched == (item.Similarity >= result.Threshold));
            if (result is null || !result.Received || !validPredictions ||
                result.ModelType != "embedding" || result.EmbeddingDimension != 768 ||
                result.Aggregation != "max" || result.ConceptEmbeddingCacheKey.Length != 64 ||
                result.ConceptEmbeddingNormMin is < .99999 or > 1.00001 ||
                result.ConceptEmbeddingNormMax is < .99999 or > 1.00001 ||
                !result.GpuAvailable ||
                result.DeviceCount != 1 || result.DeviceName != "NVIDIA GeForce GTX 1070" ||
                result.Device != "cuda:0" || result.JobId != request.JobId ||
                result.Title != request.Title ||
                result.DescriptionLength != request.Description.EnumerateRunes().Count())
                return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                    "Classifier embedding response validation failed.");
            return new(true, stopwatch.Elapsed.TotalMilliseconds, result, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Embedding classifier unavailable for job {JobId}: {FailureType}.",
                LogValue(request.JobId), exception.GetType().Name);
            return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                "Classifier service is unavailable.");
        }
    }

    private static string LogValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 160)];
}
