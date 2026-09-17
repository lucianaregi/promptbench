using System.Text.Json;
using PromptBench.Api.Comparisons;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class RunStoreTests
{
    [Fact]
    public async Task SavesAndLoadsCompleteRunResult()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var id = Guid.NewGuid();
        var result = CreateResult(id);

        await store.SaveAsync(result);
        var loaded = await store.LoadAsync(id);

        Assert.Equal(RunLoadStatus.Success, loaded.Status);
        Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, $"{id:D}.json")));
        var restored = loaded.Result?.Deserialize<EvaluationRunResult>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(restored);
        Assert.Equal(id, restored.Id);
        Assert.Equal("summarization-basic", restored.Evaluation);
        var caseResult = Assert.Single(restored.Results);
        Assert.Equal("A biblioteca agora abre aos domingos.", caseResult.Output);
        Assert.True(caseResult.Evaluation?.Passed);
        Assert.Equal(31, caseResult.Evaluation?.Usage?.TotalTokens);
    }

    [Fact]
    public async Task DoesNotLeavePartialFileWhenSaveIsCanceled()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var result = CreateResult(Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(result, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task ListsEmptyDirectory()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);

        var summaries = await store.ListAsync();

        Assert.Empty(summaries);
    }

    [Fact]
    public async Task ListsSummaryWithRelevantData()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var result = CreateResult(Guid.NewGuid());
        await store.SaveAsync(result);

        var summary = Assert.Single(await store.ListAsync());

        Assert.Equal(result.Id, summary.Id);
        Assert.Equal("evaluation_run", summary.Type);
        Assert.Equal("summarization-basic", summary.Evaluation);
        Assert.Equal(result.StartedAt, summary.StartedAt);
        Assert.Equal(["provedor/modelo:free"], summary.Models);
        Assert.Equal("completed", summary.Status);
        Assert.Equal(100, summary.PassRate);
    }

    [Fact]
    public async Task ListsComparisonModelsStatusAndAggregatedPassRate()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var comparison = new ComparisonResult(
            Guid.NewGuid(),
            "summarization-basic",
            DateTimeOffset.Parse("2026-09-16T15:00:00Z"),
            900,
            "completed",
            [
                CreateComparisonRun("provedor/modelo-a:free", true),
                CreateComparisonRun("provedor/modelo-b:free", false)
            ]);
        await store.SaveAsync(comparison);

        var summary = Assert.Single(await store.ListAsync());

        Assert.Equal("comparison", summary.Type);
        Assert.Equal(
            ["provedor/modelo-a:free", "provedor/modelo-b:free"],
            summary.Models);
        Assert.Equal("completed", summary.Status);
        Assert.Equal(50, summary.PassRate);
    }

    [Fact]
    public async Task ListsRunsFromNewestToOldest()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var older = CreateResult(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-16T12:00:00Z"));
        var newer = CreateResult(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-16T14:00:00Z"));
        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var summaries = await store.ListAsync();

        Assert.Equal([newer.Id, older.Id], summaries.Select(summary => summary.Id));
    }

    [Fact]
    public async Task IgnoresInvalidFileAndPreservesValidRuns()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var valid = CreateResult(Guid.NewGuid());
        await store.SaveAsync(valid);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "arquivo-invalido.json"),
            "{ conteúdo inválido }");

        var summary = Assert.Single(await store.ListAsync());

        Assert.Equal(valid.Id, summary.Id);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingId()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);

        var result = await store.LoadAsync(Guid.NewGuid());

        Assert.Equal(RunLoadStatus.NotFound, result.Status);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task ReportsInvalidJson()
    {
        using var directory = new TemporaryDirectory();
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, $"{id:D}.json"),
            "{ conteúdo inválido }");
        var store = new RunStore(directory.Path);

        var result = await store.LoadAsync(id);

        Assert.Equal(RunLoadStatus.Invalid, result.Status);
        Assert.Null(result.Result);
    }

    private static EvaluationRunResult CreateResult(
        Guid id,
        DateTimeOffset? startedAt = null) =>
        new(
            id,
            "summarization-basic",
            "provedor/modelo:free",
            startedAt ?? DateTimeOffset.Parse("2026-09-16T12:00:00Z"),
            450,
            [
                new EvaluationCaseRunResult(
                    "biblioteca-aos-domingos",
                    "A biblioteca do bairro agora também abre aos domingos.",
                    "A biblioteca passou a abrir aos domingos.",
                    "A biblioteca agora abre aos domingos.",
                    "provedor/modelo-real",
                    300,
                    new TokenUsage(20, 10, 30),
                    new EvaluatorResult(
                        "llm_judge",
                        "completed",
                        true,
                        "A resposta preserva a informação principal.",
                        "provedor/judge:free",
                        "provedor/judge-real",
                        150,
                        new TokenUsage(22, 9, 31),
                        null))
            ]);

    private static ComparisonRunResult CreateComparisonRun(string model, bool passed) =>
        new(
            Guid.NewGuid(),
            model,
            model,
            "completed",
            400,
            20,
            10,
            30,
            [
                new EvaluationCaseRunResult(
                    "biblioteca-aos-domingos",
                    "A biblioteca agora também abre aos domingos.",
                    "A biblioteca passou a abrir aos domingos.",
                    "A biblioteca também abre aos domingos.",
                    model,
                    250,
                    new TokenUsage(12, 6, 18),
                    new EvaluatorResult(
                        "llm_judge",
                        "completed",
                        passed,
                        passed
                            ? "A resposta preserva a informação principal."
                            : "A resposta não preserva a informação principal.",
                        "provedor/judge:free",
                        "provedor/judge-real",
                        150,
                        new TokenUsage(8, 4, 12),
                        null))
            ],
            null);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-store-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
