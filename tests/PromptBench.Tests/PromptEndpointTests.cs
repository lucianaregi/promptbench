using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PromptBench.Api.Prompts;

namespace PromptBench.Tests;

public sealed class PromptEndpointTests
{
    [Fact]
    public async Task ListsAvailablePrompts()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/prompts");
        var prompts = await response.Content.ReadFromJsonAsync<PromptDefinition[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = Assert.Single(prompts!);
        Assert.Equal("summarization", prompt.Name);
        Assert.Equal("v1", prompt.Version);
        Assert.Contains("{{input}}", prompt.Template);
    }
}
