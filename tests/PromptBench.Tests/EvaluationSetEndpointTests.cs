using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PromptBench.Api.Evals;

namespace PromptBench.Tests;

public sealed class EvaluationSetEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EvaluationSetEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEvalsReturnsAvailableEvaluationSets()
    {
        var summaries = await _client.GetFromJsonAsync<List<EvaluationSetSummary>>("/evals");

        var summary = Assert.Single(summaries!);
        Assert.Equal("summarization-basic", summary.Name);
        Assert.Equal(2, summary.CaseCount);
    }

    [Fact]
    public async Task GetEvalReturnsExistingEvaluationSet()
    {
        var response = await _client.GetAsync("/evals/summarization-basic");
        var evaluationSet = await response.Content.ReadFromJsonAsync<EvaluationSet>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(evaluationSet);
        Assert.Equal("summarization-basic", evaluationSet.Name);
        Assert.Equal(2, evaluationSet.Cases.Count);
    }

    [Fact]
    public async Task GetEvalReturnsNotFoundForMissingEvaluationSet()
    {
        var response = await _client.GetAsync("/evals/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
