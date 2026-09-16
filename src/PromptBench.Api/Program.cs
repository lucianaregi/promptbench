using PromptBench.Api.Comparisons;
using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new EvaluationSetLoader(
    Path.Combine(AppContext.BaseDirectory, "evals")));
builder.Services.AddSingleton(new RunStore(
    Path.Combine(AppContext.BaseDirectory, "runs")));
builder.Services.Configure<OpenRouterOptions>(
    builder.Configuration.GetSection(OpenRouterOptions.SectionName));
builder.Services.AddHttpClient<OpenRouterClient>(client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddTransient<EvaluationRunner>();
builder.Services.AddTransient<LlmJudgeEvaluator>();
builder.Services.AddTransient<ComparisonRunner>();

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
        RunStore runStore,
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
            await runStore.SaveAsync(result, cancellationToken);
            return Results.Ok(result);
        }
        catch (OpenRouterException exception)
        {
            return MapOpenRouterError(exception);
        }
        catch (RunStorageException exception)
        {
            return MapRunStorageError(exception);
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

app.MapPost("/evals/{name}/comparisons", async (
        string name,
        ComparisonRequest? request,
        EvaluationSetLoader loader,
        ComparisonRunner runner,
        RunStore runStore,
        CancellationToken cancellationToken) =>
    {
        var validationError = ValidateModels(request?.Models, out var models);

        if (validationError is not null)
        {
            return Results.Problem(
                title: "Requisição inválida",
                detail: validationError,
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

        var result = await runner.RunAsync(
            loadResult.EvaluationSet!,
            models,
            cancellationToken);

        try
        {
            await runStore.SaveAsync(result, cancellationToken);
            return Results.Ok(result);
        }
        catch (RunStorageException exception)
        {
            return MapRunStorageError(exception);
        }
    })
    .WithName("CompareEvaluationSetModels")
    .WithDescription("Executa o mesmo evaluation set sequencialmente em pelo menos dois modelos distintos do OpenRouter.")
    .Produces<ComparisonResult>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

app.MapGet("/runs", async (RunStore runStore, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await runStore.ListAsync(cancellationToken));
        }
        catch (RunStorageException exception)
        {
            return Results.Problem(
                title: "Falha ao listar execuções",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    })
    .WithName("ListPersistedRuns")
    .WithDescription("Lista resumos das execuções e comparações persistidas, da mais recente para a mais antiga.")
    .Produces<IReadOnlyList<RunSummary>>()
    .ProducesProblem(StatusCodes.Status500InternalServerError);
app.MapGet("/runs/{id:guid}", async (
        Guid id,
        RunStore runStore,
        CancellationToken cancellationToken) =>
    {
        var result = await runStore.LoadAsync(id, cancellationToken);

        return result.Status switch
        {
            RunLoadStatus.Success => Results.Json(result.Result),
            RunLoadStatus.NotFound => Results.NotFound(),
            RunLoadStatus.Invalid => Results.Problem(
                title: "Execução persistida inválida",
                detail: "O resultado armazenado não contém um JSON válido.",
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Problem(
                title: "Falha ao consultar execução",
                detail: "Não foi possível ler o resultado armazenado.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    })
    .WithName("GetPersistedRun")
    .WithDescription("Retorna uma execução ou comparação persistida pelo identificador.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);
app.Run();

static string? ValidateModels(IReadOnlyList<string>? requestedModels, out IReadOnlyList<string> models)
{
    models = [];

    if (requestedModels is null || requestedModels.Count is 0)
    {
        return "O campo 'models' deve conter pelo menos dois modelos distintos.";
    }

    if (requestedModels.Any(string.IsNullOrWhiteSpace))
    {
        return "Os identificadores dos modelos não podem ser vazios.";
    }

    var normalizedModels = requestedModels.Select(model => model.Trim()).ToArray();

    if (normalizedModels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedModels.Length)
    {
        return "A lista de modelos não pode conter identificadores duplicados.";
    }

    if (normalizedModels.Length < 2)
    {
        return "O campo 'models' deve conter pelo menos dois modelos distintos.";
    }

    models = normalizedModels;
    return null;
}


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

static IResult MapRunStorageError(RunStorageException exception) =>
    Results.Problem(
        title: "Falha ao persistir execução",
        detail: exception.Message,
        statusCode: StatusCodes.Status500InternalServerError);
public partial class Program;
