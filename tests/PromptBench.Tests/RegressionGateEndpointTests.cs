using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class RegressionGateEndpointTests
{
    [Fact]
    public async Task PassesWhenThereAreNoRegressionsIncludingAddedAndRemovedCases()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("sem-alteracao", true),
            ("removido", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("sem-alteracao", true),
            ("adicionado", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{candidate.Id}/gate");
        var gate = await response.Content.ReadFromJsonAsync<RegressionGateResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(gate);
        Assert.True(gate.Passed);
        Assert.Equal(0, gate.Regressions);
        Assert.Null(gate.RegressionCaseIds);
    }

    [Fact]
    public async Task ReturnsConflictAndRegressionCaseIdsWhenGateFails()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            300,
            20,
            ("biblioteca-aos-domingos", true),
            ("previsao-do-tempo", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(),
            "summarization-basic",
            350,
            22,
            ("biblioteca-aos-domingos", false),
            ("previsao-do-tempo", false));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{candidate.Id}/gate");
        var gate = await response.Content.ReadFromJsonAsync<RegressionGateResult>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(gate);
        Assert.False(gate.Passed);
        Assert.Equal(2, gate.Regressions);
        Assert.Equal(
            ["biblioteca-aos-domingos", "previsao-do-tempo"],
            gate.RegressionCaseIds);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingRun()
    {
        using var directory = new TemporaryDirectory();
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{Guid.NewGuid()}/compare/{Guid.NewGuid()}/gate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRunsFromDifferentEvaluationSets()
    {
        using var directory = new TemporaryDirectory();
        var store = new RunStore(directory.Path);
        var baseline = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(), "resumos", 300, 20, ("caso", true));
        var candidate = PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(), "classificacao", 350, 22, ("caso", true));
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
        await using var factory = new RunStoreApiFactory(directory.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/runs/{baseline.Id}/compare/{candidate.Id}/gate");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("mesmo Evaluation Set", body);
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
            Path = Directory.CreateTempSubdirectory("promptbench-gate-endpoint-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
