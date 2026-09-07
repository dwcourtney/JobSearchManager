// Research-only: compiled by research/qwen/Jsm.Qwen.Research.csproj, never the web app.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public sealed record ClassifierRequest(string JobId, string Title, string Description);

public sealed record QwenDeepAnalysisResponse(
    bool Received, string JobId, string Title, string PostingContentHash,
    string ModelId, string ModelTag, string ModelDigest,
    int TaxonomyVersion, string TaxonomyFingerprint,
    string PromptVersion, string PromptHash, string OutputContractVersion,
    string OutputSchemaHash, string ClassificationFingerprint,
    DateTimeOffset AnalyzedUtc, IReadOnlyList<SemanticConceptPrediction> Predictions,
    string Analysis, QwenInferenceMetrics? Inference = null);

public static class QwenDeepAnalysisContract
{
    public const string ModelId = "Qwen/Qwen3-4B-Instruct-2507";
    public const string ModelTag = "qwen3:4b-instruct-2507-q4_K_M";
    public const string ModelDigest =
        "sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0";
    public const string PromptVersion = "job-fit-85-compact-json-v2";
    public const string PromptHash =
        "3b78608ac218ae11e021598b7ad386315674ecb9abba18840e93f8ee36f05d98";
    public const string OutputContractVersion = "compact-85-boolean-map-v2";
    public const string OutputSchemaHash =
        "15e934183d07749e0db4c3cb4f3bef51a5507285fad23025196ae4f184ad2ef8";
    public const int ContextLength = 8192;
    public const int MaximumOutputTokens = 3072;
    public const int Seed = 42;
    public const double Temperature = 0;

    public static string ClassificationFingerprint(
        string postingContentHash,
        JobConceptCatalog catalog) => Hash(string.Join('\n',
            postingContentHash, catalog.Version, catalog.Fingerprint,
            ModelId, ModelTag, ModelDigest, PromptVersion, PromptHash,
            OutputContractVersion, OutputSchemaHash,
            Temperature, Seed, ContextLength, MaximumOutputTokens));

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
            var contentHash = SemanticRulesetFingerprint.PostingContentHash(
                request.Title, request.Description);
            var expectedIds = catalog.Concepts.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var predictionsValid = value?.Predictions is { Count: 85 } &&
                value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(expectedIds);
            if (value is null || !value.Received || value.JobId != request.JobId ||
                value.Title != request.Title || value.PostingContentHash != contentHash ||
                value.ModelId != QwenDeepAnalysisContract.ModelId ||
                value.ModelTag != QwenDeepAnalysisContract.ModelTag ||
                value.ModelDigest != QwenDeepAnalysisContract.ModelDigest ||
                value.TaxonomyVersion != catalog.Version ||
                value.TaxonomyFingerprint != catalog.Fingerprint ||
                value.PromptVersion != QwenDeepAnalysisContract.PromptVersion ||
                value.PromptHash != QwenDeepAnalysisContract.PromptHash ||
                value.OutputContractVersion != QwenDeepAnalysisContract.OutputContractVersion ||
                value.OutputSchemaHash != QwenDeepAnalysisContract.OutputSchemaHash || !predictionsValid ||
                value.ClassificationFingerprint != QwenDeepAnalysisContract.ClassificationFingerprint(
                    contentHash, catalog) ||
                string.IsNullOrWhiteSpace(value.Analysis) || !ValidMetrics(value.Inference))
                return null;
            return new QwenDeepAnalysis(value.PostingContentHash, value.ModelId,
                value.ModelTag, value.ModelDigest, value.TaxonomyVersion,
                value.TaxonomyFingerprint, value.PromptVersion, value.PromptHash,
                value.ClassificationFingerprint, value.AnalyzedUtc, value.Predictions,
                value.Analysis, value.Inference);
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

    private static bool ValidMetrics(QwenInferenceMetrics? value) => value is not null &&
        new long?[] { value.TotalDurationNanoseconds, value.LoadDurationNanoseconds,
            value.PromptTokenCount, value.PromptDurationNanoseconds, value.OutputTokenCount,
            value.OutputDurationNanoseconds, value.ModelResidentBytes, value.ModelVramBytes,
            value.AdapterPeakResidentBytes }.All(item => item is null or >= 0) &&
        value.TokensPerSecond is null or >= 0;
}
