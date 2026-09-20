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
    public async Task ComparesTwoPersistedRuns()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("biblioteca-aos-domingos", true)) with
        {
            PromptName = "summarization",
            PromptVersion = "v1"
        };
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("biblioteca-aos-domingos", false)) with
        {
            PromptName = "summarization",
            PromptVersion = "v2"
        };
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/runs/{baseline.Id}/compare/{candidate.Id}");
        var comparison = await response.Content.ReadFromJsonAsync<PersistedRunComparisonResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(comparison);
        Assert.Equal(1, comparison.Summary.Regressions);
        Assert.Equal("regression", Assert.Single(comparison.Cases).Status);
        Assert.Equal("summarization", comparison.BaselinePromptName);
        Assert.Equal("v1", comparison.BaselinePromptVersion);
        Assert.Equal("summarization", comparison.CandidatePromptName);
        Assert.Equal("v2", comparison.CandidatePromptVersion);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenComparisonBaselineDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("biblioteca-aos-domingos", true));
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/runs/{Guid.NewGuid()}/compare/{candidate.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("referência não encontrada", body);
    }

    [Fact]
    public async Task GeneratesAndSavesMarkdownComparisonReport()
    {
        using var directory = new TemporaryDirectory();
        var reportsDirectory = System.IO.Path.Combine(directory.Path, "reports");
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("biblioteca-aos-domingos", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("biblioteca-aos-domingos", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path, reportsDirectory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{candidate.Id}/report");
        var markdown = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("# Relatório de comparação de execuções", markdown);
        Assert.Contains(baseline.Id.ToString(), markdown);
        Assert.Contains(candidate.Id.ToString(), markdown);
        Assert.Contains("provedor/modelo-real", markdown);
        Assert.Contains("| biblioteca-aos-domingos | Passou | Falhou | Regressão |", markdown);
        var reportPath = System.IO.Path.Combine(
            reportsDirectory,
            $"{baseline.Id:D}_vs_{candidate.Id:D}.md");
        Assert.True(File.Exists(reportPath));
        Assert.Equal(markdown, await File.ReadAllTextAsync(reportPath));

        var duplicateResponse = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{candidate.Id}/report");

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(markdown, await File.ReadAllTextAsync(reportPath));
    }

    [Fact]
    public async Task ReturnsNotFoundWhenReportBaselineDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{Guid.NewGuid()}/compare/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("referência não encontrada", body);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenReportCandidateDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("biblioteca-aos-domingos", true));
        await store.SaveAsync(baseline);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("candidata não encontrada", body);
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

    private sealed class RunStoreApiFactory(
        string runsDirectory,
        string? reportsDirectory = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<RunStore>();
                services.AddSingleton(new RunStore(runsDirectory));
                services.RemoveAll<ReportStore>();
                services.AddSingleton(new ReportStore(
                    reportsDirectory ?? System.IO.Path.Combine(runsDirectory, "reports")));
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
