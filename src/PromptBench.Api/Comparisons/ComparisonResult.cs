using PromptBench.Api.Runs;

namespace PromptBench.Api.Comparisons;

public sealed record ComparisonResult(
    string Evaluation,
    DateTimeOffset StartedAt,
    long DurationMs,
    string Status,
    IReadOnlyList<ComparisonRunResult> Runs);

public sealed record ComparisonRunResult(
    string RequestedModel,
    string? ActualModel,
    string Status,
    long DurationMs,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    IReadOnlyList<EvaluationCaseRunResult> Results,
    ComparisonError? Error);

public sealed record ComparisonError(string Type, string Message);
