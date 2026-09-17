using System.Text.Json;

namespace PromptBench.Api.Runs;

public sealed class RunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _runsDirectory;

    public RunStore(string runsDirectory)
    {
        _runsDirectory = runsDirectory;
    }

    public async Task SaveAsync<T>(T result, CancellationToken cancellationToken = default)
        where T : IPersistedRunResult
    {
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(_runsDirectory);
            var path = GetPath(result.Id);
            temporaryPath = Path.Combine(
                _runsDirectory,
                $".{result.Id:D}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    result,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path);
            temporaryPath = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RunStorageException(
                "Não foi possível persistir o resultado da execução.",
                exception);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static void DeleteTemporaryFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A falha principal não deve ser ocultada por uma falha de limpeza.
        }
    }

    public async Task<IReadOnlyList<RunSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_runsDirectory))
        {
            return [];
        }

        try
        {
            var summaries = new List<RunSummary>();

            foreach (var path in Directory.EnumerateFiles(
                         _runsDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                var root = await ReadJsonAsync(path, cancellationToken);

                if (root is { } json && TryCreateSummary(json, out var summary))
                {
                    summaries.Add(summary);
                }
            }

            return summaries
                .OrderByDescending(summary => summary.StartedAt)
                .ThenBy(summary => summary.Id)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RunStorageException(
                "Não foi possível listar os resultados persistidos.",
                exception);
        }
    }

    public async Task<RunLoadResult> LoadAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);

        if (!File.Exists(path))
        {
            return RunLoadResult.NotFound();
        }

        try
        {
            var root = await ReadJsonAsync(path, cancellationToken);

            if (root is null ||
                root.Value.ValueKind is not JsonValueKind.Object ||
                !root.Value.TryGetProperty("id", out var storedId) ||
                storedId.ValueKind is not JsonValueKind.String ||
                !Guid.TryParse(storedId.GetString(), out var parsedId) ||
                parsedId != id)
            {
                return RunLoadResult.Invalid();
            }

            return RunLoadResult.Success(root.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RunLoadResult.Unavailable();
        }
    }

    private static async Task<JsonElement?> ReadJsonAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryCreateSummary(JsonElement root, out RunSummary summary)
    {
        summary = null!;

        if (root.ValueKind is not JsonValueKind.Object ||
            !TryGetGuid(root, "id", out var id) ||
            !TryGetString(root, "type", out var type) ||
            !TryGetString(root, "evaluation", out var evaluation) ||
            !TryGetDateTimeOffset(root, "startedAt", out var startedAt) ||
            !TryGetModels(root, type, out var models) ||
            !TryGetStatus(root, type, out var status))
        {
            return false;
        }

        summary = new RunSummary(
            id,
            type,
            evaluation,
            startedAt,
            models,
            status,
            CalculatePassRate(root, type));
        return true;
    }

    private static bool TryGetModels(
        JsonElement root,
        string type,
        out IReadOnlyList<string> models)
    {
        models = [];

        if (type is "evaluation_run" && TryGetString(root, "requestedModel", out var model))
        {
            models = [model];
            return true;
        }

        if (type is not "comparison" ||
            !root.TryGetProperty("runs", out var runs) ||
            runs.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        var collectedModels = new List<string>();

        foreach (var run in runs.EnumerateArray())
        {
            if (!TryGetString(run, "requestedModel", out var requestedModel))
            {
                return false;
            }

            if (!collectedModels.Contains(requestedModel, StringComparer.OrdinalIgnoreCase))
            {
                collectedModels.Add(requestedModel);
            }
        }

        models = collectedModels;
        return models.Count > 0;
    }

    private static bool TryGetStatus(JsonElement root, string type, out string status)
    {
        status = string.Empty;

        if (type is "evaluation_run")
        {
            status = "completed";
            return true;
        }

        return type is "comparison" && TryGetString(root, "status", out status);
    }

    private static double? CalculatePassRate(JsonElement root, string type)
    {
        var passed = 0;
        var evaluated = 0;

        if (type is "evaluation_run")
        {
            CountEvaluations(root, ref passed, ref evaluated);
        }
        else if (root.TryGetProperty("runs", out var runs) && runs.ValueKind is JsonValueKind.Array)
        {
            foreach (var run in runs.EnumerateArray())
            {
                CountEvaluations(run, ref passed, ref evaluated);
            }
        }

        return evaluated is 0
            ? null
            : Math.Round(passed * 100d / evaluated, 2);
    }

    private static void CountEvaluations(JsonElement container, ref int passed, ref int evaluated)
    {
        if (!container.TryGetProperty("results", out var results) ||
            results.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("evaluation", out var evaluation) ||
                evaluation.ValueKind is not JsonValueKind.Object ||
                !TryGetString(evaluation, "status", out var status) ||
                status is not "completed" ||
                !evaluation.TryGetProperty("passed", out var passedValue) ||
                passedValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                continue;
            }

            evaluated++;

            if (passedValue.GetBoolean())
            {
                passed++;
            }
        }
    }

    private static bool TryGetGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryGetDateTimeOffset(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private string GetPath(Guid id) =>
        Path.Combine(_runsDirectory, $"{id:D}.json");
}

public enum RunLoadStatus
{
    Success,
    NotFound,
    Invalid,
    Unavailable
}

public sealed record RunLoadResult(RunLoadStatus Status, JsonElement? Result)
{
    public static RunLoadResult Success(JsonElement result) =>
        new(RunLoadStatus.Success, result);

    public static RunLoadResult NotFound() =>
        new(RunLoadStatus.NotFound, null);

    public static RunLoadResult Invalid() =>
        new(RunLoadStatus.Invalid, null);

    public static RunLoadResult Unavailable() =>
        new(RunLoadStatus.Unavailable, null);
}

public sealed class RunStorageException : Exception
{
    public RunStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
