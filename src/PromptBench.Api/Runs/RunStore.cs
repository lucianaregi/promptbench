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
        try
        {
            Directory.CreateDirectory(_runsDirectory);
            var path = GetPath(result.Id);

            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await JsonSerializer.SerializeAsync(stream, result, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RunStorageException(
                "Não foi possível persistir o resultado da execução.",
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
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object ||
                !root.TryGetProperty("id", out var storedId) ||
                storedId.ValueKind is not JsonValueKind.String ||
                !Guid.TryParse(storedId.GetString(), out var parsedId) ||
                parsedId != id)
            {
                return RunLoadResult.Invalid();
            }

            return RunLoadResult.Success(root.Clone());
        }
        catch (JsonException)
        {
            return RunLoadResult.Invalid();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RunLoadResult.Unavailable();
        }
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
