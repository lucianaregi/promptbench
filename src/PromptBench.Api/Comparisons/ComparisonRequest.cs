using System.ComponentModel.DataAnnotations;

namespace PromptBench.Api.Comparisons;

public sealed record ComparisonRequest(
    [property: Required, MinLength(2)] IReadOnlyList<string>? Models);