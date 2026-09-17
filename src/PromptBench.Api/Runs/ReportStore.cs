namespace PromptBench.Api.Runs;

public sealed class ReportStore
{
    private readonly string _reportsDirectory;

    public ReportStore(string reportsDirectory)
    {
        _reportsDirectory = reportsDirectory;
    }

    public async Task<ReportSaveStatus> SaveAsync(
        Guid baselineId,
        Guid candidateId,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        var fileName = $"{baselineId:D}_vs_{candidateId:D}.md";
        var path = Path.Combine(_reportsDirectory, fileName);

        try
        {
            Directory.CreateDirectory(_reportsDirectory);

            if (File.Exists(path))
            {
                return ReportSaveStatus.AlreadyExists;
            }

            temporaryPath = Path.Combine(
                _reportsDirectory,
                $".{baselineId:D}_{candidateId:D}.{Guid.NewGuid():N}.tmp");

            await File.WriteAllTextAsync(temporaryPath, markdown, cancellationToken);

            try
            {
                File.Move(temporaryPath, path);
                temporaryPath = null;
                return ReportSaveStatus.Saved;
            }
            catch (IOException) when (File.Exists(path))
            {
                return ReportSaveStatus.AlreadyExists;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReportStorageException(
                "Não foi possível salvar o relatório da comparação.",
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
}

public enum ReportSaveStatus
{
    Saved,
    AlreadyExists
}


public sealed class ReportStorageException : Exception
{
    public ReportStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
