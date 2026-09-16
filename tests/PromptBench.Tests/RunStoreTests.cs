using System.Text.Json;
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

    private static EvaluationRunResult CreateResult(Guid id) =>
        new(
            id,
            "summarization-basic",
            "provedor/modelo:free",
            DateTimeOffset.Parse("2026-09-16T12:00:00Z"),
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
