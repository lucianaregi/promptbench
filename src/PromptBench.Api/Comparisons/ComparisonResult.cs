using PromptBench.Api.Runs;

namespace PromptBench.Api.Comparisons;

public sealed record ComparisonResult(
    Guid Id,
    string Evaluation,
    DateTimeOffset StartedAt,
    long DurationMs,
    string Status,
    IReadOnlyList<ComparisonRunResult> Runs,
    string? PromptName = null,
    string? PromptVersion = null) : IPersistedRunResult
{
    public string Type => "comparison";
}

public sealed record ComparisonRunResult(
    Guid Id,
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
