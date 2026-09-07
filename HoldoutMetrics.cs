namespace JobSearchManager;

internal sealed record HoldoutMetricResult(
    IReadOnlyList<AiHoldoutConceptMetric> Concepts,
    RegexAggregateEvaluation Macro, RegexAggregateEvaluation Micro,
    int EligibleCount, int PositiveCount, int NegativeCount);

internal static class HoldoutMetricCalculator
{
    public static HoldoutMetricResult Calculate(JobConceptCatalog catalog,
        AiReferenceDataset references,
        IReadOnlyDictionary<string, IReadOnlySet<string>> predictions)
    {
        var concepts = new List<AiHoldoutConceptMetric>();
        foreach (var concept in catalog.Concepts)
        {
            var values = references.Decisions.Where(item => item.ConceptId == concept.Id).ToArray();
            var eligible = values.Where(item => !item.Unresolved).ToArray();
            var tp = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Present &&
                predictions[item.EvaluationExampleId].Contains(concept.Id));
            var fp = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Absent &&
                predictions[item.EvaluationExampleId].Contains(concept.Id));
            var fn = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Present &&
                !predictions[item.EvaluationExampleId].Contains(concept.Id));
            var tn = eligible.Count(item => item.FinalReferenceJudgment == AiReferenceJudgments.Absent &&
                !predictions[item.EvaluationExampleId].Contains(concept.Id));
            var precision = Divide(tp, tp + fp);
            var recall = Divide(tp, tp + fn);
            concepts.Add(new(concept.Id, concept.DisplayName, tp + fn, eligible.Length,
                values.Length - eligible.Length, tp, fp, fn, tn, precision, recall,
                Harmonic(precision, recall), values.Count(item => !item.Agreed),
                values.Length == 0 ? 0 : (double)values.Count(item => !item.Agreed) / values.Length));
        }
        var macro = new RegexAggregateEvaluation(Average(concepts.Select(item => item.Precision)),
            Average(concepts.Select(item => item.Recall)), Average(concepts.Select(item => item.F1)),
            concepts.Count, concepts.Sum(item => item.EligibleDecisions));
        var totalTp = concepts.Sum(item => item.TruePositive);
        var totalFp = concepts.Sum(item => item.FalsePositive);
        var totalFn = concepts.Sum(item => item.FalseNegative);
        var microPrecision = Divide(totalTp, totalTp + totalFp);
        var microRecall = Divide(totalTp, totalTp + totalFn);
        var micro = new RegexAggregateEvaluation(microPrecision, microRecall,
            Harmonic(microPrecision, microRecall), concepts.Count,
            concepts.Sum(item => item.EligibleDecisions));
        var eligibleCount = concepts.Sum(item => item.EligibleDecisions);
        var positives = concepts.Sum(item => item.Support);
        return new(concepts, macro, micro, eligibleCount, positives, eligibleCount - positives);
    }

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
}
