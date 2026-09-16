namespace PromptBench.Api.OpenRouter;

public sealed record OpenRouterCompletion(
    string Output,
    string? Model,
    TokenUsage? Usage);

public sealed record TokenUsage(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
