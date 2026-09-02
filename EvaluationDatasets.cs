using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public sealed record HoldoutSamplingPlan(
    int Version, string PopulationDefinition, IReadOnlyList<string>? SourceCompanies,
    DateOnly? MinimumPostingDate, DateOnly? MaximumPostingDate, string AvailabilityCriteria,
    int SampleSize, string SamplingMethod, long RandomSeed);

public sealed record EvaluationExample(
    string EvaluationExampleId, string DatasetRole, string SamplingRunId,
    DateTimeOffset? FirstSeenUtc, string LabelStatus, string LabelProvenance,
    bool DetectorOutputExposedDuringLabeling, bool UsedForRuleDevelopment,
    DateTimeOffset? ContaminatedUtc, string? ContaminationReason,
    string PostingContentHash, string CompanyId, string RequisitionId, string Title,
    string DescriptionHtml, string SourceUrl, DateOnly? PostingDate,
    bool IsSourceAvailable, bool? ExpectedPresent = null, string? ConceptId = null,
    double? PredictionScore = null);

public sealed record HoldoutSampleDocument(
    int Version, string DatasetRole, string DatasetStatus, string SamplingRunId,
    DateTimeOffset SampledUtc, HoldoutSamplingPlan Plan, string PlanFingerprint,
    string PopulationFingerprint, int PopulationSize, string SampleFingerprint,
    string LabelingInstruction, IReadOnlyList<EvaluationExample> Examples);

public static class EvaluationDatasetValidation
{
    public static void ValidateForUnbiasedHoldoutScoring(EvaluationExample example)
    {
        if (example.DatasetRole != EvaluationDatasetRoles.ProductionHoldout)
            throw new InvalidDataException("Only production-holdout examples belong in unbiased holdout scoring.");
        if (example.LabelStatus is "unresolved" or "ambiguous/unresolved" ||
            example.ExpectedPresent is null || string.IsNullOrWhiteSpace(example.ConceptId))
            throw new InvalidDataException("A holdout example needs an independent resolved label before scoring.");
        if (example.DetectorOutputExposedDuringLabeling || example.UsedForRuleDevelopment ||
            example.ContaminatedUtc is not null || !string.IsNullOrWhiteSpace(example.ContaminationReason))
            throw new InvalidDataException("A contaminated holdout example cannot count as unseen test evidence.");
        if (example.LabelProvenance is "unlabeled" or "regex-prediction" or "qwen-prediction")
            throw new InvalidDataException("Detector predictions are not independent ground truth.");
    }
}

