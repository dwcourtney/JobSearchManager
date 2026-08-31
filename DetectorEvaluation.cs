using System.Text.Json;

namespace JobSearchManager;

public sealed record DetectorEvaluationFixture(
    string Id, string Source, string Title, string Excerpt,
    string? ConceptId = null, bool? ExpectedPresent = null,
    string? Rationale = null, string? RequisitionId = null,
    string? Provenance = null, string? LabelSource = null,
    string? LabelScope = null,
    IReadOnlyList<string>? ExpectedPresentConceptIds = null);

internal sealed record DetectorEvaluationFixtureDocument(
    int Version,
    IReadOnlyList<DetectorEvaluationFixture> Fixtures,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? LabelScopes = null);

public sealed record DetectorEvaluationExample(
    string FixtureId, string Source, string Title,
    bool ExpectedPresent, bool PredictedPresent, string Result,
    string Excerpt, string Evidence, string? Rationale,
    string? RequisitionId, string Provenance, string LabelSource);

public sealed record DetectorMetric(
    string ConceptId, string Concept, string Category, string Tier,
    string EvaluationClass, string EvaluationRationale, bool Evaluated,
    int PositiveSupport, int NegativeExamples, int TotalExamples, string SampleSize,
    int TruePositive, int FalsePositive, int FalseNegative, int TrueNegative,
    double? Precision, double? Recall, double? F1,
    IReadOnlyList<string> ErrorFixtureIds,
    IReadOnlyList<DetectorEvaluationExample> Examples,
    IReadOnlyList<DetectorEvaluationExample> FalsePositives,
    IReadOnlyList<DetectorEvaluationExample> FalseNegatives);

public sealed record DetectorAggregateMetric(
    double? Precision, double? Recall, double? F1,
    int ConceptCount = 0, int LabelCount = 0);

public sealed record DetectorTierAggregate(
    string Tier, DetectorAggregateMetric Macro, DetectorAggregateMetric Micro);

public sealed record DetectorEvaluationExclusion(string Name, string Rationale);

public sealed record DetectorEvaluationReport(
    int FixtureVersion, int FixtureCount, int LabelCount, int CanonicalConceptCount,
    int EvaluatableCount, int PartiallyEvaluatableCount, int ExcludedCount,
    IReadOnlyList<DetectorMetric> Concepts,
    DetectorAggregateMetric Macro, DetectorAggregateMetric Micro,
    IReadOnlyList<DetectorTierAggregate> TierAggregates,
    IReadOnlyList<DetectorEvaluationExclusion> Exclusions,
    string? BuildSha = null);

public sealed class DetectorEvaluationService
{
    public const string Tier1 = "Tier 1 — Target Technical";
    public const string Tier2 = "Tier 2 — Strong Negative";
    public const string Tier3 = "Tier 3 — Other";

