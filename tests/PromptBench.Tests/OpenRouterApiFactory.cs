using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Tests;

internal sealed class OpenRouterApiFactory : WebApplicationFactory<Program>
{
    private readonly HttpMessageHandler _handler;
    private readonly string? _apiKey;
    private readonly string _runsDirectory =
        Directory.CreateTempSubdirectory("promptbench-runs-tests-").FullName;

    public OpenRouterApiFactory(HttpMessageHandler handler, string? apiKey = "chave-de-teste")
    {
        _handler = handler;
        _apiKey = apiKey;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = _apiKey
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<RunStore>();
            services.AddSingleton(new RunStore(_runsDirectory));
            services
                .AddHttpClient<OpenRouterClient>()
                .ConfigurePrimaryHttpMessageHandler(() => _handler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_runsDirectory))
        {
            Directory.Delete(_runsDirectory, recursive: true);
        }
    }
}
