using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PromptBench.Api.OpenRouter;

public sealed class OpenRouterClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;

    public OpenRouterClient(HttpClient httpClient, IOptions<OpenRouterOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<OpenRouterCompletion> CompleteAsync(
        string model,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new OpenRouterException(
                OpenRouterFailureKind.Configuration,
                "A chave da API do OpenRouter não está configurada.");
        }

        var payload = new ChatCompletionRequest(
            model,
            [new ChatMessage("user", prompt)]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpenRouterException(
                OpenRouterFailureKind.Timeout,
                "A chamada ao OpenRouter excedeu o tempo limite.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OpenRouterException(
                OpenRouterFailureKind.ServiceUnavailable,
                "Não foi possível acessar o OpenRouter.",
                innerException: exception);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode);
            }

            ChatCompletionResponse? completion;

            try
            {
                completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw InvalidResponse(exception);
            }

            var output = completion?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(output))
            {
                throw InvalidResponse();
            }

            var usage = completion?.Usage is null
                ? null
                : new TokenUsage(
                    completion.Usage.PromptTokens,
                    completion.Usage.CompletionTokens,
                    completion.Usage.TotalTokens);

            return new OpenRouterCompletion(output, completion?.Model, usage);
        }
    }

    private static OpenRouterException CreateApiException(HttpStatusCode statusCode)
    {
        var kind = statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity =>
                OpenRouterFailureKind.InvalidRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                OpenRouterFailureKind.Authentication,
            HttpStatusCode.TooManyRequests => OpenRouterFailureKind.RateLimit,
            HttpStatusCode.RequestTimeout => OpenRouterFailureKind.Timeout,
            _ => OpenRouterFailureKind.ServiceUnavailable
        };

        var message = kind switch
        {
            OpenRouterFailureKind.InvalidRequest =>
                "O OpenRouter rejeitou o modelo ou a requisição informada.",
            OpenRouterFailureKind.Authentication => "O OpenRouter rejeitou as credenciais configuradas.",
            OpenRouterFailureKind.RateLimit =>
                "O limite de requisições do OpenRouter foi atingido.",
            OpenRouterFailureKind.Timeout => "O OpenRouter não respondeu dentro do tempo esperado.",
            _ => "O OpenRouter retornou um erro ao processar a requisição."
        };

        return new OpenRouterException(kind, message, statusCode);
    }

    private static OpenRouterException InvalidResponse(Exception? innerException = null) =>
        new(
            OpenRouterFailureKind.InvalidResponse,
            "O OpenRouter retornou uma resposta inesperada.",
            innerException: innerException);

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionResponse(
        string? Model,
        IReadOnlyList<Choice>? Choices,
        UsageResponse? Usage);

    private sealed record Choice(MessageResponse? Message);

    private sealed record MessageResponse(string? Content);

    private sealed record UsageResponse(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

}
