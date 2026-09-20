using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class PersistedRunComparisonServiceTests
{
    [Fact]
    public async Task ClassifiesCasesAndBuildsCorrectSummaryAndMetrics()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            800,
            20,
            ("sem-alteracao-aprovado", true),
            ("sem-alteracao-reprovado", false),
            ("regressao", true),
            ("melhoria", false),
            ("removido", true));
        var candidate = CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            1_000,
            24,
            ("sem-alteracao-aprovado", true),
            ("sem-alteracao-reprovado", false),
            ("regressao", false),
            ("melhoria", true),
            ("adicionado", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var service = new PersistedRunComparisonService(store);

        var outcome = await service.CompareAsync(baseline.Id, candidate.Id);

        Assert.Equal(PersistedRunComparisonStatus.Success, outcome.Status);
        var result = Assert.IsType<PersistedRunComparisonResult>(outcome.Result);
        Assert.Equal("summarization-basic", result.Evaluation);
        Assert.Equal(6, result.Summary.Total);
        Assert.Equal(1, result.Summary.Regressions);
        Assert.Equal(1, result.Summary.Improvements);
        Assert.Equal(1, result.Summary.UnchangedPass);
        Assert.Equal(1, result.Summary.UnchangedFail);
        Assert.Equal(1, result.Summary.Added);
        Assert.Equal(1, result.Summary.Removed);
        AssertCase(result, "sem-alteracao-aprovado", "unchanged_pass", true, true);
        AssertCase(result, "sem-alteracao-reprovado", "unchanged_fail", false, false);
        AssertCase(result, "regressao", "regression", true, false);
        AssertCase(result, "melhoria", "improvement", false, true);
        AssertCase(result, "adicionado", "added", null, false);
        AssertCase(result, "removido", "removed", true, null);
        Assert.Equal(60, result.Metrics.PassRate.Baseline);
        Assert.Equal(40, result.Metrics.PassRate.Candidate);
        Assert.Equal(-20, result.Metrics.PassRate.Difference);
        Assert.Equal(200, result.Metrics.DurationMs.Difference);
        Assert.Equal(100, result.Metrics.TotalTokens.Baseline);
        Assert.Equal(120, result.Metrics.TotalTokens.Candidate);
        Assert.Equal(20, result.Metrics.TotalTokens.Difference);
        Assert.Null(result.BaselinePromptName);
        Assert.Null(result.BaselinePromptVersion);
        Assert.Null(result.CandidatePromptName);
        Assert.Null(result.CandidatePromptVersion);
    }

    [Fact]
    public async Task DistinguishesMissingBaselineAndCandidate()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var existing = CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("caso", true));
        await store.SaveAsync(existing);
        var service = new PersistedRunComparisonService(store);

        var missingBaseline = await service.CompareAsync(Guid.NewGuid(), existing.Id);
        var missingCandidate = await service.CompareAsync(existing.Id, Guid.NewGuid());

        Assert.Equal(PersistedRunComparisonStatus.BaselineNotFound, missingBaseline.Status);
        Assert.Equal(PersistedRunComparisonStatus.CandidateNotFound, missingCandidate.Status);
    }

    [Fact]
    public async Task RejectsDifferentEvaluationSets()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = CreateRun(Guid.NewGuid(), "resumos", 300, 20, ("caso", true));
        var candidate = CreateRun(Guid.NewGuid(), "classificacao", 300, 20, ("caso", true));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var service = new PersistedRunComparisonService(store);

        var outcome = await service.CompareAsync(baseline.Id, candidate.Id);

        Assert.Equal(PersistedRunComparisonStatus.DifferentEvaluation, outcome.Status);
    }

    [Fact]
    public async Task ComparesDifferentPromptVersionsAndExposesTheirIdentity()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = CreateRun(
            Guid.NewGuid(), "resumos", 300, 20, ("caso", true)) with
        {
            PromptName = "summarization",
            PromptVersion = "v1"
        };
        var candidate = CreateRun(
            Guid.NewGuid(), "resumos", 300, 20, ("caso", true)) with
        {
            PromptName = "summarization",
            PromptVersion = "v2"
        };
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var service = new PersistedRunComparisonService(store);

        var outcome = await service.CompareAsync(baseline.Id, candidate.Id);

        Assert.Equal(PersistedRunComparisonStatus.Success, outcome.Status);
        Assert.Equal("v1", outcome.Result?.Baseline.PromptVersion);
        Assert.Equal("v2", outcome.Result?.Candidate.PromptVersion);
        Assert.Equal("summarization", outcome.Result?.Baseline.PromptName);
        Assert.Equal("summarization", outcome.Result?.Candidate.PromptName);
        Assert.Equal("summarization", outcome.Result?.BaselinePromptName);
        Assert.Equal("v1", outcome.Result?.BaselinePromptVersion);
        Assert.Equal("summarization", outcome.Result?.CandidatePromptName);
        Assert.Equal("v2", outcome.Result?.CandidatePromptVersion);
        Assert.True(new RegressionGateService().Evaluate(outcome.Result!).Passed);
    }

    [Fact]
    public async Task RejectsRunWithoutCompletedEvaluations()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = CreateRun(Guid.NewGuid(), "resumos", 300, 20, ("caso", true));
        var candidate = baseline with
        {
            Id = Guid.NewGuid(),
            Results =
            [
                baseline.Results[0] with { Evaluation = null }
            ]
        };
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        var service = new PersistedRunComparisonService(store);

        var outcome = await service.CompareAsync(baseline.Id, candidate.Id);

        Assert.Equal(PersistedRunComparisonStatus.InsufficientData, outcome.Status);
    }

    [Fact]
    public async Task ReportsInvalidPersistedFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baselineId = Guid.NewGuid();
        var candidate = CreateRun(Guid.NewGuid(), "resumos", 300, 20, ("caso", true));
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, $"{baselineId:D}.json"),
            "{ execução inválida }");
        await store.SaveAsync(candidate);
        var service = new PersistedRunComparisonService(store);

        var outcome = await service.CompareAsync(baselineId, candidate.Id);

        Assert.Equal(PersistedRunComparisonStatus.InvalidStorage, outcome.Status);
    }

    internal static EvaluationRunResult CreateRun(
        Guid id,
        string evaluation,
        long durationMs,
        int totalTokensPerCase,
        params (string CaseId, bool Passed)[] cases) =>
        new(
            id,
            evaluation,
            "provedor/modelo:free",
            DateTimeOffset.Parse("2026-09-16T17:30:00Z"),
            durationMs,
            cases.Select(item => new EvaluationCaseRunResult(
                item.CaseId,
                $"Entrada do caso {item.CaseId}.",
                $"Resposta esperada do caso {item.CaseId}.",
                $"Resposta produzida para o caso {item.CaseId}.",
                "provedor/modelo-real",
                100,
                new TokenUsage(8, 2, totalTokensPerCase - 4),
                new EvaluatorResult(
                    "llm_judge",
                    "completed",
                    item.Passed,
                    item.Passed
                        ? "A resposta atende ao critério."
                        : "A resposta não atende ao critério.",
                    "provedor/judge:free",
                    "provedor/judge-real",
                    50,
                    new TokenUsage(3, 1, 4),
                    null)))
                .ToArray());

    private static void AssertCase(
        PersistedRunComparisonResult result,
        string caseId,
        string status,
        bool? baselinePassed,
        bool? candidatePassed)
    {
        var comparison = Assert.Single(result.Cases, item => item.CaseId == caseId);
        Assert.Equal(status, comparison.Status);
        Assert.Equal(baselinePassed, comparison.BaselinePassed);
        Assert.Equal(candidatePassed, comparison.CandidatePassed);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-comparison-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
