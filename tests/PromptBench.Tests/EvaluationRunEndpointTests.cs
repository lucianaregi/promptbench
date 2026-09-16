using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class EvaluationRunEndpointTests
{
    [Fact]
    public async Task ReturnsNotFoundForMissingEvaluationSet()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/does-not-exist/runs",
            new EvaluationRunRequest("provider/model"));

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
            prompts.Add(message.GetProperty("content").GetString()!);
            return OpenRouterClientTests.JsonResponse(OpenRouterClientTests.SuccessResponse);
        });
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model"));
        var result = await response.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("summarization-basic", result.Evaluation);
        Assert.Equal("provider/model", result.RequestedModel);
        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, item =>
        {
            Assert.Equal("Resumo produzido.", item.Output);
            Assert.Equal("provider/actual-model", item.UsedModel);
            Assert.Equal(19, item.Usage?.TotalTokens);
        });
        Assert.Equal(2, prompts.Count);
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
            new EvaluationRunRequest("provider/model"));
        var body = await result.Content.ReadFromJsonAsync<EvaluationRunResult>();

        Assert.NotNull(body);
        Assert.Collection(
            body.Results,
            first => Assert.Equal("biblioteca-aos-domingos", first.CaseId),
            second => Assert.Equal("previsao-do-tempo", second.CaseId));
    }

    [Fact]
    public async Task ReturnsServiceUnavailableWhenApiKeyIsMissing()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/runs",
            new EvaluationRunRequest("provider/model"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class OpenRouterApiFactory : WebApplicationFactory<Program>
    {
        private readonly HttpMessageHandler _handler;

        public OpenRouterApiFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenRouter:ApiKey"] = "test-key"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services
                    .AddHttpClient<OpenRouterClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _handler);
            });
        }
    }
}
