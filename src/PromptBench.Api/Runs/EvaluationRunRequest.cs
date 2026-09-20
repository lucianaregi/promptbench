namespace PromptBench.Api.Runs;

public sealed record EvaluationRunRequest(
    string Model,
    string PromptName,
    string PromptVersion);
