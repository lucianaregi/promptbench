namespace PromptBench.Api.Evals;

public sealed record EvaluationSet
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<EvaluationCase> Cases { get; init; }
}
