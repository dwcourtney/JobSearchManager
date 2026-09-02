using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JobSearchManager;

public sealed record RegexValidationFixture(
    string Id, string Source, string Title, string Excerpt,
    string? ConceptId = null, bool? ExpectedPresent = null,
    string? Rationale = null, string? RequisitionId = null,
    string? Provenance = null, string? LabelSource = null,
    string? LabelScope = null,
    IReadOnlyList<string>? ExpectedPresentConceptIds = null);

internal sealed record RegexValidationDocument(
    int Version, IReadOnlyList<RegexValidationFixture> Fixtures,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? LabelScopes = null);

public sealed record RegexRuleEvaluationResult(
    string RuleId, int ValidationMatchCount, int TruePositiveMatches,
    int FalsePositiveMatches, double? Precision, int UniqueTruePositives,
    int RedundantTruePositives, IReadOnlyList<string> RepresentativeExamples,
    IReadOnlyList<string> FalsePositiveExamples);

public sealed record RegexConceptEvaluationResult(
    string ConceptId, int TruePositive, int FalsePositive, int FalseNegative, int TrueNegative,
    double? Precision, double? Recall, double? F1);

public sealed record RegexAggregateEvaluation(
    double? Precision, double? Recall, double? F1, int ConceptCount, int LabelCount);

public sealed record RegexEvaluationReport(
    string EvaluationRunId, DateTimeOffset EvaluatedUtc, string RulesetFingerprint,
    string ValidationCorpusFingerprint, string TaxonomyFingerprint,
    string ConfigurationFingerprint, int FixtureCount, int RuleCount,
    IReadOnlyList<RegexRuleEvaluationResult> Rules,
    IReadOnlyList<RegexConceptEvaluationResult> Concepts,
    RegexAggregateEvaluation Macro, RegexAggregateEvaluation Micro,
    RegexAggregateEvaluation HistoricalBenchmarkMacro,
    RegexAggregateEvaluation HistoricalBenchmarkMicro);

public sealed class RegexEvaluationService
{
    private static readonly IReadOnlySet<string> HistoricalBenchmarkConcepts =
        new HashSet<string>([
            "role.ai-ml-engineering", "role.software-engineering",
            "technical.software-development", "technical.backend-development",
            "technical.api-development", "technical.automation-scripting",
            "role.cloud-engineering", "technical.containers"
        ], StringComparer.Ordinal);
    private readonly RegexSemanticClassifier _classifier;
    private readonly SqliteSemanticRuleStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly RemoteWorkDetector _remote = new();
    private readonly ExtendedLocationRequirementDetector _extended = new();
    private readonly string _corpusPath;

    public RegexEvaluationService(IHostEnvironment environment, RegexSemanticClassifier classifier,
        SqliteSemanticRuleStore store, JobConceptCatalog catalog)
        : this(Path.Combine(environment.ContentRootPath, "RegexValidationCorpus.json"),
            classifier, store, catalog)
    {
    }

    internal RegexEvaluationService(string corpusPath, RegexSemanticClassifier classifier,
        SqliteSemanticRuleStore store, JobConceptCatalog catalog)
    {
        _classifier = classifier;
        _store = store;
        _catalog = catalog;
        _corpusPath = corpusPath;
    }

