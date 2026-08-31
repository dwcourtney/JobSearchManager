namespace JobSearchManager;

public sealed record ZeroShotConcept(string ConceptId, string Hypothesis);
public sealed record ZeroShotEvaluationCase(
    string FixtureId, string Title, string Description,
    IReadOnlyDictionary<string, bool> Labels);
public sealed record ZeroShotConceptMetric(
    string ConceptId, double Threshold, int TruePositive, int FalsePositive,
    int FalseNegative, int TrueNegative, double? Precision, double? Recall, double? F1);
public sealed record ZeroShotThresholdReport(
    double Threshold, DetectorAggregateMetric Macro, DetectorAggregateMetric Micro,
    IReadOnlyList<ZeroShotConceptMetric> Concepts);
public sealed record ZeroShotEvaluationReport(
    string Status, string? Error, int FixtureCount, int LabelCount,
    string? ModelId, string? ModelRevision, string? Device,
    double? AverageInferenceMilliseconds, double? AverageRoundTripMilliseconds,
    double? BestThreshold, IReadOnlyList<ZeroShotThresholdReport> Thresholds,
    DetectorAggregateMetric RegexMacro, DetectorAggregateMetric RegexMicro,
    string? BuildSha);

public sealed class ZeroShotEvaluationService(
    DetectorEvaluationService fixtures, ClassifierClient classifier)
{
    public static readonly ZeroShotConcept[] Concepts = [
        new("role.ai-ml-engineering", "This job involves artificial intelligence or machine-learning engineering work."),
        new("role.software-engineering", "This job involves direct software engineering work."),
        new("technical.software-development", "This job involves developing or maintaining software."),
        new("technical.backend-development", "This job involves backend or server-side software development."),
        new("technical.api-development", "This job involves designing, implementing, or maintaining APIs."),
        new("technical.automation-scripting", "This job involves scripting or software automation."),
        new("role.cloud-engineering", "This job involves cloud engineering responsibilities."),
        new("technical.containers", "This job involves Kubernetes, Docker, or container orchestration responsibilities.")
    ];
    public static readonly double[] ThresholdValues = [.3, .5, .7];

    public async Task<ZeroShotEvaluationReport> EvaluateAsync(
        string? buildSha = null, CancellationToken cancellationToken = default)
    {
        var cases = fixtures.BuildZeroShotCases();
        var regex = fixtures.Evaluate(buildSha);
        var regexSelected = regex.Concepts.Where(item => Concepts.Any(concept => concept.ConceptId == item.ConceptId)).ToArray();
        var regexAggregate = Aggregate(regexSelected.Select(item => new Counts(
            item.TruePositive, item.FalsePositive, item.FalseNegative, item.TrueNegative)).ToArray());
        var predictions = new List<Prediction>();
        foreach (var item in cases)
        {
            var result = await classifier.ClassifyZeroShotAsync(
                new(item.FixtureId, item.Title, item.Description), cancellationToken);
            if (!result.Available || result.Response is null)
                return new("unavailable", result.Error, cases.Count, cases.Sum(value => value.Labels.Count),
                    null, null, null, null, null, null, [], regexAggregate.Macro,
                    regexAggregate.Micro, NormalizeSha(buildSha));
            predictions.Add(new(item, result.Response, result.RoundTripMilliseconds));
        }
        var thresholds = ThresholdValues.Select(threshold => Calculate(predictions, threshold)).ToArray();
        var best = thresholds.OrderByDescending(item => item.Macro.F1 ?? -1)
            .ThenBy(item => item.Threshold).First();
        var first = predictions[0].Response;
        return new("complete", null, cases.Count, cases.Sum(item => item.Labels.Count),
            first.ModelId, first.ModelRevision, first.Device,
            predictions.Average(item => item.Response.InferenceMilliseconds),
            predictions.Average(item => item.RoundTripMilliseconds), best.Threshold, thresholds,
            regexAggregate.Macro, regexAggregate.Micro, NormalizeSha(buildSha));
    }

    internal static ZeroShotThresholdReport Calculate(IReadOnlyList<Prediction> predictions, double threshold)
    {
        var metrics = Concepts.Select(concept =>
        {
            var counts = new Counts();
            foreach (var prediction in predictions)
            {
                var expected = prediction.Case.Labels[concept.ConceptId];
                var actual = prediction.Response.Scores.Single(item => item.ConceptId == concept.ConceptId).Score >= threshold;
                counts = counts.Add(expected, actual);
            }
            var precision = DetectorEvaluationService.Divide(counts.TruePositive, counts.TruePositive + counts.FalsePositive);
            var recall = DetectorEvaluationService.Divide(counts.TruePositive, counts.TruePositive + counts.FalseNegative);
            return new ZeroShotConceptMetric(concept.ConceptId, threshold, counts.TruePositive,
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
    internal sealed record Prediction(ZeroShotEvaluationCase Case,
        ZeroShotClassifierResponse Response, double RoundTripMilliseconds);
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
