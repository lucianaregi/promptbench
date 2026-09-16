using System.Text.Json;

namespace PromptBench.Api.Evals;

public sealed class EvaluationSetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _evalsDirectory;

    public EvaluationSetLoader(string evalsDirectory)
    {
        _evalsDirectory = evalsDirectory;
    }

    public async Task<IReadOnlyList<EvaluationSetSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_evalsDirectory))
        {
            return [];
        }

        var summaries = new List<EvaluationSetSummary>();

        foreach (var path in Directory.EnumerateFiles(_evalsDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var result = await LoadFileAsync(path, cancellationToken);

            if (result.Status is not EvaluationSetLoadStatus.Success)
            {
                throw new InvalidDataException("Um ou mais evaluation sets não puderam ser carregados.");
            }

            var evaluationSet = result.EvaluationSet!;
            summaries.Add(new EvaluationSetSummary(
                evaluationSet.Name,
                evaluationSet.Description,
                evaluationSet.Cases.Count));
        }

        return summaries;
    }

    public async Task<EvaluationSetLoadResult> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_evalsDirectory))
        {
            return EvaluationSetLoadResult.NotFound();
        }

        var path = Directory
            .EnumerateFiles(_evalsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(candidate => string.Equals(
                Path.GetFileNameWithoutExtension(candidate),
                name,
                StringComparison.OrdinalIgnoreCase));

        return path is null
            ? EvaluationSetLoadResult.NotFound()
            : await LoadFileAsync(path, cancellationToken);
    }

    private static async Task<EvaluationSetLoadResult> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var evaluationSet = await JsonSerializer.DeserializeAsync<EvaluationSet>(
                stream,
                JsonOptions,
                cancellationToken);

            if (evaluationSet is null)
            {
                return EvaluationSetLoadResult.Invalid("O arquivo não contém um evaluation set.");
            }

            var validationErrors = Validate(evaluationSet);
            return validationErrors.Count > 0
                ? EvaluationSetLoadResult.Invalid(validationErrors)
                : EvaluationSetLoadResult.Success(evaluationSet);
        }
        catch (JsonException)
        {
            return EvaluationSetLoadResult.Invalid("O arquivo contém JSON inválido.");
        }
        catch (IOException)
        {
            return EvaluationSetLoadResult.Invalid("O arquivo não pôde ser lido.");
        }
    }

    private static IReadOnlyList<string> Validate(EvaluationSet evaluationSet)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(evaluationSet.Name))
        {
            errors.Add("O campo 'name' é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(evaluationSet.Description))
        {
            errors.Add("O campo 'description' é obrigatório.");
        }

        if (evaluationSet.Cases is not { Count: > 0 })
        {
            errors.Add("O campo 'cases' deve conter pelo menos um item.");
            return errors;
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evaluationCase in evaluationSet.Cases)
        {
            if (evaluationCase is null)
            {
                errors.Add("Os itens de 'cases' não podem ser nulos.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(evaluationCase.Id))
            {
                errors.Add("O campo 'id' é obrigatório em todos os casos.");
            }
            else if (!caseIds.Add(evaluationCase.Id))
            {
                errors.Add($"O ID de caso '{evaluationCase.Id}' está duplicado.");
            }

            if (string.IsNullOrWhiteSpace(evaluationCase.Input))
            {
                errors.Add("O campo 'input' é obrigatório em todos os casos.");
            }

            if (string.IsNullOrWhiteSpace(evaluationCase.Expected))
            {
                errors.Add("O campo 'expected' é obrigatório em todos os casos.");
            }
        }

        return errors;
    }
}

public enum EvaluationSetLoadStatus
{
    Success,
    NotFound,
    Invalid
}

public sealed record EvaluationSetLoadResult(
    EvaluationSetLoadStatus Status,
    EvaluationSet? EvaluationSet,
    IReadOnlyList<string> Errors)
{
    public static EvaluationSetLoadResult Success(EvaluationSet evaluationSet) =>
        new(EvaluationSetLoadStatus.Success, evaluationSet, []);

    public static EvaluationSetLoadResult NotFound() =>
        new(EvaluationSetLoadStatus.NotFound, null, []);

    public static EvaluationSetLoadResult Invalid(params string[] errors) =>
        new(EvaluationSetLoadStatus.Invalid, null, errors);

    public static EvaluationSetLoadResult Invalid(IReadOnlyList<string> errors) =>
        new(EvaluationSetLoadStatus.Invalid, null, errors);
}
