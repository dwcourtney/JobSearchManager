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
    public static bool IsValid(string? value) => value is Correct or Incorrect or DifferentLabel or MultipleLabels or None or Unsure;
    public static string? NormalizeImport(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "correct" => Correct, "incorrect" => Incorrect,
        "different-label" or "differentlabel" => DifferentLabel,
        "multiple-labels" or "multiplelabels" => MultipleLabels,
        "none" => None, "unsure" or "skip" or "unsure/skip" => Unsure, _ => null
    };
}

public static class AnnotationReviewerTypes
{
    public const string Qwen = "qwen-reviewed";
    public const string ChatGpt = "chatgpt-reviewed";
    public const string Codex = "codex-reviewed";
    public const string OtherMachine = "other-machine-reviewed";
    public static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "qwen" or Qwen => Qwen, "chatgpt" or "chat-gpt" or ChatGpt => ChatGpt,
        "codex" or Codex => Codex, "machine" or "other" or "other-machine" or OtherMachine => OtherMachine,
        _ => null
    };
}

public static class AnnotationExportModes
{
    public const string All = "all";
    public const string Reviewed = "reviewed";
    public const string Unreviewed = "unreviewed";
    public const string Unsure = "unsure";
    public const string TrainingEligible = "trainingEligible";
    public static bool IsValid(string? value) => value is All or Reviewed or Unreviewed or Unsure or TrainingEligible;
}

public sealed record AnnotationMachineProvenance(string Method, int TaxonomyVersion, string TaxonomyFingerprint,
    IReadOnlyList<string> BasisConceptIds, string? Model, decimal? Confidence);
public sealed record AnnotationMachineReview(string Id, string ReviewerType, string? ReviewerIdentity, string Decision,
    IReadOnlyList<string> SelectedConceptIds, decimal? Confidence, string? Rationale, DateTimeOffset? ReviewedUtc,
    DateTimeOffset ImportedUtc, string ContentHash, string TaxonomyFingerprint);
public sealed record AnnotationSource(string Id, string ContentHash, string JobId, string CompanyId, string Title,
    string SourceUrl, string FullPosting, DateTimeOffset CreatedUtc);
public sealed record AnnotationItem(string Id, string SourceId, string JobId, string CompanyId, string Title,
    string SourceUrl, string ContentHash, string Evidence, string ContextBefore, string ContextAfter,
    IReadOnlyList<string> CandidateConceptIds, AnnotationMachineProvenance Machine, string Status, string? Decision,
    IReadOnlyList<string> SelectedConceptIds, string? Reviewer, DateTimeOffset? ReviewedUtc, string? UnsureReason,
    bool TrainingEligible, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    IReadOnlyList<AnnotationMachineReview>? MachineReviews = null, string? HumanProvenance = null);
public sealed record AnnotationImportRejection(int Line, string? ItemId, string Category, string Message,
    string? ContentHash, string? TaxonomyFingerprint);
public sealed record AnnotationImportSummary(int RecordsRead, int Imported, int Unchanged, int Conflicts, int Rejected,
    int StaleFingerprint, int UnknownConcept, int Malformed, int UnknownItem, int ContentHashMismatch, int InvalidProvenance);
public sealed record AnnotationImportBatch(string Id, string FileName, DateTimeOffset ImportedUtc,
    AnnotationImportSummary Summary, IReadOnlyList<AnnotationImportRejection> Rejections);