public static class ProductionHoldoutSampler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true };

    public static async Task<HoldoutSampleDocument> SampleAsync(string cacheRoot,
        HoldoutSamplingPlan plan, CancellationToken cancellationToken = default)
    {
        Validate(plan);
        var sampledUtc = DateTimeOffset.UtcNow;
        var planJson = JsonSerializer.Serialize(plan, JsonOptions).Replace("\r\n", "\n");
        var planFingerprint = Hash(planJson);
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var historyCache = new Dictionary<string, IReadOnlyDictionary<string, JobHistoryEntry>>(
            StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.GetFullPath(cacheRoot), "*.json",
                     SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = JsonSerializer.Deserialize<JobsCacheDocument>(
                    await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
                if (document?.Jobs is null) continue;
                var history = await HistoryForCacheAsync(path, historyCache, cancellationToken);
                foreach (var job in document.Jobs)
                {
                    if (!Eligible(job, plan)) continue;
                    var description = await DescriptionAsync(job, cancellationToken);
                    if (string.IsNullOrWhiteSpace(description)) continue;
                    var contentHash = Hash($"{job.Title}\n{description}");
                    var candidate = new Candidate(job, description, contentHash,
                        history.TryGetValue(job.StableId, out var historyEntry)
                            ? historyEntry.FirstSeenAt : null,
                        Hash($"{plan.RandomSeed}\n{contentHash}\n{job.StableId}"));
                    candidates[$"{job.StableId}:{contentHash}"] = candidate;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException
                                               or FormatException) { }
        }

        var population = candidates.Values.OrderBy(value => value.Job.StableId, StringComparer.Ordinal)
            .ThenBy(value => value.ContentHash, StringComparer.Ordinal).ToArray();
        var populationFingerprint = Hash(string.Join("\n", population.Select(value =>
            $"{value.Job.StableId}|{value.ContentHash}")));
        var selected = population.OrderBy(value => value.RandomRank, StringComparer.Ordinal)
            .ThenBy(value => value.Job.StableId, StringComparer.Ordinal).Take(plan.SampleSize).ToArray();
        var samplingRunId = $"holdout-{planFingerprint[..12]}-{populationFingerprint[..12]}";
        var examples = selected.Select(value => new EvaluationExample(
            Hash($"{samplingRunId}\n{value.Job.StableId}\n{value.ContentHash}"),
            EvaluationDatasetRoles.ProductionHoldout, samplingRunId, value.FirstSeenUtc,
            "unresolved", "unlabeled",
            false, false, null, null, value.ContentHash, value.Job.CompanyId,
            value.Job.RequisitionId, value.Job.Title, value.Description, value.Job.SourceUrl,
            value.Job.StartDate, value.Job.IsSourceAvailable)).ToArray();
        var sampleFingerprint = Hash(string.Join("\n", examples.Select(value =>
            $"{value.EvaluationExampleId}|{value.PostingContentHash}")));
        return new(1, EvaluationDatasetRoles.ProductionHoldout, "frozen-unlabeled", samplingRunId,
            sampledUtc, plan, planFingerprint, populationFingerprint, population.Length,
            sampleFingerprint,
            "Label independently without RegEx or Qwen output. Unresolved or contaminated examples must not be scored.",
            examples);
    }

    public static async Task WriteAtomicallyAsync(HoldoutSampleDocument sample, string outputPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(sample, JsonOptions),
            cancellationToken);
        File.Move(temporary, fullPath, true);
    }

    private static void Validate(HoldoutSamplingPlan plan)
    {
        if (plan.Version != 1 || plan.SampleSize <= 0 || plan.SampleSize > 10_000)
            throw new InvalidDataException("Holdout plan version or sample size is invalid.");
        if (plan.SamplingMethod != "simple-random")
            throw new InvalidDataException("Only predefined simple-random sampling is currently supported.");
        if (plan.AvailabilityCriteria is not ("active-and-inactive" or "active-only" or "inactive-only"))
            throw new InvalidDataException("Availability criteria is invalid.");
        if (string.IsNullOrWhiteSpace(plan.PopulationDefinition))
            throw new InvalidDataException("The source population must be defined before sampling.");
    }

    private static bool Eligible(JobRecord job, HoldoutSamplingPlan plan)
    {
        if (plan.SourceCompanies is { Count: > 0 } &&
            !plan.SourceCompanies.Contains(job.CompanyId, StringComparer.Ordinal)) return false;
        if (plan.MinimumPostingDate is not null && job.StartDate < plan.MinimumPostingDate) return false;
        if (plan.MaximumPostingDate is not null && job.StartDate > plan.MaximumPostingDate) return false;
        return plan.AvailabilityCriteria switch
        {
            "active-only" => job.IsSourceAvailable,
            "inactive-only" => !job.IsSourceAvailable,
            _ => true
        };
    }

    private static async Task<string> DescriptionAsync(JobRecord job, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(job.DescriptionHtml)) return job.DescriptionHtml;
        if (string.IsNullOrWhiteSpace(job.CompressedDescriptionHtml)) return "";
        await using var input = new MemoryStream(Convert.FromBase64String(job.CompressedDescriptionHtml));
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync(token);
    }

    private static async Task<IReadOnlyDictionary<string, JobHistoryEntry>> HistoryForCacheAsync(
        string cachePath,
        IDictionary<string, IReadOnlyDictionary<string, JobHistoryEntry>> cache,
        CancellationToken token)
    {
        var directory = Directory.GetParent(cachePath);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "job-history.json");
            if (File.Exists(path))
            {
                if (cache.TryGetValue(path, out var known)) return known;
                try
                {
                    var document = JsonSerializer.Deserialize<JobHistoryDocument>(
                        await File.ReadAllBytesAsync(path, token), JsonOptions);
                    known = document?.Jobs ?? new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal);
                }
                catch (JsonException)
                {
                    known = new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal);
                }
                cache[path] = known;
                return known;
            }
            directory = directory.Parent;
        }
        return new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Candidate(JobRecord Job, string Description, string ContentHash,
        DateTimeOffset? FirstSeenUtc, string RandomRank);
}
