namespace PromptBench.Api.Prompts;

public sealed record PromptDefinition(
    string Name,
    string Version,
    string Template);
