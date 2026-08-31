namespace JobSearchManager;

public sealed record EmbeddingConcept(string ConceptId, string Description);
public sealed record EmbeddingEvaluationCase(
    string FixtureId, string Title, string Description,
    IReadOnlyDictionary<string, bool> Labels);
public sealed record EmbeddingConceptMetric(
    string ConceptId, double Threshold, int TruePositive, int FalsePositive,
    int FalseNegative, int TrueNegative, double? Precision, double? Recall, double? F1);
public sealed record EmbeddingThresholdReport(
    double Threshold, DetectorAggregateMetric Macro, DetectorAggregateMetric Micro,
    IReadOnlyList<EmbeddingConceptMetric> Concepts);
public sealed record EmbeddingConceptComparison(
    string ConceptId, string Concept, int PositiveSupport, int NegativeSupport,
    double? RegexPrecision, double? RegexRecall, double? RegexF1,
    double? EmbeddingPrecision, double? EmbeddingRecall, double? EmbeddingF1,
    double? F1Delta);
public sealed record EmbeddingEvaluationReport(
    string Status, string? Error, int FixtureCount, int LabelCount,
    string? ModelId, string? ModelRevision, string? Device,
    double? AverageInferenceMilliseconds, double? AverageRoundTripMilliseconds,
    double? BestThreshold, IReadOnlyList<EmbeddingThresholdReport> Thresholds,
    IReadOnlyList<EmbeddingConceptComparison> ConceptComparisons,
    DetectorAggregateMetric RegexMacro, DetectorAggregateMetric RegexMicro,
    string? BuildSha);

