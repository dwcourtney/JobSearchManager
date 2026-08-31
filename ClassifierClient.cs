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

public sealed record LlmPrediction(string ConceptId, bool Matched);
public sealed record LlmClassifierResponse(
    bool Received, string JobId, string Title, int DescriptionLength,
    string ServiceVersion, string ProtocolVersion, string Revision,
    bool GpuAvailable, int DeviceCount, string? DeviceName,
    int? VramTotalMiB, int? VramUsedMiB, string? DriverVersion,
    string ModelType, string ModelId, string ModelTag, string ModelDigest,
    string Quantization, string OllamaVersion, string Device,
    string PromptVersion, string PromptHash, double Temperature, int Seed,
    int ContextLength, int MaxOutputTokens, long? TotalDurationNanoseconds,
    long? LoadDurationNanoseconds, int? PromptTokenCount, int? OutputTokenCount,
    double? TokensPerSecond, double InferenceMilliseconds, int MalformedOutputCount,
    IReadOnlyList<LlmPrediction> Predictions);
public sealed record LlmClassifierResult(
    bool Available, double RoundTripMilliseconds,
    LlmClassifierResponse? Response, string? Error);

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

    public async Task<LlmClassifierResult> ClassifyLlmAsync(
        ClassifierRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var content = CreateJsonContent(request);
            using var response = await httpClient.PostAsync(
                "classify-llm", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                    $"Classifier returned HTTP {(int)response.StatusCode}.");
            var result = await response.Content.ReadFromJsonAsync<LlmClassifierResponse>(
                JsonOptions, cancellationToken);
            var validPredictions = result?.Predictions is { Count: 8 } &&
                result.Predictions.Select(item => item.ConceptId).Distinct(StringComparer.Ordinal).Count() == 8 &&
                result.Predictions.All(item => LlmEvaluationService.Concepts.Any(
                    concept => concept.ConceptId == item.ConceptId));
            if (result is null || !result.Received || !validPredictions ||
                result.ModelType != "generative-llm" || result.PromptHash.Length != 64 ||
                result.PromptVersion != "phase3-general-evidence-v2" || result.Temperature != 0 ||
                result.ContextLength != 8192 || result.MalformedOutputCount != 0 ||
                result.ModelDigest != "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0" ||
                !result.GpuAvailable ||
                result.DeviceCount != 1 || result.DeviceName != "NVIDIA GeForce GTX 1070" ||
                result.Device != "cuda:0" || result.JobId != request.JobId ||
                result.Title != request.Title ||
                result.DescriptionLength != request.Description.EnumerateRunes().Count())
                return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                    "Classifier LLM response validation failed.");
            return new(true, stopwatch.Elapsed.TotalMilliseconds, result, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("LLM classifier unavailable for job {JobId}: {FailureType}.",
                LogValue(request.JobId), exception.GetType().Name);
            return new(false, stopwatch.Elapsed.TotalMilliseconds, null,
                "Classifier service is unavailable.");
        }
    }

    private static string LogValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 160)];
}
