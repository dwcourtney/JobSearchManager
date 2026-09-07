// Persisted historical evaluation ledger contract, shared with explicit research tools.
namespace JobSearchManager;

public sealed record LlmEvaluationLedgerDetails(
    string EvaluationRunId, string HoldoutFingerprint, string ReferenceLabelFingerprint,
    string PredictionFingerprint, string ClassifierType, string ModelId, string ModelTag,
    string ModelDigest, string PromptVersion, string PromptHash,
    string GenerationConfigurationJson, string RuntimeMetricsJson,
    string ComparisonMetricsJson, string Limitations);
