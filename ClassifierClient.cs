using System.Diagnostics;
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

public sealed class ClassifierClient(HttpClient httpClient, ILogger<ClassifierClient> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClassifierDiagnosticResult> ClassifyAsync(
        ClassifierRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "classify", request, JsonOptions, cancellationToken);
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

    private static string LogValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 160)];
}
