using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new EvaluationSetLoader(
    Path.Combine(AppContext.BaseDirectory, "evals")));
builder.Services.Configure<OpenRouterOptions>(
    builder.Configuration.GetSection(OpenRouterOptions.SectionName));
builder.Services.AddHttpClient<OpenRouterClient>(client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddTransient<EvaluationRunner>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PromptBench API v1");
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth")
    .WithDescription("Confirma que a API está funcionando.");

app.MapGet("/evals", async (EvaluationSetLoader loader, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await loader.ListAsync(cancellationToken));
        }
        catch (InvalidDataException)
        {
            return Results.Problem(
                title: "Evaluation sets inválidos",
                detail: "Um ou mais evaluation sets não puderam ser carregados.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    })
    .WithName("ListEvaluationSets")
    .WithDescription("Lista os evaluation sets disponíveis sem incluir seus casos.")
    .Produces<IReadOnlyList<EvaluationSetSummary>>()
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.MapGet("/evals/{name}", async (
        string name,
        EvaluationSetLoader loader,
        CancellationToken cancellationToken) =>
    {
        var result = await loader.LoadAsync(name, cancellationToken);

        return result.Status switch
        {
            EvaluationSetLoadStatus.Success => Results.Ok(result.EvaluationSet),
            EvaluationSetLoadStatus.NotFound => Results.NotFound(),
            _ => Results.Problem(
                title: "Evaluation set inválido",
                detail: "O evaluation set existe, mas não pôde ser carregado.",
                statusCode: StatusCodes.Status422UnprocessableEntity)
        };
    })
    .WithName("GetEvaluationSet")
    .WithDescription("Retorna um evaluation set completo pelo nome do arquivo.")
    .Produces<EvaluationSet>()
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.MapPost("/evals/{name}/runs", async (
        string name,
        EvaluationRunRequest? request,
        EvaluationSetLoader loader,
        EvaluationRunner runner,
        CancellationToken cancellationToken) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Model))
        {
            return Results.Problem(
                title: "Requisição inválida",
                detail: "O campo 'model' é obrigatório.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var loadResult = await loader.LoadAsync(name, cancellationToken);

        if (loadResult.Status is EvaluationSetLoadStatus.NotFound)
        {
            return Results.NotFound();
        }

        if (loadResult.Status is EvaluationSetLoadStatus.Invalid)
        {
            return Results.Problem(
                title: "Evaluation set inválido",
                detail: "O evaluation set existe, mas não pôde ser carregado.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        try
        {
            var result = await runner.RunAsync(
                loadResult.EvaluationSet!,
                request.Model,
                cancellationToken);
            return Results.Ok(result);
        }
        catch (OpenRouterException exception)
        {
            return MapOpenRouterError(exception);
        }
    })
    .WithName("RunEvaluationSet")
    .WithDescription("Executa sequencialmente os casos de um evaluation set no modelo OpenRouter informado.")
    .Produces<EvaluationRunResult>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
    .ProducesProblem(StatusCodes.Status429TooManyRequests)
    .ProducesProblem(StatusCodes.Status502BadGateway)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status504GatewayTimeout);

app.Run();

static IResult MapOpenRouterError(OpenRouterException exception)
{
    var (statusCode, title) = exception.Kind switch
    {
        OpenRouterFailureKind.Configuration =>
            (StatusCodes.Status503ServiceUnavailable, "OpenRouter não configurado"),
        OpenRouterFailureKind.InvalidRequest =>
            (StatusCodes.Status400BadRequest, "Requisição rejeitada pelo OpenRouter"),
        OpenRouterFailureKind.Authentication =>
            (StatusCodes.Status502BadGateway, "Falha de autenticação no OpenRouter"),
        OpenRouterFailureKind.RateLimit =>
            (StatusCodes.Status429TooManyRequests, "Limite do OpenRouter atingido"),
        OpenRouterFailureKind.Timeout =>
            (StatusCodes.Status504GatewayTimeout, "Tempo limite do OpenRouter excedido"),
        OpenRouterFailureKind.InvalidResponse =>
            (StatusCodes.Status502BadGateway, "Resposta inválida do OpenRouter"),
        _ => (StatusCodes.Status502BadGateway, "Falha no OpenRouter")
    };

    return Results.Problem(
        title: title,
        detail: exception.Message,
        statusCode: statusCode);
}

public partial class Program;