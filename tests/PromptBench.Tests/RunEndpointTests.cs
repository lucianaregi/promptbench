using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class RunEndpointTests
{
    [Fact]
    public async Task ReturnsNotFoundForMissingRun()
    {
        using var directory = new TemporaryDirectory();
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/runs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListsPersistedRunSummaries()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var result = new EvaluationRunResult(
            Guid.NewGuid(),
            "summarization-basic",
            "provedor/modelo:free",
            DateTimeOffset.Parse("2026-09-16T17:30:00Z"),
            320,
            [
                new EvaluationCaseRunResult(
                    "biblioteca-aos-domingos",
                    "A biblioteca agora abre aos domingos.",
                    "A biblioteca passou a abrir aos domingos.",
                    "A biblioteca também abre aos domingos.",
                    "provedor/modelo-real",
                    320,
                    null,
                    null)
            ]);
        await store.SaveAsync(result);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/runs");
        var summaries = await response.Content.ReadFromJsonAsync<List<RunSummary>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = Assert.Single(summaries!);
        Assert.Equal(result.Id, summary.Id);
        Assert.Equal("summarization-basic", summary.Evaluation);
        Assert.Equal(["provedor/modelo:free"], summary.Models);
        Assert.Equal("completed", summary.Status);
        Assert.Null(summary.PassRate);
    }

    [Fact]
    public async Task ReturnsControlledErrorForInvalidStoredJson()
    {
        using var directory = new TemporaryDirectory();
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, $"{id:D}.json"),
            "{ resultado inválido }");
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/runs/{id}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Execução persistida inválida", body);
        Assert.DoesNotContain(directory.Path, body);
    }

    private sealed class RunStoreApiFactory(string runsDirectory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<RunStore>();
                services.AddSingleton(new RunStore(runsDirectory));
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("promptbench-endpoint-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
