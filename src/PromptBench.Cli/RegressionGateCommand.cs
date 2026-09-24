using PromptBench.Api.Runs;
using System.Text.Json;

namespace PromptBench.Cli;

public static class RegressionGateCommand
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public const int PassedExitCode = 0;
    public const int RegressionExitCode = 1;
    public const int ErrorExitCode = 2;
    public static Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error,
        string? runsDirectory = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(args, output, error, runsDirectory, cancellationToken);
    private static async Task<int> ExecuteAsync(
        string[] args, TextWriter output, TextWriter error,
        string? runsDirectory, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Error is not null)
            return await UsageErrorAsync(parsed.Error, error);
        return await CompareAsync(parsed, output, error, runsDirectory, cancellationToken);
    }
    private static async Task<int> UsageErrorAsync(string message, TextWriter error)
    {
        await error.WriteLineAsync(message);
        await error.WriteLineAsync(
            "Uso: regression-gate (--baseline RUN-ID --candidate RUN-ID | --baseline-file ARQUIVO --candidate-file ARQUIVO)");
        return ErrorExitCode;
    }
    private static async Task<int> CompareAsync(
        CommandArguments parsed, TextWriter output, TextWriter error,
        string? runsDirectory, CancellationToken cancellationToken)
    {
        var directory = runsDirectory ?? Path.Combine(AppContext.BaseDirectory, "runs");
        try
        {
            var service = new PersistedRunComparisonService(new RunStore(directory));
            var comparison = parsed.BaselineId is { } baselineId &&
                             parsed.CandidateId is { } candidateId
                ? await service.CompareAsync(baselineId, candidateId, cancellationToken)
                : await service.CompareFilesAsync(
                    parsed.BaselineFile!, parsed.CandidateFile!, cancellationToken);
            if (comparison.Status is not PersistedRunComparisonStatus.Success)
                return await ComparisonErrorAsync(comparison.Status, error);
            return await GateAsync(comparison.Result!, output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("A execução do regression gate foi cancelada.");
            return ErrorExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync("Não foi possível ler as execuções persistidas.");
            return ErrorExitCode;
        }
    }
    private static async Task<int> GateAsync(
        PersistedRunComparisonResult comparison, TextWriter output)
    {
        var gate = new RegressionGateService().Evaluate(comparison);
        var result = new RegressionGateCommandResult(
            gate.Passed,
            comparison.BaselineId,
            comparison.CandidateId,
            gate.Regressions,
            gate.RegressionCaseIds ?? []);
        await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        return gate.Passed ? PassedExitCode : RegressionExitCode;
    }
    private static async Task<int> ComparisonErrorAsync(
        PersistedRunComparisonStatus status, TextWriter error)
    {
        var message = status switch
        {
            PersistedRunComparisonStatus.BaselineNotFound =>
                "A execução informada como baseline não existe.",
            PersistedRunComparisonStatus.CandidateNotFound =>
                "A execução informada como candidate não existe.",
            PersistedRunComparisonStatus.DifferentEvaluation =>
                "As execuções devem pertencer ao mesmo Evaluation Set.",
            PersistedRunComparisonStatus.InsufficientData =>
                "As execuções não possuem dados suficientes para comparação.",
            _ => "Um resultado armazenado está inválido ou não pôde ser lido."
        };
        await error.WriteLineAsync(message);
        return ErrorExitCode;
    }
    private static CommandArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count is 0 || args[0] is not "regression-gate")
            return CommandArguments.Failure("Informe o comando 'regression-gate'.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count)
                return CommandArguments.Failure($"Informe um valor para '{args[index]}'.");
            var option = args[index];
            if (option is not ("--baseline" or "--candidate" or
                "--baseline-file" or "--candidate-file"))
                return CommandArguments.Failure($"A opção '{option}' não é reconhecida.");
            if (!options.TryAdd(option, args[index + 1]))
                return CommandArguments.Failure($"A opção '{option}' foi repetida.");
        }
        if (options.Count is 2 &&
            options.TryGetValue("--baseline", out var baselineValue) &&
            options.TryGetValue("--candidate", out var candidateValue))
        {
            if (!Guid.TryParse(baselineValue, out var baseline) ||
                !Guid.TryParse(candidateValue, out var candidate))
                return CommandArguments.Failure("Baseline e candidate devem ser GUIDs válidos.");
            return CommandArguments.FromIds(baseline, candidate);
        }
        if (options.Count is 2 &&
            options.TryGetValue("--baseline-file", out var baselineFile) &&
            options.TryGetValue("--candidate-file", out var candidateFile))
            return CommandArguments.FromFiles(baselineFile, candidateFile);
        return CommandArguments.Failure(
            "Informe o par --baseline/--candidate ou --baseline-file/--candidate-file.");
    }
    private sealed record CommandArguments(
        Guid? BaselineId, Guid? CandidateId,
        string? BaselineFile, string? CandidateFile, string? Error)
    {
        public static CommandArguments FromIds(Guid baseline, Guid candidate) =>
            new(baseline, candidate, null, null, null);
        public static CommandArguments FromFiles(string baseline, string candidate) =>
            new(null, null, baseline, candidate, null);
        public static CommandArguments Failure(string error) =>
            new(null, null, null, null, error);
    }
    private sealed record RegressionGateCommandResult(
        bool Passed,
        Guid BaselineId,
        Guid CandidateId,
        int Regressions,
        IReadOnlyList<string> RegressionCaseIds);
}
