using System.Text.Json;

namespace PromptBench.Api.Prompts;

public sealed class PromptLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _promptsDirectory;

    public PromptLoader(string promptsDirectory)
    {
        _promptsDirectory = promptsDirectory;
    }

    public async Task<IReadOnlyList<PromptDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_promptsDirectory))
        {
            return [];
        }

        var prompts = new List<PromptDefinition>();

        foreach (var path in Directory.EnumerateFiles(
                     _promptsDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var result = await LoadFileAsync(path, cancellationToken);

            if (result.Status is not PromptLoadStatus.Success)
            {
                throw new InvalidDataException("Um ou mais prompts não puderam ser carregados.");
            }

            prompts.Add(result.Prompt!);
        }

        if (prompts
            .GroupBy(prompt => (prompt.Name, prompt.Version), PromptIdentityComparer.Instance)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Há prompts com a mesma identidade e versão.");
        }

        return prompts;
    }

    public async Task<PromptLoadResult> LoadAsync(
        string name,
        string version,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PromptDefinition> prompts;

        try
        {
            prompts = await ListAsync(cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return PromptLoadResult.Invalid(exception.Message);
        }

        var prompt = prompts.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Version, version, StringComparison.OrdinalIgnoreCase));

        return prompt is null
            ? PromptLoadResult.NotFound()
            : PromptLoadResult.Success(prompt);
    }

    private static async Task<PromptLoadResult> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var prompt = await JsonSerializer.DeserializeAsync<PromptDefinition>(
                stream,
                JsonOptions,
                cancellationToken);

            if (prompt is null)
            {
                return PromptLoadResult.Invalid("O arquivo não contém um prompt.");
            }

            var errors = Validate(prompt);
            return errors.Count is 0
                ? PromptLoadResult.Success(prompt)
                : PromptLoadResult.Invalid(errors);
        }
        catch (JsonException)
        {
            return PromptLoadResult.Invalid("O arquivo contém JSON inválido.");
        }
        catch (IOException)
        {
            return PromptLoadResult.Invalid("O arquivo não pôde ser lido.");
        }
    }

    private static IReadOnlyList<string> Validate(PromptDefinition prompt)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(prompt.Name))
        {
            errors.Add("O campo 'name' é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(prompt.Version))
        {
            errors.Add("O campo 'version' é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(prompt.Template))
        {
            errors.Add("O campo 'template' é obrigatório.");
        }
        else if (!prompt.Template.Contains("{{input}}", StringComparison.Ordinal))
        {
            errors.Add("O campo 'template' deve conter '{{input}}'.");
        }

        return errors;
    }

    private sealed class PromptIdentityComparer : IEqualityComparer<(string Name, string Version)>
    {
        public static PromptIdentityComparer Instance { get; } = new();

        public bool Equals(
            (string Name, string Version) x,
            (string Name, string Version) y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, string Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
    }
}

public enum PromptLoadStatus
{
    Success,
    NotFound,
    Invalid
}

public sealed record PromptLoadResult(
    PromptLoadStatus Status,
    PromptDefinition? Prompt,
    IReadOnlyList<string> Errors)
{
    public static PromptLoadResult Success(PromptDefinition prompt) =>
        new(PromptLoadStatus.Success, prompt, []);

    public static PromptLoadResult NotFound() =>
        new(PromptLoadStatus.NotFound, null, []);

    public static PromptLoadResult Invalid(params string[] errors) =>
        new(PromptLoadStatus.Invalid, null, errors);

    public static PromptLoadResult Invalid(IReadOnlyList<string> errors) =>
        new(PromptLoadStatus.Invalid, null, errors);
}
