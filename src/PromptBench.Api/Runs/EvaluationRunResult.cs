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
    TokenUsage? Usage);
