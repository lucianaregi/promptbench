using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptBench.Api.OpenRouter;

namespace PromptBench.Tests;

internal sealed class OpenRouterApiFactory : WebApplicationFactory<Program>
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
                ["OpenRouter:ApiKey"] = "chave-de-teste"
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
