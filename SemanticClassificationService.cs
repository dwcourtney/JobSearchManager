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
        var contentHash = InputFingerprint(job);
        var value = job.SemanticClassification;
        return value.PostingContentHash == contentHash &&
            value.TaxonomyVersion == catalog.Version &&
            value.TaxonomyFingerprint == catalog.Fingerprint &&
            value.ModelType == "deterministic-regex" &&
            value.ModelId == "jsm-semantic-regex" &&
            value.ModelDigest == regexClassifier.RulesetFingerprint &&
            value.ClassifierConfigurationVersion == regexClassifier.AuthorityTag &&
            value.ClassifierConfigurationFingerprint == ExpectedConfigurationFingerprint &&
            value.ClassificationFingerprint ==
                SemanticRulesetFingerprint.ClassificationFingerprint(
                    contentHash, regexClassifier.RulesetFingerprint, catalog.Fingerprint) &&
            value.Predictions.Count == 85 &&
            value.Predictions.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(catalog.Concepts.Select(item => item.Id));
    }

    // JSON freshness includes every consumed fact and its relevant provider inputs. UTC cache
    // timestamps are deliberately excluded. The SQLite branch is rollback/test compatibility.
    public string InputFingerprint(JobRecord job) => regexClassifier.UsesSqliteCompatibility
        ? SemanticRulesetFingerprint.PostingContentHash(job.Title, JobAnalysis.HtmlToPlainText(job.DescriptionHtml))
        : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                contract = "posting-and-consumed-facts-v1", job.Title,
                description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml),
                job.CompanyId, job.PrimaryLocation, job.AdditionalLocations,
                job.RemoteWork, job.ExtendedLocationRequirement
            })));

    public bool CanPersist(JobRecord job, SemanticJobClassification classification) =>
        IsCurrent(job with { SemanticClassification = classification });

    public Task<SemanticClassificationAttempt> ClassifyAsync(
        JobRecord job,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.DescriptionHtml))
            return Task.FromResult(new SemanticClassificationAttempt(false, null, "Description unavailable."));
        var description = JobAnalysis.HtmlToPlainText(job.DescriptionHtml);
        var contentHash = InputFingerprint(job);
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
            job.ExtendedLocationRequirement, productionUsage: regexClassifier.UsesSqliteCompatibility);
        var inputHash = InputFingerprint(job);
        var fingerprint = SemanticRulesetFingerprint.ClassificationFingerprint(
            inputHash, result.RulesetFingerprint, catalog.Fingerprint);
        var matched = result.Concepts.Select(item => item.ConceptId).ToHashSet(StringComparer.Ordinal);
        var classification = new SemanticJobClassification(
            inputHash, catalog.Version, catalog.Fingerprint,
            "deterministic-regex", "jsm-semantic-regex", regexClassifier.UsesSqliteCompatibility ? "lifecycle-managed" : "json-regex-v1",
            result.RulesetFingerprint, "", "", "", result.ClassifiedUtc, fingerprint,
            catalog.Concepts.Select(item => new SemanticConceptPrediction(
                item.Id, matched.Contains(item.Id))).ToArray(),
            regexClassifier.AuthorityTag, result.RulesetFingerprint);
        if (_completed.Count >= MaximumProcessCacheEntries) _completed.Clear();
        _completed[fingerprint] = classification;
        return await Task.FromResult(new SemanticClassificationAttempt(true, classification, null));
    }
}
