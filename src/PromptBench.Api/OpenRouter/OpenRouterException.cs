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

    public HttpStatusCode? UpstreamStatusCode { get; }
}
