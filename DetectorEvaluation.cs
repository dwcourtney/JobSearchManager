using System.Text.Json;

namespace JobSearchManager;

public sealed record DetectorEvaluationFixture(
    string Id,
    string Source,
    string Title,
    string Excerpt,
    string ConceptId,
    bool ExpectedPresent,
    string? Rationale = null);

internal sealed record DetectorEvaluationFixtureDocument(
    int Version,
    IReadOnlyList<DetectorEvaluationFixture> Fixtures);

public sealed record DetectorEvaluationError(
    string FixtureId,
    string Source,
    string Title,
    bool ExpectedPresent,
    bool PredictedPresent,
    string Evidence,
    string? Rationale);

public sealed record DetectorMetric(
    string ConceptId,
    string Concept,
    int PositiveSupport,
    int NegativeExamples,
    int TruePositive,
    int FalsePositive,
    int FalseNegative,
    int TrueNegative,
    double? Precision,
    double? Recall,
    double? F1,
    IReadOnlyList<DetectorEvaluationError> FalsePositives,
    IReadOnlyList<DetectorEvaluationError> FalseNegatives);

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

        DetectorEvaluationError Error(Observation item) => new(
            item.Fixture.Id,
            item.Fixture.Source,
            item.Fixture.Title,
            item.Fixture.ExpectedPresent,
            item.Prediction is not null,
            item.Prediction?.Evidence ?? item.Fixture.Excerpt,
            item.Fixture.Rationale);

        return new DetectorMetric(
            concept.Id,
            concept.DisplayName,
            truePositive + falseNegative,
            falsePositive + trueNegative,
            truePositive,
            falsePositive,
            falseNegative,
            trueNegative,
            precision,
            recall,
            HarmonicMean(precision, recall),
            observations.Where(item => !item.Fixture.ExpectedPresent && item.Prediction is not null)
                .Select(Error).ToArray(),
            observations.Where(item => item.Fixture.ExpectedPresent && item.Prediction is null)
                .Select(Error).ToArray());
    }

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
                string.IsNullOrWhiteSpace(fixture.Excerpt) || !catalog.Contains(fixture.ConceptId))
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

    internal sealed record Observation(
        DetectorEvaluationFixture Fixture,
        DetectedJobConcept? Prediction);
}
