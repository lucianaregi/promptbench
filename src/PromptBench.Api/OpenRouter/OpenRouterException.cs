using System.Net;

namespace PromptBench.Api.OpenRouter;

public enum OpenRouterFailureKind
{
    Configuration,
    InvalidRequest,
    Authentication,
    RateLimit,
    Timeout,
    ServiceUnavailable,
    InvalidResponse
}

public sealed class OpenRouterException : Exception
{
    public OpenRouterException(
        OpenRouterFailureKind kind,
        string message,
        HttpStatusCode? upstreamStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        UpstreamStatusCode = upstreamStatusCode;
    }

    public OpenRouterFailureKind Kind { get; }

    public string Type => Kind switch
    {
        OpenRouterFailureKind.Configuration => "configuration",
        OpenRouterFailureKind.InvalidRequest => "invalid_request",
        OpenRouterFailureKind.Authentication => "authentication",
        OpenRouterFailureKind.RateLimit => "rate_limit",
        OpenRouterFailureKind.Timeout => "timeout",
        OpenRouterFailureKind.InvalidResponse => "invalid_response",
        _ => "service_unavailable"
    };

    public HttpStatusCode? UpstreamStatusCode { get; }
}
