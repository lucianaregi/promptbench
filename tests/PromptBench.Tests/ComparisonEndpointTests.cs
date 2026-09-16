using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PromptBench.Api.Comparisons;

namespace PromptBench.Tests;

public sealed class ComparisonEndpointTests
{
    public static TheoryData<IReadOnlyList<string>> InvalidModelLists => new()
    {
        Array.Empty<string>(),
        new[] { "provedor/modelo-a" },
        new[] { "provedor/modelo-a", "PROVEDOR/MODELO-A" }
    };

    [Theory]
    [MemberData(nameof(InvalidModelLists))]
    public async Task RejectsInvalidModelLists(IReadOnlyList<string> models)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/comparisons",
            new ComparisonRequest(models));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundForMissingEvaluationSet()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/inexistente/comparisons",
            new ComparisonRequest(["provedor/modelo-a", "provedor/modelo-b"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RunsModelsSequentiallyWithTheSameCasesAndAggregatesTokens()
    {
        var requests = new List<(string Model, string Prompt)>();
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var model = payload.RootElement.GetProperty("model").GetString()!;
            var message = Assert.Single(payload.RootElement.GetProperty("messages").EnumerateArray());
            var prompt = message.GetProperty("content").GetString()!;
            requests.Add((model, prompt));

            var output = model.EndsWith("modelo-a", StringComparison.Ordinal)
                ? "Resumo produzido pelo modelo A."
                : "Resumo produzido pelo modelo B.";
            return JsonResponse(output, $"{model}-real", 12, 7, 19);
        });
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/comparisons",
            new ComparisonRequest(["provedor/modelo-a", "provedor/modelo-b"]));
        var comparison = await response.Content.ReadFromJsonAsync<ComparisonResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(comparison);
        Assert.Equal("completed", comparison.Status);
        Assert.Collection(
            comparison.Runs,
            first =>
            {
                Assert.Equal("provedor/modelo-a", first.RequestedModel);
                Assert.Equal("provedor/modelo-a-real", first.ActualModel);
                Assert.Equal("Resumo produzido pelo modelo A.", first.Results[0].Output);
                Assert.Equal(24, first.PromptTokens);
                Assert.Equal(14, first.CompletionTokens);
                Assert.Equal(38, first.TotalTokens);
            },
            second =>
            {
                Assert.Equal("provedor/modelo-b", second.RequestedModel);
                Assert.Equal("provedor/modelo-b-real", second.ActualModel);
                Assert.Equal("Resumo produzido pelo modelo B.", second.Results[0].Output);
                Assert.Equal(24, second.PromptTokens);
                Assert.Equal(14, second.CompletionTokens);
                Assert.Equal(38, second.TotalTokens);
            });

        Assert.Equal(
            ["provedor/modelo-a", "provedor/modelo-a", "provedor/modelo-b", "provedor/modelo-b"],
            requests.Select(item => item.Model));
        Assert.Equal(requests[0].Prompt, requests[2].Prompt);
        Assert.Equal(requests[1].Prompt, requests[3].Prompt);
        Assert.Contains("biblioteca", requests[0].Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chuva", requests[1].Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreservesSuccessfulRunWhenAnotherModelHitsRateLimitWithoutRetry()
    {
        var requestCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var model = payload.RootElement.GetProperty("model").GetString()!;
            requestCounts[model] = requestCounts.GetValueOrDefault(model) + 1;

            if (model is "provedor/modelo-limitado")
            {
                return JsonResponse(
                    "O limite de requisições foi atingido.",
                    HttpStatusCode.TooManyRequests);
            }

            return JsonResponse("Resumo concluído.", "provedor/modelo-disponivel-real", 10, 5, 15);
        });
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/comparisons",
            new ComparisonRequest(["provedor/modelo-limitado", "provedor/modelo-disponivel"]));
        var comparison = await response.Content.ReadFromJsonAsync<ComparisonResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(comparison);
        Assert.Equal("partial", comparison.Status);
        var failedRun = Assert.Single(comparison.Runs, run => run.Status is "failed");
        Assert.Equal("rate_limit", failedRun.Error?.Type);
        Assert.Contains("limite", failedRun.Error?.Message, StringComparison.OrdinalIgnoreCase);
        var completedRun = Assert.Single(comparison.Runs, run => run.Status is "completed");
        Assert.Equal(2, completedRun.Results.Count);
        Assert.Equal("Resumo concluído.", completedRun.Results[0].Output);
        Assert.Equal(1, requestCounts["provedor/modelo-limitado"]);
        Assert.Equal(2, requestCounts["provedor/modelo-disponivel"]);
    }

    [Fact]
    public async Task ReportsFailedWhenEveryModelFails()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            JsonResponse("Serviço temporariamente indisponível.", HttpStatusCode.ServiceUnavailable)));
        await using var factory = new OpenRouterApiFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/evals/summarization-basic/comparisons",
            new ComparisonRequest(["provedor/modelo-a", "provedor/modelo-b"]));
        var comparison = await response.Content.ReadFromJsonAsync<ComparisonResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(comparison);
        Assert.Equal("failed", comparison.Status);
        Assert.All(comparison.Runs, run => Assert.Equal("failed", run.Status));
    }

    private static HttpResponseMessage JsonResponse(
        string output,
        string actualModel,
        int promptTokens,
        int completionTokens,
        int totalTokens) =>
        OpenRouterClientTests.JsonResponse($$"""
            {
              "choices": [
                {
                  "message": {
                    "content": "{{output}}",
                    "role": "assistant"
                  }
                }
              ],
              "model": "{{actualModel}}",
              "usage": {
                "prompt_tokens": {{promptTokens}},
                "completion_tokens": {{completionTokens}},
                "total_tokens": {{totalTokens}}
              }
            }
            """);

    private static HttpResponseMessage JsonResponse(string message, HttpStatusCode statusCode) =>
        OpenRouterClientTests.JsonResponse(
            JsonSerializer.Serialize(new { error = new { message } }),
            statusCode);
}
