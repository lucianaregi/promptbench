using System.Text.Json.Serialization;

namespace PromptBench.Api.Runs;

public sealed class RegressionGateService
{
    public RegressionGateResult Evaluate(PersistedRunComparisonResult comparison)
    {
        var regressionCaseIds = comparison.Cases
            .Where(item => item.Status is "regression")
            .Select(item => item.CaseId)
            .ToArray();

        return new RegressionGateResult(
            comparison.Summary.Regressions is 0,
            comparison.Summary.Regressions,
            regressionCaseIds.Length is 0 ? null : regressionCaseIds);
    }
}

public sealed record RegressionGateResult(
    bool Passed,
    int Regressions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? RegressionCaseIds);
