using System.IO.Compression;
using System.Text.Json;

namespace JobSearchManager;

public sealed record RegexCacheReconciliationReport(
    string CacheRoot, int CacheFilesInspected, int JobsInspected, int StaleResultsFound,
    int RecomputedResults, int InconsistenciesRepaired, int FilesUpdated, int FilesSkipped,
    double ElapsedMilliseconds, string RulesetFingerprint, string TaxonomyFingerprint);

public static class RegexCacheReconciler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<RegexCacheReconciliationReport> ReconcileAsync(string cacheRoot,
        RegexSemanticClassifier classifier, JobConceptCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(cacheRoot);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var files = 0;
        var inspected = 0;
        var stale = 0;
        var recomputed = 0;
        var repaired = 0;
        var updatedFiles = 0;
        var skipped = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JobsCacheDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<JobsCacheDocument>(
                    await File.ReadAllBytesAsync(path, cancellationToken), JsonOptions);
            }
            catch (JsonException)
            {
                skipped++;
                continue;
            }
            if (document?.Jobs is null || document.Jobs.Count == 0)
            {
                skipped++;
                continue;
            }
            files++;
            var changed = false;
            var jobs = new List<JobRecord>(document.Jobs.Count);
            foreach (var original in document.Jobs)
            {
                inspected++;
                var description = await DescriptionAsync(original, cancellationToken);
                if (string.IsNullOrWhiteSpace(description))
                {
                    jobs.Add(original);
                    continue;
                }
                var hydrated = original with { DescriptionHtml = description };
                if (IsCurrent(hydrated, classifier, catalog))
                {
                    jobs.Add(original);
                    continue;
                }
                stale++;
                var result = classifier.Classify(hydrated.Title, hydrated.DescriptionHtml,
                    hydrated.RemoteWork, hydrated.ExtendedLocationRequirement, productionUsage: false);
                var matched = result.Concepts.Select(item => item.ConceptId)
                    .ToHashSet(StringComparer.Ordinal);
                var predictions = catalog.Concepts.Select(item =>
                    new SemanticConceptPrediction(item.Id, matched.Contains(item.Id))).ToArray();
                var fingerprint = SemanticRulesetFingerprint.ClassificationFingerprint(
                    result.PostingContentHash, result.RulesetFingerprint, catalog.Fingerprint);
                var classification = new SemanticJobClassification(result.PostingContentHash,
                    catalog.Version, catalog.Fingerprint, "deterministic-regex", "jsm-semantic-regex",
                    "lifecycle-managed", result.RulesetFingerprint, "", "", "",
                    result.ClassifiedUtc, fingerprint, predictions, "sqlite-regex-v1",
                    result.RulesetFingerprint);
                var authoritative = result.Concepts.OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                    .ToArray();
                var previous = (original.DetectedConcepts ?? []).Select(item => item.ConceptId)
                    .OrderBy(value => value, StringComparer.Ordinal);
                if (!previous.SequenceEqual(authoritative.Select(item => item.ConceptId))) repaired++;
                jobs.Add(original with
                {
                    SemanticClassification = classification,
                    SemanticClassificationStatus = SemanticClassificationStates.Complete,
                    SemanticClassificationLastAttemptUtc = result.ClassifiedUtc,
                    DetectedConcepts = authoritative
                });
                recomputed++;
                changed = true;
            }
            if (!changed) continue;
            var revised = document with { Jobs = jobs };
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(revised, JsonOptions),
                cancellationToken);
            File.Move(temporary, path, true);
            updatedFiles++;
        }
        timer.Stop();
        return new(root, files, inspected, stale, recomputed, repaired, updatedFiles, skipped,
            timer.Elapsed.TotalMilliseconds, classifier.RulesetFingerprint, catalog.Fingerprint);
    }

    private static bool IsCurrent(JobRecord job, RegexSemanticClassifier classifier,
        JobConceptCatalog catalog)
    {
        if (job.SemanticClassification is null) return false;
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = SemanticRulesetFingerprint.PostingContentHash(job.Title, description);
        var value = job.SemanticClassification;
        return value.PostingContentHash == contentHash && value.TaxonomyVersion == catalog.Version &&
            value.TaxonomyFingerprint == catalog.Fingerprint &&
            value.ModelType == "deterministic-regex" && value.ModelId == "jsm-semantic-regex" &&
            value.ModelDigest == classifier.RulesetFingerprint &&
            value.ClassifierConfigurationVersion == "sqlite-regex-v1" &&
            value.ClassifierConfigurationFingerprint == classifier.RulesetFingerprint &&
            value.ClassificationFingerprint == SemanticRulesetFingerprint.ClassificationFingerprint(
                contentHash, classifier.RulesetFingerprint, catalog.Fingerprint) &&
            value.Predictions.Count == catalog.Concepts.Count &&
            value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalog.Concepts.Select(item => item.Id));
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
}
