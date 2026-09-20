using PromptBench.Api.Prompts;

namespace PromptBench.Tests;

public sealed class PromptLoaderTests
{
    [Fact]
    public async Task LoadsPromptByNameAndVersion()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteAsync("resumo-v1", ValidPromptJson);
        var loader = new PromptLoader(directory.Path);

        var result = await loader.LoadAsync("summarization", "v1");

        Assert.Equal(PromptLoadStatus.Success, result.Status);
        Assert.Equal("summarization", result.Prompt?.Name);
        Assert.Equal("v1", result.Prompt?.Version);
        Assert.Contains("{{input}}", result.Prompt?.Template);
    }

    [Theory]
    [InlineData("", "v1", "Texto: {{input}}")]
    [InlineData("summarization", "", "Texto: {{input}}")]
    [InlineData("summarization", "v1", "")]
    [InlineData("summarization", "v1", "Texto sem variável")]
    public async Task RejectsInvalidPrompt(string name, string version, string template)
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteAsync("invalido", $$"""
            {
              "name": "{{name}}",
              "version": "{{version}}",
              "template": "{{template}}"
            }
            """);
        var loader = new PromptLoader(directory.Path);

        var result = await loader.LoadAsync("summarization", "v1");

        Assert.Equal(PromptLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task RejectsDuplicatePromptIdentity()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteAsync("primeiro", ValidPromptJson);
        await directory.WriteAsync("segundo", ValidPromptJson);
        var loader = new PromptLoader(directory.Path);

        var result = await loader.LoadAsync("summarization", "v1");

        Assert.Equal(PromptLoadStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownVersion()
    {
        using var directory = new TemporaryDirectory();
        await directory.WriteAsync("resumo-v1", ValidPromptJson);
        var loader = new PromptLoader(directory.Path);

        var result = await loader.LoadAsync("summarization", "v2");

        Assert.Equal(PromptLoadStatus.NotFound, result.Status);
    }

    private const string ValidPromptJson = """
        {
          "name": "summarization",
          "version": "v1",
          "template": "Resuma o texto: {{input}}"
        }
        """;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-prompts-tests-").FullName;
        }

        public string Path { get; }

        public Task WriteAsync(string name, string contents) =>
            File.WriteAllTextAsync(System.IO.Path.Combine(Path, $"{name}.json"), contents);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
