namespace PromptBench.Api.Runs;

public sealed record RunSummary(
    Guid Id,
    string Type,
    string Evaluation,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> Models,
    string Status,
    double? PassRate);
