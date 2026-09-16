using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PromptBench.Api.Comparisons;
using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class LlmJudgeEvaluatorTests
{
    [Fact]
    public async Task ApprovesResponseAndSendsDelimitedDataWithStructuredOutput()
    {
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("provedor/judge:free", payload.RootElement.GetProperty("model").GetString());
            Assert.Equal("json_schema", payload.RootElement.GetProperty("response_format").GetProperty("type").GetString());
            var prompt = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Assert.Contains("<dados_da_avaliacao>", prompt);
            Assert.Contains("A biblioteca", prompt);
            Assert.Contains("A biblioteca passou", prompt);
            Assert.Contains("A biblioteca agora", prompt);
            Assert.Contains("preservar as informações", prompt);
            Assert.Contains("português brasileiro", prompt);
            return CompletionResponse(
                """{"passed":true,"reason":"O resumo preserva a informação principal."}""",
                "provedor/judge-real",
                40,
                12,
                52);
        });
        var evaluator = CreateEvaluator(handler);

        var result = await evaluator.EvaluateAsync(Configuration, Case, "A biblioteca agora abre aos domingos.");

        Assert.Equal("completed", result.Status);
        Assert.True(result.Passed);
        Assert.Equal("O resumo preserva a informação principal.", result.Reason);
        Assert.Equal("provedor/judge:free", result.RequestedModel);
        Assert.Equal("provedor/judge-real", result.UsedModel);
        Assert.Equal(52, result.Usage?.TotalTokens);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task RejectsResponseWithoutReportingJudgeFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            CompletionResponse(
                """{"passed":false,"reason":"O resumo acrescenta uma informação inexistente."}""",
                "provedor/judge:free")));
        var evaluator = CreateEvaluator(handler);

        var result = await evaluator.EvaluateAsync(Configuration, Case, "A biblioteca fecha aos domingos.");

        Assert.Equal("completed", result.Status);
        Assert.False(result.Passed);
        Assert.Equal("O resumo acrescenta uma informação inexistente.", result.Reason);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("não é JSON")]
    [InlineData("{\"reason\":\"Falta o resultado.\"}")]
    [InlineData("{\"passed\":true,\"reason\":\"\"}")]
    public async Task ReportsInvalidStructuredResponseAsJudgeFailure(string output)
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            CompletionResponse(output, "provedor/judge:free")));
        var evaluator = CreateEvaluator(handler);

        var result = await evaluator.EvaluateAsync(Configuration, Case, "Resposta produzida.");

        Assert.Equal("failed", result.Status);
        Assert.Null(result.Passed);
        Assert.Null(result.Reason);
        Assert.Equal("invalid_response", result.Error?.Type);
        Assert.Contains("inválida", result.Error?.Message);
    }

    [Fact]
    public async Task ReportsOpenRouterErrorAsJudgeFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            OpenRouterClientTests.JsonResponse(
                """{"error":{"message":"O limite de requisições foi atingido."}}""",
                HttpStatusCode.TooManyRequests)));
        var evaluator = CreateEvaluator(handler);

        var result = await evaluator.EvaluateAsync(Configuration, Case, "Resposta produzida.");

        Assert.Equal("failed", result.Status);
        Assert.Null(result.Passed);
        Assert.Equal("rate_limit", result.Error?.Type);
    }

    [Fact]
    public async Task IntegratesJudgeWithRunsAndComparisonsUsingSeparateCalls()
    {
        var requestedModels = new List<string>();
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var model = payload.RootElement.GetProperty("model").GetString()!;
            requestedModels.Add(model);

            return model == "provedor/judge:free"
                ? CompletionResponse(
                    """{"passed":true,"reason":"A resposta atende ao critério informado."}""",
                    "provedor/judge-real",
                    30,
                    8,
                    38)
                : CompletionResponse(
                    $"Resumo produzido por {model}.",
                    $"{model}-real",
                    12,
                    7,
                    19);
        });
        var openRouterClient = CreateClient(handler);
        var evaluator = new LlmJudgeEvaluator(openRouterClient);
        var runner = new EvaluationRunner(openRouterClient, evaluator);
        var comparisonRunner = new ComparisonRunner(runner);
        var evaluationSet = new EvaluationSet
        {
            Name = "resumo-com-judge",
            Description = "Avaliação de resumo com judge.",
            Evaluation = Configuration,
            Cases = [Case]
        };

        var run = await runner.RunAsync(evaluationSet, "provedor/modelo-a");
        var comparison = await comparisonRunner.RunAsync(
            evaluationSet,
            ["provedor/modelo-a", "provedor/modelo-b"]);

        Assert.True(Assert.Single(run.Results).Evaluation?.Passed);
        Assert.Equal("completed", comparison.Status);
        Assert.All(comparison.Runs, item => Assert.True(Assert.Single(item.Results).Evaluation?.Passed));
        Assert.Equal(
            [
                "provedor/modelo-a", "provedor/judge:free",
                "provedor/modelo-a", "provedor/judge:free",
                "provedor/modelo-b", "provedor/judge:free"
            ],
            requestedModels);
    }

    private static readonly EvaluationConfiguration Configuration = new()
    {
        Type = "llm_judge",
        JudgeModel = "provedor/judge:free",
        Criteria = "O resumo deve preservar as informações principais."
    };

    private static readonly EvaluationCase Case = new()
    {
        Id = "biblioteca-aos-domingos",
        Input = "A biblioteca do bairro agora também abre aos domingos.",
        Expected = "A biblioteca passou a abrir aos domingos."
    };

    private static LlmJudgeEvaluator CreateEvaluator(HttpMessageHandler handler) =>
        new(CreateClient(handler));

    private static OpenRouterClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") },
            Options.Create(new OpenRouterOptions { ApiKey = "chave-de-teste" }));

    private static HttpResponseMessage CompletionResponse(
        string output,
        string model,
        int promptTokens = 10,
        int completionTokens = 5,
        int totalTokens = 15)
    {
        var json = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = output, role = "assistant" } } },
            model,
            usage = new
            {
                prompt_tokens = promptTokens,
                completion_tokens = completionTokens,
                total_tokens = totalTokens
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
