using PromptBench.Api.OpenRouter;

namespace PromptBench.Api.Runs;

public sealed record EvaluationRunResult(
    string Evaluation,
    string RequestedModel,
    DateTimeOffset StartedAt,
    long DurationMs,
    IReadOnlyList<EvaluationCaseRunResult> Results);

public sealed record EvaluationCaseRunResult(
    string CaseId,
    string Input,
    string Expected,
    string Output,
    string? UsedModel,
    long DurationMs,
    TokenUsage? Usage,
    EvaluatorResult? Evaluation);

public sealed record EvaluatorResult(
    string Type,
    string Status,
    bool? Passed,
    string? Reason,
    string RequestedModel,
    string? UsedModel,
    long DurationMs,
    TokenUsage? Usage,
    EvaluatorError? Error);

public sealed record EvaluatorError(string Type, string Message);
