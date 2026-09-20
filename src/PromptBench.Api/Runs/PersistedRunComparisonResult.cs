namespace PromptBench.Api.Runs;

public sealed record PersistedRunComparisonResult(
    Guid BaselineId,
    Guid CandidateId,
    string Evaluation,
    PersistedRunInfo Baseline,
    PersistedRunInfo Candidate,
    PersistedRunComparisonSummary Summary,
    PersistedRunComparisonMetrics Metrics,
    IReadOnlyList<PersistedRunCaseComparison> Cases);

public sealed record PersistedRunInfo(
    Guid Id,
    DateTimeOffset StartedAt,
    string RequestedModel,
    IReadOnlyList<string> UsedModels,
    string? PromptName = null,
    string? PromptVersion = null);

public sealed record PersistedRunComparisonSummary(
    int Total,
    int Regressions,
    int Improvements,
    int UnchangedPass,
    int UnchangedFail,
    int Added,
    int Removed);

public sealed record PersistedRunComparisonMetrics(
    RunDoubleMetricComparison PassRate,
    RunLongMetricComparison DurationMs,
    RunLongMetricComparison TotalTokens);

public sealed record RunDoubleMetricComparison(
    double? Baseline,
    double? Candidate,
    double? Difference);

public sealed record RunLongMetricComparison(
    long? Baseline,
    long? Candidate,
    long? Difference);

public sealed record PersistedRunCaseComparison(
    string CaseId,
    string Status,
    bool? BaselinePassed,
    bool? CandidatePassed);
