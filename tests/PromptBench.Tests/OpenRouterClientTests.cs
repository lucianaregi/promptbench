using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PromptBench.Api.OpenRouter;

namespace PromptBench.Tests;

public sealed class OpenRouterClientTests
{
    [Fact]
    public async Task SendsRequestedModelAndPrompt()
    {
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);

            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("provider/model", payload.RootElement.GetProperty("model").GetString());
            var message = Assert.Single(payload.RootElement.GetProperty("messages").EnumerateArray());
            Assert.Equal("user", message.GetProperty("role").GetString());
            Assert.Equal("Resuma este texto.", message.GetProperty("content").GetString());

            return JsonResponse(SuccessResponse);
        });
        var client = CreateClient(handler);

        await client.CompleteAsync("provider/model", "Resuma este texto.");
    }

    [Fact]
    public async Task ParsesOutputModelAndUsage()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessResponse)));
        var client = CreateClient(handler);

        var result = await client.CompleteAsync("provider/model", "Texto");

        Assert.Equal("Resumo produzido.", result.Output);
        Assert.Equal("provider/actual-model", result.Model);
        Assert.NotNull(result.Usage);
        Assert.Equal(12, result.Usage.PromptTokens);
        Assert.Equal(7, result.Usage.CompletionTokens);
        Assert.Equal(19, result.Usage.TotalTokens);
    }

    [Fact]
    public async Task MapsErrorResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{"error":{"message":"Model not found"}}""",
                HttpStatusCode.BadRequest)));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<OpenRouterException>(() =>
            client.CompleteAsync("invalid/model", "Texto"));

        Assert.Equal(OpenRouterFailureKind.InvalidRequest, exception.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, exception.UpstreamStatusCode);
        Assert.Equal("Model not found", exception.Message);
    }

    private static OpenRouterClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };

        return new OpenRouterClient(
            httpClient,
            Options.Create(new OpenRouterOptions { ApiKey = "test-key" }));
    }

    internal static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    internal const string SuccessResponse = """
        {
          "choices": [
            {
              "message": {
                "content": "Resumo produzido.",
                "role": "assistant"
              }
            }
          ],
          "model": "provider/actual-model",
          "usage": {
            "prompt_tokens": 12,
            "completion_tokens": 7,
            "total_tokens": 19
          }
        }
        """;
}
