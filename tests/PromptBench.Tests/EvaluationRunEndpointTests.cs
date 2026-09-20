using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class EvaluationRunEndpointTests
{
    [Fact]
    public async Task RequiresExplicitPromptIdentity()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new { model = "provider/model" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundForUnknownPromptVersion()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model", "summarization", "v99"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingEvaluationSet()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/does-not-exist/runs",
            new EvaluationRunRequest("provider/model", "summarization", "v1"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunsValidEvaluationSetSequentially()
    {
        var prompts = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var message = Assert.Single(payload.RootElement.GetProperty("messages").EnumerateArray());
            if (payload.RootElement.TryGetProperty("response_format", out var responseFormat))
            {
                return OpenRouterClientTests.JsonResponse(
                    """
                    {
                      "choices": [
                        {
                          "message": {
                            "content": "{\"passed\":true,\"reason\":\"O resumo preserva as informações principais.\"}",
                            "role": "assistant"
                          }
                        }
                      ],
                      "model": "qwen/qwen3.8-27b:free"
                    }
                    """);
            }

            prompts.Add(message.GetProperty("content").GetString()!);
            return OpenRouterClientTests.JsonResponse(OpenRouterClientTests.SuccessResponse);
        });
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model", "summarization", "v1"));
        var result = await response.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("summarization-basic", result.Evaluation);
        Assert.Equal("provider/model", result.RequestedModel);
        Assert.Equal("summarization", result.PromptName);
        Assert.Equal("v1", result.PromptVersion);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, item =>
        {
            Assert.Equal("Resumo produzido.", item.Output);
            Assert.Equal("provider/actual-model", item.UsedModel);
            Assert.Equal(19, item.Usage?.TotalTokens);
            Assert.Equal("completed", item.Evaluation?.Status);
            Assert.True(item.Evaluation?.Passed);
        });
        Assert.Equal(2, prompts.Count);
        Assert.All(prompts, prompt => Assert.StartsWith("Resuma o texto", prompt));
        Assert.Contains("biblioteca", prompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chuva", prompts[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseContainsResultsForEveryCase()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenRouterClientTests.JsonResponse(OpenRouterClientTests.SuccessResponse)));
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var result = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model", "summarization", "v1"));
        var body = await result.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.NotNull(body);
        Assert.Collection(
            body.Results,
            first => Assert.Equal("biblioteca-aos-domingos", first.CaseId),
            second => Assert.Equal("previsao-do-tempo", second.CaseId));
    }

    [Fact]
    public async Task PersistsCompletedRunAndReturnsItById()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(OpenRouterClientTests.JsonResponse(OpenRouterClientTests.SuccessResponse)));
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provedor/modelo", "summarization", "v1"));
        var created = await response.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(created);
        var storedResponse = await client.GetAsync($"/runs/{created.Id}");
        var stored = await storedResponse.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.Equal(HttpStatusCode.OK, storedResponse.StatusCode);
        Assert.NotNull(stored);
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal("summarization", stored.PromptName);
        Assert.Equal("v1", stored.PromptVersion);
        Assert.Equal(created.Results, stored.Results);
    }

    [Fact]
    public async Task ReturnsServiceUnavailableWhenApiKeyIsMissing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("O OpenRouter não deve ser chamado sem chave."));
        await using var factory = new OpenRouterApiFactory(handler, apiKey: "");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model", "summarization", "v1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

}
