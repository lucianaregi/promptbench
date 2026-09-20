using System.Text.Json;

namespace PromptBench.Api.Runs;

public sealed class PersistedRunComparisonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RunStore _runStore;

    public PersistedRunComparisonService(RunStore runStore)
    {
        _runStore = runStore;
    }

    public Task<PersistedRunComparisonOutcome> CompareAsync(
        Guid baselineId,
        Guid candidateId,
        CancellationToken cancellationToken = default) =>
        CompareLoadedAsync(
            _runStore.LoadAsync(baselineId, cancellationToken),
            () => _runStore.LoadAsync(candidateId, cancellationToken));

    public Task<PersistedRunComparisonOutcome> CompareFilesAsync(
        string baselinePath,
        string candidatePath,
        CancellationToken cancellationToken = default) =>
        CompareLoadedAsync(
            _runStore.LoadFileAsync(baselinePath, cancellationToken),
            () => _runStore.LoadFileAsync(candidatePath, cancellationToken));

    private static async Task<PersistedRunComparisonOutcome> CompareLoadedAsync(
        Task<RunLoadResult> baselineLoadTask,
        Func<Task<RunLoadResult>> loadCandidate)
    {
        var baselineLoad = await baselineLoadTask;

        if (baselineLoad.Status is RunLoadStatus.NotFound)
        {
            return PersistedRunComparisonOutcome.BaselineNotFound();
        }

        if (baselineLoad.Status is not RunLoadStatus.Success || baselineLoad.Result is null)
        {
            return PersistedRunComparisonOutcome.InvalidStorage();
        }

        var candidateLoad = await loadCandidate();

        if (candidateLoad.Status is RunLoadStatus.NotFound)
        {
            return PersistedRunComparisonOutcome.CandidateNotFound();
        }

        if (candidateLoad.Status is not RunLoadStatus.Success || candidateLoad.Result is null)
        {
            return PersistedRunComparisonOutcome.InvalidStorage();
        }

        if (!TryReadRun(baselineLoad.Result.Value, out var baseline) ||
            !TryReadRun(candidateLoad.Result.Value, out var candidate))
        {
            return PersistedRunComparisonOutcome.InsufficientData();
        }

        if (!string.Equals(baseline.Evaluation, candidate.Evaluation, StringComparison.Ordinal))
        {
            return PersistedRunComparisonOutcome.DifferentEvaluation();
        }

        return PersistedRunComparisonOutcome.Success(CreateComparison(baseline, candidate));
    }

    private static PersistedRunComparisonResult CreateComparison(
        EvaluationRunResult baseline,
        EvaluationRunResult candidate)
    {
        var baselineCases = baseline.Results.ToDictionary(result => result.CaseId, StringComparer.Ordinal);
        var candidateCases = candidate.Results.ToDictionary(result => result.CaseId, StringComparer.Ordinal);
        var cases = new List<PersistedRunCaseComparison>(baselineCases.Count + candidateCases.Count);

        foreach (var baselineCase in baseline.Results)
        {
            var baselinePassed = baselineCase.Evaluation!.Passed!.Value;

            if (!candidateCases.TryGetValue(baselineCase.CaseId, out var candidateCase))
            {
                cases.Add(new PersistedRunCaseComparison(
                    baselineCase.CaseId,
                    "removed",
                    baselinePassed,
                    null));
                continue;
            }

            var candidatePassed = candidateCase.Evaluation!.Passed!.Value;
            cases.Add(new PersistedRunCaseComparison(
                baselineCase.CaseId,
                Classify(baselinePassed, candidatePassed),
                baselinePassed,
                candidatePassed));
        }

        foreach (var candidateCase in candidate.Results)
        {
            if (baselineCases.ContainsKey(candidateCase.CaseId))
            {
                continue;
            }

            cases.Add(new PersistedRunCaseComparison(
                candidateCase.CaseId,
                "added",
                null,
                candidateCase.Evaluation!.Passed!.Value));
        }

        var baselinePassRate = PassRate(baseline);
        var candidatePassRate = PassRate(candidate);
        var baselineTokens = TotalTokens(baseline);
        var candidateTokens = TotalTokens(candidate);

        return new PersistedRunComparisonResult(
            baseline.Id,
            candidate.Id,
            baseline.Evaluation,
            CreateRunInfo(baseline),
            CreateRunInfo(candidate),
            new PersistedRunComparisonSummary(
                cases.Count,
                cases.Count(item => item.Status is "regression"),
                cases.Count(item => item.Status is "improvement"),
                cases.Count(item => item.Status is "unchanged_pass"),
                cases.Count(item => item.Status is "unchanged_fail"),
                cases.Count(item => item.Status is "added"),
                cases.Count(item => item.Status is "removed")),
            new PersistedRunComparisonMetrics(
                new RunDoubleMetricComparison(
                    baselinePassRate,
                    candidatePassRate,
                    Math.Round(candidatePassRate - baselinePassRate, 2)),
                new RunLongMetricComparison(
                    baseline.DurationMs,
                    candidate.DurationMs,
                    candidate.DurationMs - baseline.DurationMs),
                new RunLongMetricComparison(
                    baselineTokens,
                    candidateTokens,
                    Difference(baselineTokens, candidateTokens))),
            cases);
    }

    private static PersistedRunInfo CreateRunInfo(EvaluationRunResult run) =>
        new(
            run.Id,
            run.StartedAt,
            run.RequestedModel,
            run.Results
                .Select(result => result.UsedModel)
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            run.PromptName,
            run.PromptVersion);

    private static bool TryReadRun(JsonElement root, out EvaluationRunResult run)
    {
        run = null!;

        if (!root.TryGetProperty("type", out var type) ||
            type.ValueKind is not JsonValueKind.String ||
            type.GetString() is not "evaluation_run")
        {
            return false;
        }

        try
        {
            var parsed = root.Deserialize<EvaluationRunResult>(JsonOptions);

            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.Evaluation) ||
                parsed.Results is not { Count: > 0 } ||
                parsed.Results.Select(result => result.CaseId).Distinct(StringComparer.Ordinal).Count() !=
                parsed.Results.Count ||
                parsed.Results.Any(result =>
                    string.IsNullOrWhiteSpace(result.CaseId) ||
                    result.Evaluation is null ||
                    result.Evaluation.Status is not "completed" ||
                    result.Evaluation.Passed is null))
            {
                return false;
            }

            run = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Classify(bool baselinePassed, bool candidatePassed) =>
        (baselinePassed, candidatePassed) switch
        {
            (true, true) => "unchanged_pass",
            (false, false) => "unchanged_fail",
            (true, false) => "regression",
            _ => "improvement"
        };

    private static double PassRate(EvaluationRunResult run) =>
        Math.Round(
            run.Results.Count(result => result.Evaluation!.Passed is true) * 100d / run.Results.Count,
            2);

    private static long? TotalTokens(EvaluationRunResult run)
    {
        if (run.Results.Any(result =>
                result.Usage?.TotalTokens is null ||
                result.Evaluation?.Usage?.TotalTokens is null))
        {
            return null;
        }

        return run.Results.Sum(result =>
            (long)result.Usage!.TotalTokens!.Value +
            result.Evaluation!.Usage!.TotalTokens!.Value);
    }

    private static long? Difference(long? baseline, long? candidate) =>
        baseline is not null && candidate is not null
            ? candidate.Value - baseline.Value
            : null;
}

public enum PersistedRunComparisonStatus
{
    Success,
    BaselineNotFound,
    CandidateNotFound,
    DifferentEvaluation,
    InsufficientData,
    InvalidStorage
}

public sealed record PersistedRunComparisonOutcome(
    PersistedRunComparisonStatus Status,
    PersistedRunComparisonResult? Result)
{
    public static PersistedRunComparisonOutcome Success(PersistedRunComparisonResult result) =>
        new(PersistedRunComparisonStatus.Success, result);

    public static PersistedRunComparisonOutcome BaselineNotFound() =>
        new(PersistedRunComparisonStatus.BaselineNotFound, null);

    public static PersistedRunComparisonOutcome CandidateNotFound() =>
        new(PersistedRunComparisonStatus.CandidateNotFound, null);

    public static PersistedRunComparisonOutcome DifferentEvaluation() =>
        new(PersistedRunComparisonStatus.DifferentEvaluation, null);

    public static PersistedRunComparisonOutcome InsufficientData() =>
        new(PersistedRunComparisonStatus.InsufficientData, null);

    public static PersistedRunComparisonOutcome InvalidStorage() =>
        new(PersistedRunComparisonStatus.InvalidStorage, null);
}