public sealed class EmbeddingEvaluationService(
    DetectorEvaluationService fixtures, ClassifierClient classifier)
{
    public static readonly EmbeddingConcept[] Concepts = [
        new("role.ai-ml-engineering", "Hands-on engineering that builds, integrates, or operationalizes machine-learning models, AI systems, pipelines, or production AI applications."),
        new("role.software-engineering", "Direct design, implementation, testing, and maintenance of software systems as an engineering responsibility."),
        new("technical.software-development", "Hands-on implementation, testing, debugging, or maintenance of software applications, services, or systems."),
        new("technical.backend-development", "Server-side software development involving services, business logic, databases, microservices, APIs, and backend systems."),
        new("technical.api-development", "Direct design, implementation, integration, operation, or maintenance of programmatic service interfaces and APIs."),
        new("technical.automation-scripting", "Automating technical workflows, deployments, operations, testing, or repetitive tasks with scripts or software tooling."),
        new("role.cloud-engineering", "Hands-on design, implementation, operation, or reliability engineering of cloud infrastructure, services, and platforms."),
        new("technical.containers", "Direct implementation or operation of containerization and orchestration using Docker, Kubernetes, or related platforms.")
    ];
    public static readonly double[] ThresholdValues = [.50, .55, .60, .65, .70, .75, .80, .85, .90];

    public async Task<EmbeddingEvaluationReport> EvaluateAsync(
        string? buildSha = null, CancellationToken cancellationToken = default)
    {
        var cases = fixtures.BuildEmbeddingCases();
        var regex = fixtures.Evaluate(buildSha);
        var regexSelected = regex.Concepts.Where(item => Concepts.Any(concept => concept.ConceptId == item.ConceptId)).ToArray();
        var regexAggregate = Aggregate(regexSelected.Select(item => new Counts(
            item.TruePositive, item.FalsePositive, item.FalseNegative, item.TrueNegative)).ToArray());
        var predictions = new List<Prediction>();
        foreach (var item in cases)
        {
            var result = await classifier.ClassifyEmbeddingAsync(
                new(item.FixtureId, item.Title, item.Description), cancellationToken);
            if (!result.Available || result.Response is null)
                return new("unavailable", result.Error, cases.Count, cases.Sum(value => value.Labels.Count),
                    null, null, null, null, null, null, [], [], regexAggregate.Macro,
                    regexAggregate.Micro, NormalizeSha(buildSha));
            predictions.Add(new(item, result.Response, result.RoundTripMilliseconds));
        }
        var thresholds = ThresholdValues.Select(threshold => Calculate(predictions, threshold)).ToArray();
        var best = thresholds.OrderByDescending(item => item.Macro.F1 ?? -1)
            .ThenBy(item => item.Threshold).First();
        var comparisons = Compare(regexSelected, best);
        var first = predictions[0].Response;
        return new("complete", null, cases.Count, cases.Sum(item => item.Labels.Count),
            first.ModelId, first.ModelRevision, first.Device,
            predictions.Average(item => item.Response.InferenceMilliseconds),
            predictions.Average(item => item.RoundTripMilliseconds), best.Threshold, thresholds,
            comparisons, regexAggregate.Macro, regexAggregate.Micro, NormalizeSha(buildSha));
    }

    internal static IReadOnlyList<EmbeddingConceptComparison> Compare(
        IReadOnlyList<DetectorMetric> regexMetrics, EmbeddingThresholdReport embedding) =>
        regexMetrics.Select(regexMetric =>
        {
            var embeddingMetric = embedding.Concepts.Single(item => item.ConceptId == regexMetric.ConceptId);
            return new EmbeddingConceptComparison(
                regexMetric.ConceptId, regexMetric.Concept, regexMetric.PositiveSupport,
                regexMetric.NegativeExamples, regexMetric.Precision, regexMetric.Recall, regexMetric.F1,
                embeddingMetric.Precision, embeddingMetric.Recall, embeddingMetric.F1,
                regexMetric.F1 is double regexF1 && embeddingMetric.F1 is double embeddingF1
                    ? embeddingF1 - regexF1 : null);
        }).ToArray();

    internal static EmbeddingThresholdReport Calculate(IReadOnlyList<Prediction> predictions, double threshold)
    {
        var metrics = Concepts.Select(concept =>
        {
            var counts = new Counts();
            foreach (var prediction in predictions)
            {
                var expected = prediction.Case.Labels[concept.ConceptId];
                var actual = prediction.Response.Predictions.Single(
                    item => item.ConceptId == concept.ConceptId).Similarity >= threshold;
                counts = counts.Add(expected, actual);
            }
            var precision = DetectorEvaluationService.Divide(counts.TruePositive, counts.TruePositive + counts.FalsePositive);
            var recall = DetectorEvaluationService.Divide(counts.TruePositive, counts.TruePositive + counts.FalseNegative);
            return new EmbeddingConceptMetric(concept.ConceptId, threshold, counts.TruePositive,
                counts.FalsePositive, counts.FalseNegative, counts.TrueNegative, precision, recall,
                DetectorEvaluationService.HarmonicMean(precision, recall));
        }).ToArray();
        var aggregate = Aggregate(metrics.Select(item => new Counts(item.TruePositive, item.FalsePositive,
            item.FalseNegative, item.TrueNegative)).ToArray(), metrics.Select(item => item.F1).ToArray(),
            metrics.Select(item => item.Precision).ToArray(), metrics.Select(item => item.Recall).ToArray());
        return new(threshold, aggregate.Macro, aggregate.Micro, metrics);
    }

    private static (DetectorAggregateMetric Macro, DetectorAggregateMetric Micro) Aggregate(
        IReadOnlyList<Counts> values, IReadOnlyList<double?>? f1 = null,
        IReadOnlyList<double?>? precisionValues = null, IReadOnlyList<double?>? recallValues = null)
    {
        precisionValues ??= values.Select(item => DetectorEvaluationService.Divide(
            item.TruePositive, item.TruePositive + item.FalsePositive)).ToArray();
        recallValues ??= values.Select(item => DetectorEvaluationService.Divide(
            item.TruePositive, item.TruePositive + item.FalseNegative)).ToArray();
        f1 ??= precisionValues.Zip(recallValues, DetectorEvaluationService.HarmonicMean).ToArray();
        var tp = values.Sum(item => item.TruePositive); var fp = values.Sum(item => item.FalsePositive);
        var fn = values.Sum(item => item.FalseNegative); var labels = values.Sum(item => item.Total);
        var microPrecision = DetectorEvaluationService.Divide(tp, tp + fp);
        var microRecall = DetectorEvaluationService.Divide(tp, tp + fn);
        return (new(DetectorEvaluationService.AverageDefined(precisionValues),
                    DetectorEvaluationService.AverageDefined(recallValues),
                    DetectorEvaluationService.AverageDefined(f1), values.Count, labels),
                new(microPrecision, microRecall,
                    DetectorEvaluationService.HarmonicMean(microPrecision, microRecall), values.Count, labels));
    }
    private static string? NormalizeSha(string? value) => value is { Length: 40 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ? value : null;
    internal sealed record Prediction(EmbeddingEvaluationCase Case,
        EmbeddingClassifierResponse Response, double RoundTripMilliseconds);
    internal sealed record Counts(int TruePositive = 0, int FalsePositive = 0,
        int FalseNegative = 0, int TrueNegative = 0)
    {
        public int Total => TruePositive + FalsePositive + FalseNegative + TrueNegative;
        public Counts Add(bool expected, bool actual) => (expected, actual) switch {
            (true, true) => this with { TruePositive = TruePositive + 1 },
            (false, true) => this with { FalsePositive = FalsePositive + 1 },
            (true, false) => this with { FalseNegative = FalseNegative + 1 },
            _ => this with { TrueNegative = TrueNegative + 1 }
        };
    }
}
