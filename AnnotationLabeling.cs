using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public static class AnnotationDecisions
{
    public const string Correct = "correct";
    public const string Incorrect = "incorrect";
    public const string DifferentLabel = "differentLabel";
    public const string MultipleLabels = "multipleLabels";
    public const string None = "none";
    public const string Unsure = "unsure";

    public static bool IsValid(string? value) => value is
        Correct or Incorrect or DifferentLabel or MultipleLabels or None or Unsure;
}

public sealed record AnnotationMachineProvenance(
    string Method,
    int TaxonomyVersion,
    string TaxonomyFingerprint,
    IReadOnlyList<string> BasisConceptIds,
    string? Model,
    decimal? Confidence);

public sealed record AnnotationSource(
    string Id,
    string ContentHash,
    string JobId,
    string CompanyId,
    string Title,
    string SourceUrl,
    string FullPosting,
    DateTimeOffset CreatedUtc);

public sealed record AnnotationItem(
    string Id,
    string SourceId,
    string JobId,
    string CompanyId,
    string Title,
    string SourceUrl,
    string ContentHash,
    string Evidence,
    string ContextBefore,
    string ContextAfter,
    IReadOnlyList<string> CandidateConceptIds,
    AnnotationMachineProvenance Machine,
    string Status,
    string? Decision,
    IReadOnlyList<string> SelectedConceptIds,
    string? Reviewer,
    DateTimeOffset? ReviewedUtc,
    string? UnsureReason,
    bool TrainingEligible,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record AnnotationCorpus(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Dictionary<string, AnnotationSource> Sources,
    Dictionary<string, AnnotationItem> Items)
{
    public static AnnotationCorpus Empty(DateTimeOffset now) =>
        new(1, now, now, new(StringComparer.Ordinal), new(StringComparer.Ordinal));
}

public sealed record AnnotationGenerateRequest(int MaxItems = 200);
public sealed record AnnotationDecisionRequest(
    string Decision,
    IReadOnlyList<string>? ConceptIds = null,
    string? UnsureReason = null);
public sealed record AnnotationQueueFilter(
    string Status = "unreviewed",
    string? Concept = null,
    string? Company = null);
public sealed record AnnotationStats(
    int Total,
    int Unreviewed,
    int Reviewed,
    int Unsure,
    int TrainingEligible,
    int Accepted,
    int Rejected,
    int Relabeled);
public sealed record AnnotationQueueResponse(
    AnnotationItem? Item,
    AnnotationStats Stats,
    IReadOnlyList<JobConceptOption> Concepts,
    IReadOnlyList<string> Companies,
    IReadOnlyDictionary<string, int> ConceptDistribution,
    IReadOnlyDictionary<string, int> CompanyDistribution,
    string TaxonomyFingerprint,
    int TaxonomyVersion);
public sealed record AnnotationGenerateResult(int Added, int Total, AnnotationQueueResponse Queue);

public sealed class AnnotationLabelingService
{
    // SHA-256-shaped namespace reserved solely for the shared administrator corpus.
    internal const string CorpusWorkspaceId =
        "9442dc573d422e5221312c5a3a9d739056583571492e318cb3963313f0124fd2";
    private const int MinimumPilotSize = 100;
    private const int MaximumPilotSize = 300;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly string[] BoilerplateMarkers =
    [
        "equal opportunity employer", "affirmative action employer", "reasonable accommodation",
        "benefits may include", "employment opportunity without regard"
    ];

    private readonly IWorkspaceDataStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AnnotationLabelingService(
        IWorkspaceDataStoreFactory stores,
        JobConceptCatalog catalog,
        TimeProvider time)
    {
        _store = stores.Create(CorpusWorkspaceId);
        _catalog = catalog;
        _time = time;
        var canonicalTaxonomy = JsonSerializer.Serialize(new
        {
            catalog.Version,
            concepts = catalog.Concepts.OrderBy(item => item.Id, StringComparer.Ordinal)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        TaxonomyFingerprint = Hash(canonicalTaxonomy);
    }

    public string TaxonomyFingerprint { get; }
    public int TaxonomyVersion => _catalog.Version;
    internal string StorageDescription => _store.Describe(WorkspaceDataFile.AnnotationCorpus);

    public async Task<AnnotationGenerateResult> GenerateAsync(
        IEnumerable<JobRecord> jobs,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        maxItems = Math.Clamp(maxItems <= 0 ? 200 : maxItems, MinimumPilotSize, MaximumPilotSize);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _time.GetUtcNow();
            var corpus = await LoadAsync(now, cancellationToken);
            var existingTemplateKeys = corpus.Items.Values
                .Select(TemplateKey)
                .ToHashSet(StringComparer.Ordinal);
            var candidates = new List<(AnnotationSource Source, AnnotationItem Item, string TemplateKey)>();

            foreach (var job in jobs
                .Where(job => !string.IsNullOrWhiteSpace(job.DescriptionHtml) && job.DetectedConcepts is { Count: > 0 })
                .OrderBy(job => job.CompanyId, StringComparer.Ordinal)
                .ThenBy(job => job.StableId, StringComparer.Ordinal))
            {
                var fullPosting = Normalize(JobAnalysis.HtmlToPlainText(job.DescriptionHtml));
                if (fullPosting.Length == 0) continue;
                var contentHash = Hash($"{Normalize(job.Title)}\n{fullPosting}");
                var sourceId = $"source-{contentHash[..24]}";
                var source = new AnnotationSource(
                    sourceId, contentHash, job.StableId, job.CompanyId, job.Title,
                    job.SourceUrl, fullPosting, now);

                foreach (var detected in job.DetectedConcepts!.OrderBy(item => item.ConceptId, StringComparer.Ordinal))
                {
                    if (!_catalog.Contains(detected.ConceptId)) continue;
                    var span = LocateEvidence(job.Title, fullPosting, detected.Evidence);
                    if (span.Evidence.Length == 0) continue;
                    if (IsBoilerplate(span.Evidence)) continue;
                    var detectedDefinition = _catalog.Get(detected.ConceptId);
                    var adjacent = _catalog.Concepts
                        .Where(concept => concept.Id != detected.ConceptId &&
                            concept.Category == detectedDefinition.Category)
                        .OrderBy(concept => Hash($"{contentHash}\n{span.Start}\n{concept.Id}"), StringComparer.Ordinal)
                        .Select(concept => concept.Id)
                        .FirstOrDefault();
                    var proposals = new List<(string ConceptId, string Method)>
                    {
                        (detected.ConceptId, "jsm-deterministic-concept-extraction")
                    };
                    if (adjacent is not null)
                        proposals.Add((adjacent, "jsm-deterministic-adjacent-hard-negative-proposal"));

                    foreach (var proposal in proposals)
                    {
                        var candidateIds = new[] { proposal.ConceptId };
                        var itemId = "annotation-" + Hash(
                            $"{contentHash}\n{span.Start}\n{span.Evidence}\n{proposal.ConceptId}\n{TaxonomyFingerprint}")[..24];
                        var item = new AnnotationItem(
                            itemId, sourceId, job.StableId, job.CompanyId, job.Title, job.SourceUrl,
                            contentHash, span.Evidence, span.Before, span.After, candidateIds,
                            new AnnotationMachineProvenance(
                                proposal.Method, _catalog.Version, TaxonomyFingerprint,
                                [detected.ConceptId], null, null),
                            "unreviewed", null, [], null, null, null, false, now, now);
                        var templateKey = TemplateKey(item);
                        if (!corpus.Items.ContainsKey(itemId) && !existingTemplateKeys.Contains(templateKey))
                        {
                            existingTemplateKeys.Add(templateKey);
                            candidates.Add((source, item, templateKey));
                        }
                    }
                }
            }

            // Round-robin concepts so common detector outputs do not consume the pilot.
            var queues = candidates.GroupBy(candidate => candidate.Item.CandidateConceptIds[0], StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new Queue<(AnnotationSource Source, AnnotationItem Item, string TemplateKey)>(
                    InterleaveCompanies(group)))
                .ToList();
            var added = 0;
            while (added < maxItems && queues.Count > 0)
            {
                for (var index = queues.Count - 1; index >= 0 && added < maxItems; index--)
                {
                    var candidate = queues[index].Dequeue();
                    corpus.Sources.TryAdd(candidate.Source.Id, candidate.Source);
                    corpus.Items.Add(candidate.Item.Id, candidate.Item);
                    added++;
                    if (queues[index].Count == 0) queues.RemoveAt(index);
                }
            }

            if (added > 0)
            {
                corpus = corpus with { UpdatedUtc = now };
                await SaveAsync(corpus, cancellationToken);
            }
            var queue = BuildQueue(corpus, new AnnotationQueueFilter());
            return new AnnotationGenerateResult(added, corpus.Items.Count, queue);
        }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationQueueResponse> GetQueueAsync(
        AnnotationQueueFilter filter,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return BuildQueue(await LoadAsync(_time.GetUtcNow(), cancellationToken), filter); }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationSource?> GetSourceAsync(string itemId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var corpus = await LoadAsync(_time.GetUtcNow(), cancellationToken);
            return corpus.Items.TryGetValue(itemId, out var item) &&
                corpus.Sources.TryGetValue(item.SourceId, out var source) ? source : null;
        }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationQueueResponse?> DecideAsync(
        string itemId,
        AnnotationDecisionRequest request,
        string reviewer,
        AnnotationQueueFilter filter,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateDecision(request);
        if (validationError is not null) throw new ArgumentException(validationError, nameof(request));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _time.GetUtcNow();
            var corpus = await LoadAsync(now, cancellationToken);
            if (!corpus.Items.TryGetValue(itemId, out var item)) return null;
            var selected = (request.ConceptIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (selected.Any(id => !_catalog.Contains(id)))
                throw new ArgumentException("A selected concept is not in the canonical taxonomy.", nameof(request));
            if (request.Decision == AnnotationDecisions.DifferentLabel &&
                selected.Any(item.CandidateConceptIds.Contains))
                throw new ArgumentException("Different label must replace the machine candidate.", nameof(request));
            var unsure = request.Decision == AnnotationDecisions.Unsure;
            corpus.Items[itemId] = item with
            {
                Status = unsure ? "unsure" : "reviewed",
                Decision = request.Decision,
                SelectedConceptIds = selected,
                Reviewer = reviewer,
                ReviewedUtc = now,
                UnsureReason = unsure ? request.UnsureReason?.Trim() : null,
                TrainingEligible = !unsure,
                UpdatedUtc = now
            };
            corpus = corpus with { UpdatedUtc = now };
            await SaveAsync(corpus, cancellationToken);
            return BuildQueue(corpus, filter);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ExportJsonLinesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var corpus = await LoadAsync(_time.GetUtcNow(), cancellationToken);
            var lines = corpus.Items.Values
                .Where(item => item.Status is "reviewed" or "unsure")
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item =>
                {
                    corpus.Sources.TryGetValue(item.SourceId, out var source);
                    var confirmedPresent = item.Decision switch
                    {
                        AnnotationDecisions.Correct => item.CandidateConceptIds,
                        AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels => item.SelectedConceptIds,
                        _ => []
                    };
                    var confirmedAbsentCandidates = (item.Decision switch
                    {
                        AnnotationDecisions.Incorrect or AnnotationDecisions.DifferentLabel or
                            AnnotationDecisions.MultipleLabels or AnnotationDecisions.None => item.CandidateConceptIds,
                        _ => []
                    }).Except(confirmedPresent, StringComparer.Ordinal).ToArray();
                    return JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        item.Id,
                        item.Status,
                        item.Decision,
                        item.TrainingEligible,
                        item.UnsureReason,
                        item.Reviewer,
                        item.ReviewedUtc,
                        item.JobId,
                        item.ContentHash,
                        item.CompanyId,
                        item.Title,
                        item.SourceUrl,
                        item.Evidence,
                        item.ContextBefore,
                        item.ContextAfter,
                        fullPosting = source?.FullPosting,
                        candidateConceptIds = item.CandidateConceptIds,
                        confirmedPresentConceptIds = confirmedPresent,
                        confirmedAbsentCandidateConceptIds = confirmedAbsentCandidates,
                        machine = item.Machine
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                });
            return string.Join('\n', lines) + (corpus.Items.Values.Any(item => item.Status is "reviewed" or "unsure") ? "\n" : "");
        }
        finally { _gate.Release(); }
    }

    internal static string? ValidateDecision(AnnotationDecisionRequest request)
    {
        if (!AnnotationDecisions.IsValid(request.Decision)) return "Choose a valid annotation decision.";
        var count = request.ConceptIds?.Distinct(StringComparer.Ordinal).Count() ?? 0;
        if (request.Decision == AnnotationDecisions.DifferentLabel && count != 1)
            return "Different label requires exactly one concept.";
        if (request.Decision == AnnotationDecisions.MultipleLabels && count < 2)
            return "Multiple labels requires at least two concepts.";
        if (request.Decision is AnnotationDecisions.Correct or AnnotationDecisions.Incorrect or
            AnnotationDecisions.None or AnnotationDecisions.Unsure && count != 0)
            return "This decision does not accept replacement concepts.";
        return null;
    }

    private AnnotationQueueResponse BuildQueue(AnnotationCorpus corpus, AnnotationQueueFilter filter)
    {
        var status = string.IsNullOrWhiteSpace(filter.Status) ? "unreviewed" : filter.Status.Trim();
        var items = corpus.Items.Values.Where(item => status switch
        {
            "all" => true,
            "trainingEligible" => item.TrainingEligible,
            "excluded" => item.Status == "unsure" || item.Decision == AnnotationDecisions.None,
            "disagreement" => item.Decision is AnnotationDecisions.Incorrect or
                AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels or AnnotationDecisions.None,
            _ => item.Status == status
        });
        if (!string.IsNullOrWhiteSpace(filter.Concept))
            items = items.Where(item => item.CandidateConceptIds.Contains(filter.Concept, StringComparer.Ordinal) ||
                item.SelectedConceptIds.Contains(filter.Concept, StringComparer.Ordinal));
        if (!string.IsNullOrWhiteSpace(filter.Company))
            items = items.Where(item => item.CompanyId == filter.Company);
        var next = items.OrderBy(item => item.UpdatedUtc).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        return new AnnotationQueueResponse(
            next, Stats(corpus), _catalog.Options,
            corpus.Items.Values.Select(item => item.CompanyId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray(),
            corpus.Items.Values.SelectMany(item => item.SelectedConceptIds.Count > 0
                    ? item.SelectedConceptIds : item.CandidateConceptIds)
                .GroupBy(id => id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            corpus.Items.Values.GroupBy(item => item.CompanyId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            TaxonomyFingerprint, TaxonomyVersion);
    }

    private static AnnotationStats Stats(AnnotationCorpus corpus) => new(
        corpus.Items.Count,
        corpus.Items.Values.Count(item => item.Status == "unreviewed"),
        corpus.Items.Values.Count(item => item.Status == "reviewed"),
        corpus.Items.Values.Count(item => item.Status == "unsure"),
        corpus.Items.Values.Count(item => item.TrainingEligible),
        corpus.Items.Values.Count(item => item.Decision == AnnotationDecisions.Correct),
        corpus.Items.Values.Count(item => item.Decision is AnnotationDecisions.Incorrect or AnnotationDecisions.None),
        corpus.Items.Values.Count(item => item.Decision is AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels));

    private static IEnumerable<(AnnotationSource Source, AnnotationItem Item, string TemplateKey)> InterleaveCompanies(
        IEnumerable<(AnnotationSource Source, AnnotationItem Item, string TemplateKey)> candidates)
    {
        var companyQueues = candidates.GroupBy(candidate => candidate.Item.CompanyId, StringComparer.Ordinal)
            .OrderBy(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Queue<(AnnotationSource Source, AnnotationItem Item, string TemplateKey)>(group))
            .ToList();
        while (companyQueues.Count > 0)
        {
            for (var index = companyQueues.Count - 1; index >= 0; index--)
            {
                yield return companyQueues[index].Dequeue();
                if (companyQueues[index].Count == 0) companyQueues.RemoveAt(index);
            }
        }
    }

    private async Task<AnnotationCorpus> LoadAsync(DateTimeOffset now, CancellationToken token) =>
        await _store.ReadJsonAsync<AnnotationCorpus>(WorkspaceDataFile.AnnotationCorpus, token)
            ?? AnnotationCorpus.Empty(now);

    private Task SaveAsync(AnnotationCorpus corpus, CancellationToken token) =>
        _store.WriteJsonAsync(WorkspaceDataFile.AnnotationCorpus, corpus, token);

    private static (string Evidence, string Before, string After, int Start) LocateEvidence(
        string title, string posting, string detectorEvidence)
    {
        var evidence = Normalize(detectorEvidence);
        var alternatives = evidence.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Prepend(evidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matchedEvidence = alternatives.FirstOrDefault(candidate =>
            posting.Contains(candidate, StringComparison.OrdinalIgnoreCase)) ?? "";
        var index = matchedEvidence.Length == 0 ? -1 : posting.IndexOf(matchedEvidence, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && !string.IsNullOrWhiteSpace(title))
        {
            matchedEvidence = alternatives.FirstOrDefault(candidate =>
                title.Contains(candidate, StringComparison.OrdinalIgnoreCase)) ?? "";
            var titleIndex = matchedEvidence.Length == 0 ? -1 : title.IndexOf(matchedEvidence, StringComparison.OrdinalIgnoreCase);
            if (titleIndex >= 0)
                return (title.Substring(titleIndex, matchedEvidence.Length), "", posting[..Math.Min(360, posting.Length)], -1);
        }
        if (index < 0)
            return ("", "", "", -1);
        var start = Math.Max(0, index - 220);
        var end = Math.Min(posting.Length, index + matchedEvidence.Length + 220);
        return (posting.Substring(index, matchedEvidence.Length), posting[start..index],
            posting[(index + matchedEvidence.Length)..end], index);
    }

    private static bool IsBoilerplate(string evidence) =>
        BoilerplateMarkers.Any(marker => evidence.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string TemplateKey(AnnotationItem item) => Hash(
        $"{Normalize(item.ContextBefore).ToLowerInvariant()}\n{Normalize(item.Evidence).ToLowerInvariant()}\n" +
        $"{Normalize(item.ContextAfter).ToLowerInvariant()}\n{string.Join(',', item.CandidateConceptIds)}\n" +
        $"{item.Machine.TaxonomyFingerprint}");
    private static string Normalize(string? value) => Whitespace.Replace(value ?? "", " ").Trim();
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
