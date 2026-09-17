using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class RegressionGateServiceTests
{
    [Fact]
    public async Task PassesForImprovementsUnchangedAddedAndRemovedCases()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("sem-alteracao", true),
            ("melhoria", false),
            ("removido", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("sem-alteracao", true),
            ("melhoria", true),
            ("adicionado", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var comparisonService = new PersistedRunComparisonService(store);
        var comparison = await comparisonService.CompareAsync(baseline.Id, candidate.Id);
        var gateService = new RegressionGateService();

        var gate = gateService.Evaluate(comparison.Result!);

        Assert.True(gate.Passed);
        Assert.Equal(0, gate.Regressions);
        Assert.Null(gate.RegressionCaseIds);
    }

    [Fact]
    public async Task FailsAndReturnsEveryRegressionCaseId()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("primeira-regressao", true),
            ("segunda-regressao", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("primeira-regressao", false),
            ("segunda-regressao", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var comparisonService = new PersistedRunComparisonService(store);
        var comparison = await comparisonService.CompareAsync(baseline.Id, candidate.Id);
        var gateService = new RegressionGateService();

        var gate = gateService.Evaluate(comparison.Result!);

        Assert.False(gate.Passed);
        Assert.Equal(2, gate.Regressions);
        Assert.Equal(
            ["primeira-regressao", "segunda-regressao"],
            gate.RegressionCaseIds);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-gate-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