    public async Task<RegexEvaluationReport> EvaluateAsync(bool persist = true,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(_corpusPath, cancellationToken);
        var document = JsonSerializer.Deserialize<RegexValidationDocument>(bytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The RegEx validation corpus is invalid.");
        Validate(document);
        var labels = new List<Observation>();
        var ruleMatches = new Dictionary<string, RuleAccumulator>(StringComparer.Ordinal);
        foreach (var fixture in document.Fixtures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = $"<p>{fixture.Excerpt}</p>";
            var remote = _remote.Analyze(fixture.Title, "", [], html);
            var extended = _extended.Analyze(fixture.Title, "", [], html);
            var prediction = _classifier.Classify(fixture.Title, html, remote, extended,
                productionUsage: false);
            var predicted = prediction.Concepts.Select(item => item.ConceptId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var label in Expand(document, fixture))
            {
                var isPredicted = predicted.Contains(label.ConceptId);
                labels.Add(new(label.ConceptId, label.Expected, isPredicted));
                if (!prediction.MatchedRuleIds.TryGetValue(label.ConceptId, out var matchedIds))
                    continue;
                foreach (var ruleId in matchedIds)
                {
                    if (!ruleMatches.TryGetValue(ruleId, out var accumulator))
                        ruleMatches[ruleId] = accumulator = new();
                    accumulator.Matches++;
                    if (label.Expected)
                    {
                        accumulator.TruePositives++;
                        if (matchedIds.Count == 1) accumulator.UniqueTruePositives++;
                        else accumulator.RedundantTruePositives++;
                        if (accumulator.Examples.Count < 5) accumulator.Examples.Add(fixture.Id);
                    }
                    else
                    {
                        accumulator.FalsePositives++;
                        if (accumulator.FalsePositiveExamples.Count < 5)
                            accumulator.FalsePositiveExamples.Add(fixture.Id);
                    }
                }
            }
        }

        var currentRules = await _store.ListRulesAsync(cancellationToken: cancellationToken);
        var ruleResults = currentRules.Where(item => SemanticRuleStatuses.RunsInProduction(item.Status))
            .Select(rule =>
            {
                ruleMatches.TryGetValue(rule.RuleId, out var value);
                value ??= new();
                return new RegexRuleEvaluationResult(rule.RuleId, value.Matches,
                    value.TruePositives, value.FalsePositives,
                    Divide(value.TruePositives, value.TruePositives + value.FalsePositives),
                    value.UniqueTruePositives, value.RedundantTruePositives,
                    value.Examples, value.FalsePositiveExamples);
            }).OrderBy(item => item.RuleId, StringComparer.Ordinal).ToArray();
        var concepts = labels.GroupBy(item => item.ConceptId, StringComparer.Ordinal)
            .Select(group => Calculate(group.Key, group.ToArray()))
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal).ToArray();
        var evaluated = concepts.Where(item => item.TruePositive + item.FalseNegative > 0 &&
            item.FalsePositive + item.TrueNegative > 0).ToArray();
        var evaluatedLabels = evaluated.Sum(item => item.TruePositive + item.FalsePositive +
            item.FalseNegative + item.TrueNegative);
        var macro = new RegexAggregateEvaluation(Average(evaluated.Select(item => item.Precision)),
            Average(evaluated.Select(item => item.Recall)), Average(evaluated.Select(item => item.F1)),
            evaluated.Length, evaluatedLabels);
        var tp = evaluated.Sum(item => item.TruePositive);
        var fp = evaluated.Sum(item => item.FalsePositive);
        var fn = evaluated.Sum(item => item.FalseNegative);
        var microPrecision = Divide(tp, tp + fp);
        var microRecall = Divide(tp, tp + fn);
        var micro = new RegexAggregateEvaluation(microPrecision, microRecall,
            Harmonic(microPrecision, microRecall), evaluated.Length, evaluatedLabels);
        var historical = Aggregate(concepts.Where(item =>
            HistoricalBenchmarkConcepts.Contains(item.ConceptId)).ToArray());
        var normalized = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal));
        var report = new RegexEvaluationReport(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            _classifier.RulesetFingerprint,
            Convert.ToHexString(SHA256.HashData(normalized)).ToLowerInvariant(),
            _catalog.Fingerprint, ConfigurationFingerprint(_store.Policy), document.Fixtures.Count,
            ruleResults.Length, ruleResults, concepts, macro, micro,
            historical.Macro, historical.Micro);
        if (persist) await _store.SaveEvaluationAsync(report, cancellationToken);
        return report;
    }

    public async Task<SemanticRuleCandidateValidation> ValidateCandidateAsync(string ruleId,
        CancellationToken cancellationToken = default)
    {
        var rule = (await _store.ListRulesAsync(cancellationToken: cancellationToken))
            .SingleOrDefault(item => item.RuleId == ruleId)
            ?? throw new KeyNotFoundException("Semantic rule not found.");
        if (rule.Status != SemanticRuleStatuses.Proposed)
            throw new InvalidOperationException("Only a proposed rule can receive candidate validation.");

        var baseline = await EvaluateAsync(persist: false, cancellationToken);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(),
            "jsm-regex-candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "candidate.db");
            await _store.BackupAsync(databasePath, cancellationToken);
            RegexEvaluationReport candidate;
            using (var candidateStore = new SqliteSemanticRuleStore(databasePath, _catalog, _store.Policy))
            {
                await candidateStore.TransitionForEvaluationAsync(ruleId,
                    SemanticRuleStatuses.Validated, cancellationToken);
                await candidateStore.TransitionForEvaluationAsync(ruleId,
                    SemanticRuleStatuses.Active, cancellationToken);
                var candidateClassifier = new RegexSemanticClassifier(candidateStore, _catalog);
                await candidateClassifier.InitializeAsync(cancellationToken);
                var evaluator = new RegexEvaluationService(_corpusPath, candidateClassifier,
                    candidateStore, _catalog);
                candidate = await evaluator.EvaluateAsync(persist: false, cancellationToken);
            }
            var result = candidate.Rules.Single(item => item.RuleId == ruleId);
            var evidence = new SemanticRuleCandidateValidation(ruleId,
                SemanticRulesetFingerprint.RuleVersion(rule), DateTimeOffset.UtcNow, result,
                baseline.Macro, candidate.Macro, baseline.Micro, candidate.Micro,
                candidate.ValidationCorpusFingerprint, candidate.TaxonomyFingerprint);
            await _store.SaveCandidateValidationAsync(evidence, cancellationToken);
            return evidence;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private void Validate(RegexValidationDocument document)
    {
        if (document.Version < 1 || document.Fixtures.Count == 0)
            throw new InvalidDataException("The RegEx validation corpus is empty.");
        var scopes = document.LabelScopes ?? new Dictionary<string, IReadOnlyList<string>>();
        if (scopes.Values.SelectMany(item => item).Any(id => !_catalog.Contains(id)))
            throw new InvalidDataException("The RegEx validation corpus references an unknown concept.");
        foreach (var fixture in document.Fixtures)
        {
            var legacy = !string.IsNullOrWhiteSpace(fixture.ConceptId) && fixture.ExpectedPresent.HasValue;
            var scoped = !string.IsNullOrWhiteSpace(fixture.LabelScope) &&
                scopes.ContainsKey(fixture.LabelScope);
            if (string.IsNullOrWhiteSpace(fixture.Id) || legacy == scoped ||
                legacy && !_catalog.Contains(fixture.ConceptId))
                throw new InvalidDataException("The RegEx validation corpus contains an invalid fixture.");
        }
    }

    private static IEnumerable<(string ConceptId, bool Expected)> Expand(
        RegexValidationDocument document, RegexValidationFixture fixture)
    {
        if (fixture.ConceptId is not null && fixture.ExpectedPresent.HasValue)
        {
            yield return (fixture.ConceptId, fixture.ExpectedPresent.Value);
            yield break;
        }
        var present = (fixture.ExpectedPresentConceptIds ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var conceptId in document.LabelScopes![fixture.LabelScope!])
            yield return (conceptId, present.Contains(conceptId));
    }

    private static RegexConceptEvaluationResult Calculate(string conceptId,
        IReadOnlyList<Observation> values)
    {
        var tp = values.Count(item => item.Expected && item.Predicted);
        var fp = values.Count(item => !item.Expected && item.Predicted);
        var fn = values.Count(item => item.Expected && !item.Predicted);
        var tn = values.Count(item => !item.Expected && !item.Predicted);
        var precision = Divide(tp, tp + fp);
        var recall = Divide(tp, tp + fn);
        return new(conceptId, tp, fp, fn, tn, precision, recall, Harmonic(precision, recall));
    }

    private static (RegexAggregateEvaluation Macro, RegexAggregateEvaluation Micro) Aggregate(
        IReadOnlyList<RegexConceptEvaluationResult> concepts)
    {
        var labels = concepts.Sum(item => item.TruePositive + item.FalsePositive +
            item.FalseNegative + item.TrueNegative);
        var macro = new RegexAggregateEvaluation(Average(concepts.Select(item => item.Precision)),
            Average(concepts.Select(item => item.Recall)), Average(concepts.Select(item => item.F1)),
            concepts.Count, labels);
        var tp = concepts.Sum(item => item.TruePositive);
        var fp = concepts.Sum(item => item.FalsePositive);
        var fn = concepts.Sum(item => item.FalseNegative);
        var precision = Divide(tp, tp + fp);
        var recall = Divide(tp, tp + fn);
        return (macro, new RegexAggregateEvaluation(precision, recall,
            Harmonic(precision, recall), concepts.Count, labels));
    }

    private static string ConfigurationFingerprint(SemanticRulePolicy policy) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(policy,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
    private static double? Divide(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;
    private static double? Harmonic(double? precision, double? recall) =>
        precision is null || recall is null || precision + recall == 0
            ? precision == 0 && recall == 0 ? 0 : null
            : 2 * precision * recall / (precision + recall);
    private static double? Average(IEnumerable<double?> values)
    {
        var defined = values.Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        return defined.Length == 0 ? null : defined.Average();
    }

    private sealed record Observation(string ConceptId, bool Expected, bool Predicted);
    private sealed class RuleAccumulator
    {
        public int Matches;
        public int TruePositives;
        public int FalsePositives;
        public int UniqueTruePositives;
        public int RedundantTruePositives;
        public List<string> Examples { get; } = [];
        public List<string> FalsePositiveExamples { get; } = [];
    }
}
