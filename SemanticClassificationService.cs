using System.Collections.Concurrent;

namespace JobSearchManager;

public sealed record SemanticClassificationAttempt(
    bool Available,
    SemanticJobClassification? Classification,
    string? Error);

public sealed class SemanticClassificationService(
    JobConceptCatalog catalog,
    RegexSemanticClassifier regexClassifier)
{
    private const int MaximumProcessCacheEntries = 4096;
    private readonly ConcurrentDictionary<string, Lazy<Task<SemanticClassificationAttempt>>> _inFlight =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemanticJobClassification> _completed =
        new(StringComparer.Ordinal);

    public string ExpectedConfigurationFingerprint =>
        regexClassifier.RulesetFingerprint;

    public bool IsCurrent(JobRecord job)
    {
        if (string.IsNullOrWhiteSpace(job.DescriptionHtml) || job.SemanticClassification is null)
            return false;
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = SemanticRulesetFingerprint.PostingContentHash(job.Title, description);
        var value = job.SemanticClassification;
        return value.PostingContentHash == contentHash &&
            value.TaxonomyVersion == catalog.Version &&
            value.TaxonomyFingerprint == catalog.Fingerprint &&
            value.ModelType == "deterministic-regex" &&
            value.ModelId == "jsm-semantic-regex" &&
            value.ModelDigest == regexClassifier.RulesetFingerprint &&
            value.ClassifierConfigurationVersion == "sqlite-regex-v1" &&
            value.ClassifierConfigurationFingerprint == ExpectedConfigurationFingerprint &&
            value.ClassificationFingerprint ==
                SemanticRulesetFingerprint.ClassificationFingerprint(
                    contentHash, regexClassifier.RulesetFingerprint, catalog.Fingerprint) &&
            value.Predictions.Count == 85 &&
            value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalog.Concepts.Select(item => item.Id));
    }

    public Task<SemanticClassificationAttempt> ClassifyAsync(
        JobRecord job,
        CancellationToken cancellationToken = default)
    {
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = SemanticRulesetFingerprint.PostingContentHash(job.Title, description);
        var fingerprint = SemanticRulesetFingerprint.ClassificationFingerprint(
            contentHash, regexClassifier.RulesetFingerprint, catalog.Fingerprint);
        if (_completed.TryGetValue(fingerprint, out var cached))
            return Task.FromResult(new SemanticClassificationAttempt(true, cached, null));
        var lazy = _inFlight.GetOrAdd(fingerprint, _ => new Lazy<Task<SemanticClassificationAttempt>>(
            () => ClassifyCoreAsync(job, description, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitAndRemoveAsync(fingerprint, lazy);
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
        cancellationToken.ThrowIfCancellationRequested();
        var result = regexClassifier.Classify(job.Title, job.DescriptionHtml, job.RemoteWork,
            job.ExtendedLocationRequirement, productionUsage: true);
        var fingerprint = SemanticRulesetFingerprint.ClassificationFingerprint(
            result.PostingContentHash, result.RulesetFingerprint, catalog.Fingerprint);
        var matched = result.Concepts.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal);
        var classification = new SemanticJobClassification(
            result.PostingContentHash, catalog.Version, catalog.Fingerprint,
            "deterministic-regex", "jsm-semantic-regex", "lifecycle-managed",
            result.RulesetFingerprint, "", "", "", result.ClassifiedUtc, fingerprint,
            catalog.Concepts.Select(item => new SemanticConceptPrediction(
                item.Id, matched.Contains(item.Id))).ToArray(),
            "sqlite-regex-v1", result.RulesetFingerprint);
        if (_completed.Count >= MaximumProcessCacheEntries) _completed.Clear();
        _completed[fingerprint] = classification;
        return await Task.FromResult(new SemanticClassificationAttempt(true, classification, null));
    }
}
