using PromptBench.Api.Evals;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new EvaluationSetLoader(
    Path.Combine(AppContext.BaseDirectory, "evals")));

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

app.Run();

public partial class Program;
