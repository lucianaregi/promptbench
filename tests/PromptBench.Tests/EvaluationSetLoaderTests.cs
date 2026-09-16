using PromptBench.Api.Evals;

namespace PromptBench.Tests;

public sealed class EvaluationSetLoaderTests
{
    [Fact]
    public async Task LoadsValidEvaluationSet()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("valid", ValidEvaluationSetJson);
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("valid");

        Assert.Equal(EvaluationSetLoadStatus.Success, result.Status);
        Assert.Equal("exemplo", result.EvaluationSet?.Name);
        Assert.Single(result.EvaluationSet!.Cases);
    }

    [Fact]
    public async Task RejectsEvaluationSetWithoutRequiredFields()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("missing-fields", """
            {
              "name": "",
              "description": "",
              "cases": []
            }
            """);
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("missing-fields");

        Assert.Equal(EvaluationSetLoadStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RejectsDuplicateCaseIds()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("duplicates", """
            {
              "name": "duplicates",
              "description": "Contém IDs duplicados.",
              "cases": [
                { "id": "same", "input": "Primeiro", "expected": "Primeiro" },
                { "id": "same", "input": "Segundo", "expected": "Segundo" }
              ]
            }
            """);
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("duplicates");

        Assert.Equal(EvaluationSetLoadStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Contains("duplicado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadsLlmJudgeConfiguration()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("com-judge", """
            {
              "name": "com-judge",
              "description": "Avaliação com judge.",
              "evaluation": {
                "type": "llm_judge",
                "judgeModel": "provedor/judge:free",
                "criteria": "A resposta deve preservar a informação principal."
              },
              "cases": [
                { "id": "caso-1", "input": "Entrada", "expected": "Resposta esperada" }
              ]
            }
            """);
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("com-judge");

        Assert.Equal(EvaluationSetLoadStatus.Success, result.Status);
        Assert.Equal("llm_judge", result.EvaluationSet?.Evaluation?.Type);
        Assert.Equal("provedor/judge:free", result.EvaluationSet?.Evaluation?.JudgeModel);
    }

    [Theory]
    [InlineData("outro_tipo", "provedor/judge:free", "Critério informado.")]
    [InlineData("llm_judge", "", "Critério informado.")]
    [InlineData("llm_judge", "provedor/judge:free", "")]
    public async Task RejectsInvalidLlmJudgeConfiguration(
        string type,
        string judgeModel,
        string criteria)
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("judge-invalido", $$"""
            {
              "name": "judge-invalido",
              "description": "Avaliação com configuração inválida.",
              "evaluation": {
                "type": "{{type}}",
                "judgeModel": "{{judgeModel}}",
                "criteria": "{{criteria}}"
              },
              "cases": [
                { "id": "caso-1", "input": "Entrada", "expected": "Resposta esperada" }
              ]
            }
            """);
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("judge-invalido");

        Assert.Equal(EvaluationSetLoadStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task RejectsInvalidJson()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteEvalAsync("broken", "{ not-json }");
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("broken");

        Assert.Equal(EvaluationSetLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingEvaluationSet()
    {
        using var directory = new TemporaryDirectory();
        var loader = new EvaluationSetLoader(directory.Path);

        var result = await loader.LoadAsync("missing");

        Assert.Equal(EvaluationSetLoadStatus.NotFound, result.Status);
    }

    private const string ValidEvaluationSetJson = """
        {
          "name": "exemplo",
          "description": "Um conjunto de avaliação válido.",
          "cases": [
            { "id": "case-1", "input": "Entrada", "expected": "Esperado" }
          ]
        }
        """;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-tests-").FullName;
        }

        public string Path { get; }

        public Task WriteEvalAsync(string name, string contents) =>
            File.WriteAllTextAsync(System.IO.Path.Combine(Path, $"{name}.json"), contents);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
