namespace PromptBench.Api.Evals;

public sealed record EvaluationCase
{
    public required string Id { get; init; }

    public required string Input { get; init; }

    public required string Expected { get; init; }
}
