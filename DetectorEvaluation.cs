using System.Text.Json;

namespace JobSearchManager;

public sealed record DetectorEvaluationFixture(
    string Id,
    string Source,
    string Title,
    string Excerpt,
    string ConceptId,
    bool ExpectedPresent,
    string? Rationale = null,
    string? RequisitionId = null,
    string? Provenance = null);

internal sealed record DetectorEvaluationFixtureDocument(
    int Version,
    IReadOnlyList<DetectorEvaluationFixture> Fixtures);

public sealed record DetectorEvaluationExample(
    string FixtureId,
    string Source,
    string Title,
    bool ExpectedPresent,
    bool PredictedPresent,
    string Result,
    string Evidence,
    string? Rationale,
    string? RequisitionId,
    string Provenance);

public sealed record DetectorMetric(
    string ConceptId,
    string Concept,
    int PositiveSupport,
    int NegativeExamples,
    int TotalExamples,
    string SampleSize,
    int TruePositive,
    int FalsePositive,
    int FalseNegative,
    int TrueNegative,
    double? Precision,
    double? Recall,
    double? F1,
    IReadOnlyList<DetectorEvaluationExample> Examples,
    IReadOnlyList<DetectorEvaluationExample> FalsePositives,
    IReadOnlyList<DetectorEvaluationExample> FalseNegatives);

public sealed record DetectorAggregateMetric(
    double? Precision,
    double? Recall,
    double? F1);

public sealed record DetectorEvaluationReport(
    int FixtureVersion,
    int FixtureCount,
    IReadOnlyList<DetectorMetric> Concepts,
    DetectorAggregateMetric Macro,
    DetectorAggregateMetric Micro,
    string? BuildSha = null);

public sealed class DetectorEvaluationService
{
    private readonly JobConceptCatalog _catalog;
    private readonly JobConceptDetector _detector;
    private readonly IReadOnlyList<DetectorEvaluationFixture> _fixtures;
    private readonly int _fixtureVersion;

    public DetectorEvaluationService(
        IHostEnvironment environment,
        JobConceptCatalog catalog,
        JobConceptDetector detector)
        : this(
            LoadDocument(Path.Combine(environment.ContentRootPath, "DetectorEvaluationFixtures.json")),
            catalog,
            detector)
    {
    }

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
    }

    public DetectorEvaluationReport Evaluate(string? buildSha = null)
    {
        var observations = _fixtures.Select(fixture =>
        {
            var predictions = _detector.Analyze(
                fixture.Title, "", [], $"<p>{fixture.Excerpt}</p>", null, null);
            var prediction = predictions.FirstOrDefault(item => item.ConceptId == fixture.ConceptId);
            return new Observation(fixture, prediction);
        }).ToArray();

        var metrics = observations
            .GroupBy(item => item.Fixture.ConceptId, StringComparer.Ordinal)
            .Select(group => CalculateConcept(_catalog.Get(group.Key), group.ToArray()))
            .OrderBy(item => item.Concept, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var macro = new DetectorAggregateMetric(
            AverageDefined(metrics.Select(item => item.Precision)),
            AverageDefined(metrics.Select(item => item.Recall)),
            AverageDefined(metrics.Select(item => item.F1)));
        var pooledTruePositive = metrics.Sum(item => item.TruePositive);
        var pooledFalsePositive = metrics.Sum(item => item.FalsePositive);
        var pooledFalseNegative = metrics.Sum(item => item.FalseNegative);
        var microPrecision = Divide(pooledTruePositive, pooledTruePositive + pooledFalsePositive);
        var microRecall = Divide(pooledTruePositive, pooledTruePositive + pooledFalseNegative);
        var micro = new DetectorAggregateMetric(
            microPrecision,
            microRecall,
            HarmonicMean(microPrecision, microRecall));

        return new DetectorEvaluationReport(
            _fixtureVersion, _fixtures.Count, metrics, macro, micro, NormalizeSha(buildSha));
    }

    internal static DetectorMetric CalculateConcept(
        JobConceptDefinition concept,
        IReadOnlyList<Observation> observations)
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
                ? predicted ? "TP" : "FN"
                : predicted ? "FP" : "TN";
            return new(
            item.Fixture.Id,
            item.Fixture.Source,
            item.Fixture.Title,
            item.Fixture.ExpectedPresent,
            predicted,
            result,
            item.Prediction?.Evidence ?? item.Fixture.Excerpt,
            item.Fixture.Rationale,
            item.Fixture.RequisitionId,
            ProvenanceFor(item.Fixture));
        }

        var examples = observations.Select(Example).OrderBy(item => item.FixtureId, StringComparer.Ordinal).ToArray();
        var positiveSupport = truePositive + falseNegative;
        var negativeExamples = falsePositive + trueNegative;

        return new DetectorMetric(
            concept.Id,
            concept.DisplayName,
            positiveSupport,
            negativeExamples,
            positiveSupport + negativeExamples,
            SampleSizeLabel(positiveSupport),
            truePositive,
            falsePositive,
            falseNegative,
            trueNegative,
            precision,
            recall,
            HarmonicMean(precision, recall),
            examples,
            examples.Where(item => item.Result == "FP").ToArray(),
            examples.Where(item => item.Result == "FN").ToArray());
    }

    internal static string SampleSizeLabel(int positiveSupport) => positiveSupport switch
    {
        < 1 => "No positive labels",
        < 5 => "Small sample",
        < 15 => "Developing sample",
        _ => "Established sample"
    };

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

    private static DetectorEvaluationFixtureDocument LoadDocument(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The detector evaluation fixture corpus is missing.", path);
        return JsonSerializer.Deserialize<DetectorEvaluationFixtureDocument>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The detector evaluation fixture corpus could not be read.");
    }

    private static void Validate(
        DetectorEvaluationFixtureDocument document,
        JobConceptCatalog catalog)
    {
        if (document.Version < 1 || document.Fixtures is null || document.Fixtures.Count == 0)
            throw new InvalidDataException("The detector evaluation fixture corpus is empty or invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixture in document.Fixtures)
        {
            if (string.IsNullOrWhiteSpace(fixture.Id) || !ids.Add(fixture.Id) ||
                string.IsNullOrWhiteSpace(fixture.Source) || string.IsNullOrWhiteSpace(fixture.Title) ||
                string.IsNullOrWhiteSpace(fixture.Excerpt) || !catalog.Contains(fixture.ConceptId) ||
                fixture.Provenance is not null and not ("synthetic" or "retained-corpus" or "public-posting"))
            {
                throw new InvalidDataException("A detector evaluation fixture has invalid or duplicate identity data.");
            }
        }
        foreach (var group in document.Fixtures.GroupBy(item => item.ConceptId, StringComparer.Ordinal))
        {
            if (!group.Any(item => item.ExpectedPresent) || !group.Any(item => !item.ExpectedPresent))
                throw new InvalidDataException($"Evaluation concept '{group.Key}' needs positive and negative labels.");
        }
    }

    private static string? NormalizeSha(string? value) =>
        value is not null && value.Length == 40 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f') ? value : null;

    private static string ProvenanceFor(DetectorEvaluationFixture fixture) =>
        fixture.Provenance ?? (fixture.Source.StartsWith("Synthetic", StringComparison.OrdinalIgnoreCase)
            ? "synthetic"
            : "public-posting");

    internal sealed record Observation(
        DetectorEvaluationFixture Fixture,
        DetectedJobConcept? Prediction);
}