    private static readonly HashSet<string> Tier1Concepts = new([
        "role.ai-ml-engineering", "role.cloud-engineering", "role.data-engineering",
        "role.data-science", "role.devops-platform", "role.software-engineering",
        "role.systems-engineering", "role.infrastructure-engineering",
        "role.network-engineering", "role.cybersecurity", "role.test-validation-engineering",
        "technical.artificial-intelligence", "technical.machine-learning",
        "technical.large-language-models", "technical.nlp", "technical.software-development",
        "technical.api-development", "technical.application-development",
        "technical.backend-development", "technical.frontend-development",
        "technical.automation-scripting", "technical.cicd", "technical.cloud",
        "technical.infrastructure-as-code", "technical.containers", "technical.linux",
        "technical.linux-administration", "technical.windows-administration",
        "technical.virtualization", "technical.networking", "technical.cisco-networking",
        "technical.embedded-systems", "technical.storage"
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> Tier2Concepts = new([
        "role.program-management", "role.project-management", "role.people-management",
        "role.field-service", "role.management-heavy", "work.field-engineering",
        "responsibility.personnel-management", "responsibility.team-leadership",
        "responsibility.budget-ownership", "responsibility.schedule-ownership",
        "responsibility.documentation-heavy", "responsibility.proposal-capture",
        "responsibility.customer-facing", "work.data-center", "work.physical-infrastructure",
        "work.manufacturing-floor", "work.outdoor-field", "work.classified-facility",
        "work.heights", "work.confined-spaces", "work.shipboard", "work.scuba",
        "work.deployment", "work.extended-away-assignment", "work.international-assignment",
        "work.rotation", "work.relocation"
    ], StringComparer.Ordinal);

    private static readonly HashSet<string> PartialConcepts = new([
        "role.individual-contributor", "role.management-heavy",
        "responsibility.hands-on-implementation", "responsibility.architecture-heavy",
        "responsibility.customer-facing", "responsibility.team-leadership",
        "responsibility.personnel-management", "responsibility.documentation-heavy",
        "responsibility.operations-sustainment", "responsibility.research-oriented",
        "work.customer-site", "work.field-engineering"
    ], StringComparer.Ordinal);

    private static readonly DetectorEvaluationExclusion[] Exclusions = [
        new("Travel Tolerance preference", "A nullable user preference compared with detected posting travel signals; it is not a binary posting detector."),
        new("Normal Work Location preference", "A nullable user ideal compared with detected remote/onsite signals; it is not a binary posting detector."),
        new("Group Hard Conflict overrides", "User-authored scoring overrides do not represent posting-text ground truth."),
        new("Configured / Not Set preference state", "Preference completeness is user state, not a production posting-text prediction.")
    ];

    private readonly JobConceptCatalog _catalog;
    private readonly JobConceptDetector _detector;
    private readonly RemoteWorkDetector _remoteWorkDetector = new();
    private readonly ExtendedLocationRequirementDetector _extendedLocationDetector = new();
    private readonly IReadOnlyList<DetectorEvaluationFixture> _fixtures;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _labelScopes;
    private readonly int _fixtureVersion;

    public DetectorEvaluationService(
        IHostEnvironment environment, JobConceptCatalog catalog, JobConceptDetector detector)
        : this(LoadDocument(Path.Combine(environment.ContentRootPath,
            "DetectorEvaluationFixtures.json")), catalog, detector) { }

    internal DetectorEvaluationService(
        DetectorEvaluationFixtureDocument document,
        JobConceptCatalog catalog,
        JobConceptDetector detector)
    {
        _catalog = catalog;
        _detector = detector;
        Validate(document, catalog);
        _fixtureVersion = document.Version;
        _fixtures = document.Fixtures.ToArray();
        _labelScopes = document.LabelScopes ?? new Dictionary<string, IReadOnlyList<string>>();
    }

    public DetectorEvaluationReport Evaluate(string? buildSha = null)
    {
        var observations = new List<Observation>();
        foreach (var fixture in _fixtures)
        {
            var descriptionHtml = $"<p>{fixture.Excerpt}</p>";
            var remoteWork = _remoteWorkDetector.Analyze(
                fixture.Title, "", [], descriptionHtml);
            var extendedLocation = _extendedLocationDetector.Analyze(
                fixture.Title, "", [], descriptionHtml);
            var predictions = _detector.Analyze(
                    fixture.Title, "", [], descriptionHtml, remoteWork, extendedLocation)
                .ToDictionary(item => item.ConceptId, StringComparer.Ordinal);
            foreach (var label in Expand(fixture))
            {
                predictions.TryGetValue(label.ConceptId, out var prediction);
                observations.Add(new Observation(label, prediction));
            }
        }

        var byConcept = observations
            .GroupBy(item => item.Fixture.ConceptId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<Observation>)group.ToArray(), StringComparer.Ordinal);
        var metrics = _catalog.Concepts
            .Select(concept => CalculateConcept(concept,
                byConcept.TryGetValue(concept.Id, out var items) ? items : []))
            .OrderBy(item => TierOrder(item.Tier))
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Concept, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evaluated = metrics.Where(item => item.Evaluated).ToArray();
        var overall = CalculateAggregate(evaluated);
        var tiers = new[] { Tier1, Tier2, Tier3 }.Select(tier =>
        {
            var aggregate = CalculateAggregate(evaluated.Where(item => item.Tier == tier).ToArray());
            return new DetectorTierAggregate(tier, aggregate.Macro, aggregate.Micro);
        }).ToArray();

        return new DetectorEvaluationReport(
            _fixtureVersion, _fixtures.Count, observations.Count, _catalog.Concepts.Count,
            metrics.Count(item => item.EvaluationClass == "Evaluatable"),
            metrics.Count(item => item.EvaluationClass == "Partially evaluatable"),
            Exclusions.Length, metrics, overall.Macro, overall.Micro, tiers, Exclusions,
            NormalizeSha(buildSha));
    }

    internal IReadOnlyList<ZeroShotEvaluationCase> BuildZeroShotCases()
    {
        var selected = new HashSet<string>(ZeroShotEvaluationService.Concepts.Select(item => item.ConceptId),
            StringComparer.Ordinal);
        return _fixtures.Select(fixture => new ZeroShotEvaluationCase(
                fixture.Id, fixture.Title, fixture.Excerpt,
                Expand(fixture).Where(label => selected.Contains(label.ConceptId))
                    .ToDictionary(label => label.ConceptId, label => label.ExpectedPresent,
                        StringComparer.Ordinal)))
            .Where(item => item.Labels.Count == selected.Count)
            .OrderBy(item => item.FixtureId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static DetectorMetric CalculateConcept(
        JobConceptDefinition concept, IReadOnlyList<Observation> observations)
    {
        var truePositive = observations.Count(item => item.Fixture.ExpectedPresent && item.Prediction is not null);
        var falsePositive = observations.Count(item => !item.Fixture.ExpectedPresent && item.Prediction is not null);
        var falseNegative = observations.Count(item => item.Fixture.ExpectedPresent && item.Prediction is null);
        var trueNegative = observations.Count(item => !item.Fixture.ExpectedPresent && item.Prediction is null);
        var precision = Divide(truePositive, truePositive + falsePositive);
        var recall = Divide(truePositive, truePositive + falseNegative);

        DetectorEvaluationExample Example(Observation item)
        {
            var predicted = item.Prediction is not null;
            var result = item.Fixture.ExpectedPresent
                ? predicted ? "TP" : "FN" : predicted ? "FP" : "TN";
            return new(item.Fixture.Id, item.Fixture.Source, item.Fixture.Title,
                item.Fixture.ExpectedPresent, predicted, result, item.Fixture.Excerpt,
                item.Prediction?.Evidence ?? "No detector evidence.", item.Fixture.Rationale,
                item.Fixture.RequisitionId, item.Fixture.Provenance, item.Fixture.LabelSource);
        }

        var examples = observations.Select(Example)
            .OrderBy(item => item.FixtureId, StringComparer.Ordinal).ToArray();
        var positiveSupport = truePositive + falseNegative;
        var negativeExamples = falsePositive + trueNegative;
        var profile = ProfileFor(concept.Id);
        var evaluated = positiveSupport > 0 && negativeExamples > 0;
        var errorFixtureIds = examples.Where(item => item.Result is "FP" or "FN")
            .Select(item => item.FixtureId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return new DetectorMetric(concept.Id, concept.DisplayName, concept.Category,
            profile.Tier, profile.Classification, profile.Rationale, evaluated,
            positiveSupport, negativeExamples, positiveSupport + negativeExamples,
            SampleSizeLabel(positiveSupport, negativeExamples), truePositive, falsePositive,
            falseNegative, trueNegative, evaluated ? precision : null,
            evaluated ? recall : null, evaluated ? HarmonicMean(precision, recall) : null,
            errorFixtureIds,
            examples, examples.Where(item => item.Result == "FP").ToArray(),
            examples.Where(item => item.Result == "FN").ToArray());
    }

    internal static string SampleSizeLabel(int positiveSupport, int negativeSupport) =>
        positiveSupport == 0 || negativeSupport == 0 ? "Not evaluated" :
        Math.Min(positiveSupport, negativeSupport) < 5 ? "Small sample" :
        Math.Min(positiveSupport, negativeSupport) < 15 ? "Developing sample" :
        "Established sample";

    internal static double? Divide(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;

    internal static double? HarmonicMean(double? precision, double? recall) =>
        precision is null || recall is null || precision + recall == 0
            ? precision == 0 && recall == 0 ? 0 : null
            : 2 * precision * recall / (precision + recall);

    internal static double? AverageDefined(IEnumerable<double?> values)
    {
        var defined = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return defined.Length == 0 ? null : defined.Average();
    }

    internal static string TierFor(string conceptId) => Tier1Concepts.Contains(conceptId)
        ? Tier1 : Tier2Concepts.Contains(conceptId) ? Tier2 : Tier3;

    internal static string ClassificationFor(string conceptId) =>
        PartialConcepts.Contains(conceptId) ? "Partially evaluatable" : "Evaluatable";

    private IEnumerable<LabeledFixture> Expand(DetectorEvaluationFixture fixture)
    {
        if (!string.IsNullOrWhiteSpace(fixture.ConceptId) && fixture.ExpectedPresent.HasValue)
        {
            yield return LabeledFixtureFor(fixture, fixture.ConceptId, fixture.ExpectedPresent.Value);
            yield break;
        }
        var scope = _labelScopes[fixture.LabelScope!];
        var present = new HashSet<string>(fixture.ExpectedPresentConceptIds ?? [], StringComparer.Ordinal);
        foreach (var conceptId in scope)
            yield return LabeledFixtureFor(fixture, conceptId, present.Contains(conceptId));
    }

    private static LabeledFixture LabeledFixtureFor(
        DetectorEvaluationFixture fixture, string conceptId, bool expectedPresent) => new(
            fixture.Id, fixture.Source, fixture.Title, fixture.Excerpt, conceptId,
            expectedPresent, fixture.Rationale, fixture.RequisitionId, ProvenanceFor(fixture),
            fixture.LabelSource ?? "Codex-reviewed");

    private static (DetectorAggregateMetric Macro, DetectorAggregateMetric Micro) CalculateAggregate(
        IReadOnlyList<DetectorMetric> metrics)
    {
        var labels = metrics.Sum(item => item.TotalExamples);
        var macro = new DetectorAggregateMetric(
            AverageDefined(metrics.Select(item => item.Precision)),
            AverageDefined(metrics.Select(item => item.Recall)),
            AverageDefined(metrics.Select(item => item.F1)), metrics.Count, labels);
        var truePositive = metrics.Sum(item => item.TruePositive);
        var falsePositive = metrics.Sum(item => item.FalsePositive);
        var falseNegative = metrics.Sum(item => item.FalseNegative);
        var precision = Divide(truePositive, truePositive + falsePositive);
        var recall = Divide(truePositive, truePositive + falseNegative);
        var micro = new DetectorAggregateMetric(precision, recall,
            HarmonicMean(precision, recall), metrics.Count, labels);
        return (macro, micro);
    }

    private static int TierOrder(string tier) => tier == Tier1 ? 1 : tier == Tier2 ? 2 : 3;

    private static (string Tier, string Classification, string Rationale) ProfileFor(string conceptId)
    {
        var classification = ClassificationFor(conceptId);
        return (TierFor(conceptId), classification,
            classification == "Partially evaluatable"
                ? "Posting text can support a reviewed label, but role context and degree require interpretation."
                : "Posting text provides a reasonably objective Present/Absent classification.");
    }

    private static DetectorEvaluationFixtureDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The detector evaluation fixture corpus is missing.", path);
        return JsonSerializer.Deserialize<DetectorEvaluationFixtureDocument>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The detector evaluation fixture corpus could not be read.");
    }

    private static void Validate(DetectorEvaluationFixtureDocument document, JobConceptCatalog catalog)
    {
        if (document.Version < 1 || document.Fixtures is null || document.Fixtures.Count == 0)
            throw new InvalidDataException("The detector evaluation fixture corpus is empty or invalid.");
        var scopes = document.LabelScopes ?? new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (scopeName, conceptIds) in scopes)
            if (string.IsNullOrWhiteSpace(scopeName) || conceptIds.Count == 0 ||
                conceptIds.Distinct(StringComparer.Ordinal).Count() != conceptIds.Count ||
                conceptIds.Any(conceptId => !catalog.Contains(conceptId)))
                throw new InvalidDataException($"Evaluation label scope '{scopeName}' is invalid.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixture in document.Fixtures)
        {
            var legacy = !string.IsNullOrWhiteSpace(fixture.ConceptId) && fixture.ExpectedPresent.HasValue;
            IReadOnlyList<string>? scope = null;
            var scoped = !string.IsNullOrWhiteSpace(fixture.LabelScope) &&
                scopes.TryGetValue(fixture.LabelScope, out scope);
            var present = fixture.ExpectedPresentConceptIds ?? [];
            if (string.IsNullOrWhiteSpace(fixture.Id) || !ids.Add(fixture.Id) ||
                string.IsNullOrWhiteSpace(fixture.Source) || string.IsNullOrWhiteSpace(fixture.Title) ||
                string.IsNullOrWhiteSpace(fixture.Excerpt) || legacy == scoped ||
                legacy && !catalog.Contains(fixture.ConceptId!) ||
                scoped && (present.Distinct(StringComparer.Ordinal).Count() != present.Count ||
                    present.Any(conceptId => !scope!.Contains(conceptId, StringComparer.Ordinal))) ||
                fixture.Provenance is not null and not ("synthetic" or "retained-corpus" or
                    "public-posting" or "synthetic-positive" or "synthetic-hard-negative"))
                throw new InvalidDataException(
                    "A detector evaluation fixture has invalid or duplicate identity or label data.");
        }
    }

    private static string? NormalizeSha(string? value) =>
        value is not null && value.Length == 40 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f') ? value : null;

    private static string ProvenanceFor(DetectorEvaluationFixture fixture) =>
        fixture.Provenance ?? (fixture.Source.StartsWith("Synthetic", StringComparison.OrdinalIgnoreCase)
            ? "synthetic" : "public-posting");

    internal sealed record LabeledFixture(
        string Id, string Source, string Title, string Excerpt, string ConceptId,
        bool ExpectedPresent, string? Rationale, string? RequisitionId,
        string Provenance, string LabelSource);

    internal sealed record Observation(LabeledFixture Fixture, DetectedJobConcept? Prediction);
}
