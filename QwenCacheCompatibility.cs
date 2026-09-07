// Historical cache DTO only. No inference client or runtime model dependency.
namespace JobSearchManager;

public sealed record QwenInferenceMetrics(
    long? TotalDurationNanoseconds, long? LoadDurationNanoseconds,
    long? PromptTokenCount, long? PromptDurationNanoseconds,
    long? OutputTokenCount, long? OutputDurationNanoseconds,
    double? TokensPerSecond, long? ModelResidentBytes, long? ModelVramBytes,
    long? AdapterPeakResidentBytes);