public sealed record AnnotationCorpus(int SchemaVersion, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    Dictionary<string, AnnotationSource> Sources, Dictionary<string, AnnotationItem> Items,
    IReadOnlyList<AnnotationImportBatch>? ImportHistory = null)
{
    public static AnnotationCorpus Empty(DateTimeOffset now) =>
        new(2, now, now, new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
}
public sealed record AnnotationGenerateRequest(int? RequestedItems = 1000, bool AllEligible = false,
    string? Company = null, string? Concept = null);
public sealed record AnnotationDecisionRequest(string Decision, IReadOnlyList<string>? ConceptIds = null,
    string? UnsureReason = null);
public sealed record AnnotationQueueFilter(string Status = "unreviewed", string? Concept = null, string? Company = null);
public sealed record AnnotationStats(int Total, int Unreviewed, int Reviewed, int Unsure, int TrainingEligible,
    int Accepted, int Rejected, int Relabeled, int MachineLabeled, int MachineDisagreements, int HumanMachineConflicts);
public sealed record AnnotationQueueResponse(AnnotationItem? Item, AnnotationStats Stats,
    IReadOnlyList<JobConceptOption> Concepts, IReadOnlyList<string> Companies,
    IReadOnlyDictionary<string, int> ConceptDistribution, IReadOnlyDictionary<string, int> CompanyDistribution,
    string TaxonomyFingerprint, int TaxonomyVersion);
public sealed record AnnotationGenerationStatus(int Total, int EligibleUngenerated);
public sealed record AnnotationGenerateResult(int Added, int Total, int EligibleCandidates, int RemainingEligible,
    AnnotationQueueResponse Queue);
internal sealed record AnnotationMachineImportRecord(string? AnnotationItemId, string? ContentHash,
    string? TaxonomyFingerprint, string? Decision, IReadOnlyList<string>? SelectedConceptIds,
    string? ReviewerType, string? ReviewerIdentity, decimal? Confidence, string? Rationale, DateTimeOffset? ReviewedUtc);

public sealed class AnnotationLabelingService
{
    internal const string CorpusWorkspaceId = "9442dc573d422e5221312c5a3a9d739056583571492e318cb3963313f0124fd2";
    internal const int MaximumImportBytes = 25 * 1024 * 1024;
    internal const int MaximumImportRecords = 100_000;
    internal const int MaximumImportLineCharacters = 262_144;
    internal const int MaximumRequestedItems = 50_000;
    private const int RareConceptThreshold = 3;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly string[] BoilerplateMarkers = ["equal opportunity employer", "affirmative action employer",
        "reasonable accommodation", "benefits may include", "employment opportunity without regard"];
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly IWorkspaceDataStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AnnotationLabelingService(IWorkspaceDataStoreFactory stores, JobConceptCatalog catalog, TimeProvider time)
    {
        _store = stores.Create(CorpusWorkspaceId); _catalog = catalog; _time = time;
        TaxonomyFingerprint = Hash(JsonSerializer.Serialize(new { catalog.Version,
            concepts = catalog.Concepts.OrderBy(item => item.Id, StringComparer.Ordinal) }, WebJson));
    }
    public string TaxonomyFingerprint { get; }
    public int TaxonomyVersion => _catalog.Version;
    internal string StorageDescription => _store.Describe(WorkspaceDataFile.AnnotationCorpus);

    public Task<AnnotationGenerateResult> GenerateAsync(IEnumerable<JobRecord> jobs, int requestedItems,
        CancellationToken token = default) => GenerateAsync(jobs, new AnnotationGenerateRequest(requestedItems), token);

    public async Task<AnnotationGenerateResult> GenerateAsync(IEnumerable<JobRecord> jobs, AnnotationGenerateRequest request,
        CancellationToken token = default)
    {
        ValidateGenerateRequest(request);
        await _gate.WaitAsync(token);
        try
        {
            var now = _time.GetUtcNow(); var corpus = await LoadAsync(now, token);
            var candidates = BuildCandidates(corpus, jobs, request.Company, request.Concept, now);
            var wanted = request.AllEligible ? int.MaxValue : request.RequestedItems!.Value;
            var queues = candidates.GroupBy(c => c.Item.CandidateConceptIds[0], StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<(AnnotationSource Source, AnnotationItem Item)>(InterleaveCompanies(g))).ToList();
            var added = 0;
            while (added < wanted && queues.Count > 0)
                for (var i = queues.Count - 1; i >= 0 && added < wanted; i--)
                {
                    var candidate = queues[i].Dequeue(); corpus.Sources.TryAdd(candidate.Source.Id, candidate.Source);
                    corpus.Items.Add(candidate.Item.Id, candidate.Item); added++; if (queues[i].Count == 0) queues.RemoveAt(i);
                }
            if (added > 0) { corpus = corpus with { SchemaVersion = 2, UpdatedUtc = now }; await SaveAsync(corpus, token); }
            return new AnnotationGenerateResult(added, corpus.Items.Count, candidates.Count, candidates.Count - added,
                BuildQueue(corpus, new AnnotationQueueFilter()));
        }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationGenerationStatus> GetGenerationStatusAsync(IEnumerable<JobRecord> jobs,
        string? company = null, string? concept = null, CancellationToken token = default)
    {
        var request = new AnnotationGenerateRequest(1, false, company, concept); ValidateGenerateRequest(request);
        await _gate.WaitAsync(token);
        try
        {
            var now = _time.GetUtcNow(); var corpus = await LoadAsync(now, token);
            return new AnnotationGenerationStatus(corpus.Items.Count,
                BuildCandidates(corpus, jobs, company, concept, now).Count);
        }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationQueueResponse> GetQueueAsync(AnnotationQueueFilter filter, CancellationToken token = default)
    { await _gate.WaitAsync(token); try { return BuildQueue(await LoadAsync(_time.GetUtcNow(), token), filter); } finally { _gate.Release(); } }
    public async Task<AnnotationSource?> GetSourceAsync(string itemId, CancellationToken token = default)
    {
        await _gate.WaitAsync(token); try { var corpus = await LoadAsync(_time.GetUtcNow(), token);
            return corpus.Items.TryGetValue(itemId, out var item) && corpus.Sources.TryGetValue(item.SourceId, out var source) ? source : null;
        } finally { _gate.Release(); }
    }

    public async Task<AnnotationQueueResponse?> DecideAsync(string itemId, AnnotationDecisionRequest request, string reviewer,
        AnnotationQueueFilter filter, CancellationToken token = default)
    {
        var error = ValidateDecision(request); if (error is not null) throw new ArgumentException(error, nameof(request));
        await _gate.WaitAsync(token);
        try
        {
            var now = _time.GetUtcNow(); var corpus = await LoadAsync(now, token);
            if (!corpus.Items.TryGetValue(itemId, out var item)) return null;
            var selected = NormalizeConcepts(request.ConceptIds);
            if (selected.Any(id => !_catalog.Contains(id))) throw new ArgumentException("A selected concept is not in the canonical taxonomy.", nameof(request));
            if (request.Decision == AnnotationDecisions.DifferentLabel && selected.Any(item.CandidateConceptIds.Contains))
                throw new ArgumentException("Different label must replace the machine candidate.", nameof(request));
            var unsure = request.Decision == AnnotationDecisions.Unsure;
            corpus.Items[itemId] = item with { Status = unsure ? "unsure" : "reviewed", Decision = request.Decision,
                SelectedConceptIds = selected, Reviewer = reviewer, ReviewedUtc = now,
                UnsureReason = unsure ? request.UnsureReason?.Trim() : null, TrainingEligible = !unsure,
                HumanProvenance = unsure ? "unsure/excluded" :
                    (item.MachineReviews?.Count ?? 0) > 0 ? "human-overridden" : "human-reviewed", UpdatedUtc = now };
            corpus = corpus with { SchemaVersion = 2, UpdatedUtc = now }; await SaveAsync(corpus, token);
            return BuildQueue(corpus, filter);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ExportJsonLinesAsync(string mode = AnnotationExportModes.Reviewed,
        string? concept = null, string? company = null, CancellationToken token = default)
    {
        if (!AnnotationExportModes.IsValid(mode)) throw new ArgumentException("Choose a valid export mode.", nameof(mode));
        await _gate.WaitAsync(token);
        try
        {
            var corpus = await LoadAsync(_time.GetUtcNow(), token);
            var items = corpus.Items.Values.Where(item => mode switch { AnnotationExportModes.All => true,
                AnnotationExportModes.Reviewed => item.Status == "reviewed", AnnotationExportModes.Unreviewed => item.Status == "unreviewed",
                AnnotationExportModes.Unsure => item.Status == "unsure", AnnotationExportModes.TrainingEligible => item.TrainingEligible, _ => false });
            if (!string.IsNullOrWhiteSpace(concept)) items = items.Where(item => AllConceptIds(item).Contains(concept));
            if (!string.IsNullOrWhiteSpace(company)) items = items.Where(item => item.CompanyId == company);
            var lines = items.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item =>
            {
                corpus.Sources.TryGetValue(item.SourceId, out var source); var present = HumanPresent(item);
                var definitions = AllConceptIds(item).Where(_catalog.Contains).Select(id => { var c = _catalog.Get(id); return new
                    { c.Id, c.DisplayName, c.Category, definition = $"Canonical JSM {c.Category} concept: {c.DisplayName}.",
                      evidencePatterns = c.EvidencePatterns ?? [], titleEvidencePatterns = c.TitleEvidencePatterns ?? [], contextRules = c.ContextRules ?? [] }; }).ToArray();
                var catalog = _catalog.Concepts.Select(c => new { c.Id, c.DisplayName, c.Category,
                    definition = $"Canonical JSM {c.Category} concept: {c.DisplayName}." }).ToArray();
                return JsonSerializer.Serialize(new { schemaVersion = 2, annotationItemId = item.Id, item.SourceId,
                    sourceJobId = item.JobId, company = item.CompanyId, item.Title, item.SourceUrl, item.ContentHash,
                    taxonomyVersion = item.Machine.TaxonomyVersion, taxonomyFingerprint = item.Machine.TaxonomyFingerprint,
                    item.Evidence, surroundingContext = $"{item.ContextBefore}{item.Evidence}{item.ContextAfter}".Trim(),
                    item.ContextBefore, item.ContextAfter, fullPosting = source?.FullPosting,
                    suggestedConceptIds = item.CandidateConceptIds,
                    suggestedConcepts = item.CandidateConceptIds.Where(_catalog.Contains).Select(id => { var c = _catalog.Get(id); return new { c.Id, c.DisplayName, c.Category }; }),
                    canonicalConceptDefinitions = definitions, canonicalConceptCatalog = catalog, machineProposal = item.Machine,
                    currentReview = new { item.Status, humanDecision = item.Decision, item.SelectedConceptIds,
                        item.HumanProvenance, item.Reviewer, item.ReviewedUtc, item.UnsureReason, item.TrainingEligible,
                        machineReviews = item.MachineReviews ?? [], machineDisagreement = HasMachineDisagreement(item),
                        humanMachineConflict = HasHumanMachineConflict(item) }, confirmedPresentConceptIds = present,
                    confirmedAbsentCandidateConceptIds = HumanAbsent(item, present), item.CreatedUtc, item.UpdatedUtc }, WebJson);
            }).ToArray();
            return lines.Length == 0 ? "" : string.Join('\n', lines) + "\n";
        }
        finally { _gate.Release(); }
    }

    public async Task<AnnotationImportSummary> ImportMachineReviewsAsync(string jsonLines, string fileName,
        CancellationToken token = default)
    {
        if (Encoding.UTF8.GetByteCount(jsonLines) > MaximumImportBytes) throw new ArgumentException("The import exceeds the 25 MB limit.");
        await _gate.WaitAsync(token);
        try
        {
            var now = _time.GetUtcNow(); var corpus = await LoadAsync(now, token); var lines = jsonLines.Replace("\r\n", "\n").Split('\n');
            int read = 0, imported = 0, unchanged = 0, conflicts = 0, stale = 0, unknownConcept = 0,
                malformed = 0, unknownItem = 0, contentMismatch = 0, invalidProvenance = 0;
            var rejections = new List<AnnotationImportRejection>();
            for (var index = 0; index < lines.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var line = lines[index]; if (string.IsNullOrWhiteSpace(line)) continue;
                read++; if (read > MaximumImportRecords) throw new ArgumentException("The import exceeds the 100,000-record limit.");
                if (line.Length > MaximumImportLineCharacters) { malformed++; Reject(rejections, index + 1, null, "malformed", "Record exceeds 256 KiB."); continue; }
                AnnotationMachineImportRecord? record;
                try { record = JsonSerializer.Deserialize<AnnotationMachineImportRecord>(line, WebJson); }
                catch (JsonException) { malformed++; Reject(rejections, index + 1, null, "malformed", "Record is not valid JSON."); continue; }
                if (record is null || string.IsNullOrWhiteSpace(record.AnnotationItemId) || string.IsNullOrWhiteSpace(record.ContentHash) || string.IsNullOrWhiteSpace(record.TaxonomyFingerprint))
                { malformed++; Reject(rejections, index + 1, record?.AnnotationItemId, "malformed", "annotationItemId, contentHash, and taxonomyFingerprint are required.", record?.ContentHash, record?.TaxonomyFingerprint); continue; }
                if (!corpus.Items.TryGetValue(record.AnnotationItemId, out var item))
                { unknownItem++; Reject(rejections, index + 1, record.AnnotationItemId, "unknown-item", "Annotation item does not exist.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                if (record.ContentHash != item.ContentHash)
                { contentMismatch++; Reject(rejections, index + 1, record.AnnotationItemId, "content-hash-mismatch", "Source content hash does not match.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                if (record.TaxonomyFingerprint != TaxonomyFingerprint || record.TaxonomyFingerprint != item.Machine.TaxonomyFingerprint)
                { stale++; Reject(rejections, index + 1, record.AnnotationItemId, "stale-fingerprint", "Taxonomy fingerprint is stale.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                var type = AnnotationReviewerTypes.Normalize(record.ReviewerType);
                if (type is null) { invalidProvenance++; Reject(rejections, index + 1, record.AnnotationItemId, "invalid-provenance",
                    "Only machine reviewer provenance may be imported; imported human provenance is forbidden.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                var decision = AnnotationDecisions.NormalizeImport(record.Decision); var selected = NormalizeConcepts(record.SelectedConceptIds);
                var validation = decision is null ? "Choose a supported decision." : ValidateDecision(new(decision, selected));
                if (validation is not null || record.ReviewerIdentity?.Length > 200 || record.Rationale?.Length > 1000 || record.Confidence is < 0 or > 1)
                { malformed++; Reject(rejections, index + 1, record.AnnotationItemId, "malformed", validation ?? "Reviewer metadata is outside allowed bounds.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                var unknown = selected.FirstOrDefault(id => !_catalog.Contains(id));
                if (unknown is not null) { unknownConcept++; Reject(rejections, index + 1, record.AnnotationItemId, "unknown-concept", $"Canonical concept '{unknown}' is unknown.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                if (decision == AnnotationDecisions.DifferentLabel && selected.Any(item.CandidateConceptIds.Contains))
                { malformed++; Reject(rejections, index + 1, record.AnnotationItemId, "malformed", "Different label must replace the candidate.", record.ContentHash, record.TaxonomyFingerprint); continue; }
                var reviewId = "machine-review-" + Hash(string.Join('\n', item.Id, record.ContentHash, type,
                    record.ReviewerIdentity?.Trim() ?? "", decision, string.Join(',', selected), record.Confidence?.ToString() ?? "", record.Rationale?.Trim() ?? ""))[..24];
                var reviews = (item.MachineReviews ?? []).ToList();
                if (reviews.Any(review => review.Id == reviewId)) { unchanged++; continue; }
                var review = new AnnotationMachineReview(reviewId, type, record.ReviewerIdentity?.Trim(), decision!, selected,
                    record.Confidence, record.Rationale?.Trim(), record.ReviewedUtc, now, record.ContentHash, record.TaxonomyFingerprint);
                var conflict = reviews.Any(other => Signature(other.Decision, other.SelectedConceptIds) != Signature(review.Decision, review.SelectedConceptIds)) ||
                    HasHumanDecision(item) && Signature(item.Decision!, item.SelectedConceptIds) != Signature(review.Decision, review.SelectedConceptIds);
                reviews.Add(review); corpus.Items[item.Id] = item with { MachineReviews = reviews,
                    TrainingEligible = item.TrainingEligible && !conflict, UpdatedUtc = now }; imported++; if (conflict) conflicts++;
            }
            var summary = new AnnotationImportSummary(read, imported, unchanged, conflicts, rejections.Count, stale,
                unknownConcept, malformed, unknownItem, contentMismatch, invalidProvenance);
            var safeName = Path.GetFileName(fileName ?? "machine-review.jsonl"); if (safeName.Length > 160) safeName = safeName[..160];
            var batch = new AnnotationImportBatch("import-" + Hash($"{now:O}\n{safeName}\n{read}\n{imported}")[..24], safeName, now, summary, rejections.Take(1000).ToArray());
            corpus = corpus with { SchemaVersion = 2, UpdatedUtc = now,
                ImportHistory = (corpus.ImportHistory ?? []).Append(batch).TakeLast(100).ToArray() };
            await SaveAsync(corpus, token); return summary;
        }
        finally { _gate.Release(); }
    }

    internal static string? ValidateDecision(AnnotationDecisionRequest request)
    {
        if (!AnnotationDecisions.IsValid(request.Decision)) return "Choose a valid annotation decision.";
        var count = request.ConceptIds?.Distinct(StringComparer.Ordinal).Count() ?? 0;
        if (request.Decision == AnnotationDecisions.DifferentLabel && count != 1) return "Different label requires exactly one concept.";
        if (request.Decision == AnnotationDecisions.MultipleLabels && count < 2) return "Multiple labels requires at least two concepts.";
        if (request.Decision is AnnotationDecisions.Correct or AnnotationDecisions.Incorrect or AnnotationDecisions.None or AnnotationDecisions.Unsure && count != 0)
            return "This decision does not accept replacement concepts.";
        return null;
    }

    private void ValidateGenerateRequest(AnnotationGenerateRequest request)
    {
        if (!request.AllEligible && (request.RequestedItems is null or < 1 or > MaximumRequestedItems))
            throw new ArgumentException($"Items to add must be between 1 and {MaximumRequestedItems:N0}.");
        if (!string.IsNullOrWhiteSpace(request.Concept) && !_catalog.Contains(request.Concept)) throw new ArgumentException("Unknown generation concept.");
        if (request.Company?.Length > 200) throw new ArgumentException("Company filter is too long.");
    }

    private List<(AnnotationSource Source, AnnotationItem Item)> BuildCandidates(AnnotationCorpus corpus,
        IEnumerable<JobRecord> jobs, string? company, string? concept, DateTimeOffset now)
    {
        var templateKeys = corpus.Items.Values.Select(TemplateKey).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<(AnnotationSource Source, AnnotationItem Item)>();
        foreach (var job in jobs.Where(job => !string.IsNullOrWhiteSpace(job.DescriptionHtml) && job.DetectedConcepts is { Count: > 0 })
            .Where(job => string.IsNullOrWhiteSpace(company) || job.CompanyId == company)
            .OrderBy(job => job.CompanyId, StringComparer.Ordinal).ThenBy(job => job.StableId, StringComparer.Ordinal))
        {
            var posting = Normalize(JobAnalysis.HtmlToPlainText(job.DescriptionHtml)); if (posting.Length == 0) continue;
            var hash = Hash($"{Normalize(job.Title)}\n{posting}"); var sourceId = $"source-{hash[..24]}";
            var source = new AnnotationSource(sourceId, hash, job.StableId, job.CompanyId, job.Title, job.SourceUrl, posting, now);
            foreach (var detected in job.DetectedConcepts!.OrderBy(item => item.ConceptId, StringComparer.Ordinal))
            {
                if (!_catalog.Contains(detected.ConceptId)) continue;
                var span = LocateEvidence(job.Title, posting, detected.Evidence);
                if (span.Evidence.Length == 0 || IsBoilerplate(span.Evidence)) continue;
                var definition = _catalog.Get(detected.ConceptId);
                var adjacent = _catalog.Concepts.Where(c => c.Id != detected.ConceptId && c.Category == definition.Category)
                    .OrderBy(c => Hash($"{hash}\n{span.Start}\n{c.Id}"), StringComparer.Ordinal).Select(c => c.Id).FirstOrDefault();
                var proposals = new List<(string Id, string Method)> { (detected.ConceptId, "jsm-deterministic-concept-extraction") };
                if (adjacent is not null) proposals.Add((adjacent, "jsm-deterministic-adjacent-hard-negative-proposal"));
                foreach (var proposal in proposals)
                {
                    if (!string.IsNullOrWhiteSpace(concept) && proposal.Id != concept && detected.ConceptId != concept) continue;
                    var itemId = "annotation-" + Hash($"{hash}\n{span.Start}\n{span.Evidence}\n{proposal.Id}\n{TaxonomyFingerprint}")[..24];
                    var item = new AnnotationItem(itemId, sourceId, job.StableId, job.CompanyId, job.Title, job.SourceUrl,
                        hash, span.Evidence, span.Before, span.After, [proposal.Id],
                        new AnnotationMachineProvenance(proposal.Method, _catalog.Version, TaxonomyFingerprint,
                            [detected.ConceptId], null, null), "unreviewed", null, [], null, null, null, false, now, now, [], null);
                    var key = TemplateKey(item);
                    if (!corpus.Items.ContainsKey(itemId) && templateKeys.Add(key)) candidates.Add((source, item));
                }
            }
        }
        return candidates;
    }

    private AnnotationQueueResponse BuildQueue(AnnotationCorpus corpus, AnnotationQueueFilter filter)
    {
        var status = string.IsNullOrWhiteSpace(filter.Status) ? "unreviewed" : filter.Status.Trim();
        var counts = corpus.Items.Values.SelectMany(AllConceptIds).GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var items = corpus.Items.Values.Where(item => status switch
        {
            "all" => true, "trainingEligible" => item.TrainingEligible,
            "excluded" => item.Status == "unsure" || item.Decision == AnnotationDecisions.None ||
                (item.MachineReviews ?? []).Any(review => review.Decision == AnnotationDecisions.Unsure) ||
                HasMachineDisagreement(item) || HasHumanMachineConflict(item),
            "unsure" => item.Status == "unsure" ||
                (item.MachineReviews ?? []).Any(review => review.Decision == AnnotationDecisions.Unsure),
            "machineDisagreement" => HasMachineDisagreement(item),
            "humanUnreviewedMachine" => !HasHumanDecision(item) && (item.MachineReviews?.Count ?? 0) > 0,
            "rareConcept" => AllConceptIds(item).Any(id => counts.GetValueOrDefault(id) <= RareConceptThreshold),
            "relabeledConflicting" => item.Decision is AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels || HasMachineDisagreement(item) || HasHumanMachineConflict(item),
            "humanReviewed" or "reviewed" => item.Status == "reviewed", _ => item.Status == status
        });
        if (!string.IsNullOrWhiteSpace(filter.Concept)) items = items.Where(item => AllConceptIds(item).Contains(filter.Concept));
        if (!string.IsNullOrWhiteSpace(filter.Company)) items = items.Where(item => item.CompanyId == filter.Company);
        var next = items.OrderByDescending(HasMachineDisagreement).ThenByDescending(item => (item.MachineReviews?.Count ?? 0) > 0)
            .ThenByDescending(item => AllConceptIds(item).Any(id => counts.GetValueOrDefault(id) <= RareConceptThreshold))
            .ThenBy(item => item.UpdatedUtc).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        return new(next, Stats(corpus), _catalog.Options,
            corpus.Items.Values.Select(item => item.CompanyId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), counts,
            corpus.Items.Values.GroupBy(item => item.CompanyId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            TaxonomyFingerprint, TaxonomyVersion);
    }

    private static AnnotationStats Stats(AnnotationCorpus corpus) => new(corpus.Items.Count,
        corpus.Items.Values.Count(i => i.Status == "unreviewed"), corpus.Items.Values.Count(i => i.Status == "reviewed"),
        corpus.Items.Values.Count(i => i.Status == "unsure"), corpus.Items.Values.Count(i => i.TrainingEligible),
        corpus.Items.Values.Count(i => i.Decision == AnnotationDecisions.Correct),
        corpus.Items.Values.Count(i => i.Decision is AnnotationDecisions.Incorrect or AnnotationDecisions.None),
        corpus.Items.Values.Count(i => i.Decision is AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels),
        corpus.Items.Values.Count(i => (i.MachineReviews?.Count ?? 0) > 0), corpus.Items.Values.Count(HasMachineDisagreement),
        corpus.Items.Values.Count(HasHumanMachineConflict));

    private static IEnumerable<(AnnotationSource Source, AnnotationItem Item)> InterleaveCompanies(IEnumerable<(AnnotationSource Source, AnnotationItem Item)> candidates)
    {
        var queues = candidates.GroupBy(c => c.Item.CompanyId, StringComparer.Ordinal).OrderBy(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Queue<(AnnotationSource, AnnotationItem)>(g)).ToList();
        while (queues.Count > 0) for (var i = queues.Count - 1; i >= 0; i--)
        { yield return queues[i].Dequeue(); if (queues[i].Count == 0) queues.RemoveAt(i); }
    }
    private async Task<AnnotationCorpus> LoadAsync(DateTimeOffset now, CancellationToken token)
    {
        var corpus = await _store.ReadJsonAsync<AnnotationCorpus>(WorkspaceDataFile.AnnotationCorpus, token) ?? AnnotationCorpus.Empty(now);
        foreach (var pair in corpus.Items.ToArray()) corpus.Items[pair.Key] = pair.Value with
        { MachineReviews = pair.Value.MachineReviews ?? [], HumanProvenance = pair.Value.HumanProvenance ??
            (pair.Value.Status == "reviewed" ? "human-reviewed" : pair.Value.Status == "unsure" ? "unsure/excluded" : null) };
        return corpus with { ImportHistory = corpus.ImportHistory ?? [] };
    }
    private Task SaveAsync(AnnotationCorpus corpus, CancellationToken token) => _store.WriteJsonAsync(WorkspaceDataFile.AnnotationCorpus, corpus, token);
    private static bool HasHumanDecision(AnnotationItem item) => item.Status is "reviewed" or "unsure" && AnnotationDecisions.IsValid(item.Decision);
    private static bool HasMachineDisagreement(AnnotationItem item)
    {
        var reviews = item.ReviewedUtc is null ? item.MachineReviews ?? [] :
            (item.MachineReviews ?? []).Where(review => review.ImportedUtc > item.ReviewedUtc.Value).ToArray();
        return reviews.Select(r => Signature(r.Decision, r.SelectedConceptIds)).Distinct().Skip(1).Any();
    }
    private static bool HasHumanMachineConflict(AnnotationItem item) => HasHumanDecision(item) &&
        (item.ReviewedUtc is null || (item.MachineReviews ?? []).Any(review =>
            review.ImportedUtc > item.ReviewedUtc.Value &&
            Signature(review.Decision, review.SelectedConceptIds) != Signature(item.Decision!, item.SelectedConceptIds)));
    private static string Signature(string decision, IReadOnlyList<string> selected) => $"{decision}:{string.Join(',', selected.Order(StringComparer.Ordinal))}";
    private static IReadOnlyList<string> HumanPresent(AnnotationItem item) => item.Decision switch { AnnotationDecisions.Correct => item.CandidateConceptIds,
        AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels => item.SelectedConceptIds, _ => [] };
    private static IReadOnlyList<string> HumanAbsent(AnnotationItem item, IReadOnlyList<string> present) => (item.Decision switch
    { AnnotationDecisions.Incorrect or AnnotationDecisions.DifferentLabel or AnnotationDecisions.MultipleLabels or AnnotationDecisions.None => item.CandidateConceptIds, _ => [] }).Except(present).ToArray();
    private static IReadOnlyList<string> AllConceptIds(AnnotationItem item) => item.CandidateConceptIds.Concat(item.Machine.BasisConceptIds)
        .Concat(item.SelectedConceptIds).Concat((item.MachineReviews ?? []).SelectMany(r => r.SelectedConceptIds)).Distinct().Order(StringComparer.Ordinal).ToArray();
    private static string[] NormalizeConcepts(IReadOnlyList<string>? values) => (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().Order(StringComparer.Ordinal).ToArray();
    private static void Reject(List<AnnotationImportRejection> list, int line, string? id, string category, string message, string? hash = null, string? fingerprint = null) => list.Add(new(line, id, category, message, hash, fingerprint));
    private static (string Evidence, string Before, string After, int Start) LocateEvidence(string title, string posting, string detectorEvidence)
    {
        var evidence = Normalize(detectorEvidence); var alternatives = evidence.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Prepend(evidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var match = alternatives.FirstOrDefault(c => posting.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? "";
        var index = match.Length == 0 ? -1 : posting.IndexOf(match, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && !string.IsNullOrWhiteSpace(title)) { match = alternatives.FirstOrDefault(c => title.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? "";
            var titleIndex = match.Length == 0 ? -1 : title.IndexOf(match, StringComparison.OrdinalIgnoreCase);
            if (titleIndex >= 0) return (title.Substring(titleIndex, match.Length), "", posting[..Math.Min(360, posting.Length)], -1); }
        if (index < 0) return ("", "", "", -1); var start = Math.Max(0, index - 220); var end = Math.Min(posting.Length, index + match.Length + 220);
        return (posting.Substring(index, match.Length), posting[start..index], posting[(index + match.Length)..end], index);
    }
    private static bool IsBoilerplate(string evidence) => BoilerplateMarkers.Any(m => evidence.Contains(m, StringComparison.OrdinalIgnoreCase));
    private static string TemplateKey(AnnotationItem item) => Hash($"{Normalize(item.ContextBefore).ToLowerInvariant()}\n{Normalize(item.Evidence).ToLowerInvariant()}\n{Normalize(item.ContextAfter).ToLowerInvariant()}\n{string.Join(',', item.CandidateConceptIds)}\n{item.Machine.TaxonomyFingerprint}");
    private static string Normalize(string? value) => Whitespace.Replace(value ?? "", " ").Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
