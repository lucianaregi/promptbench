namespace PromptBench.Api.Evals;

public sealed record EvaluationConfiguration
{
    public required string Type { get; init; }

    public string? JudgeModel { get; init; }

    public string? Criteria { get; init; }
}
