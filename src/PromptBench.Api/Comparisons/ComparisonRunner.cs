using System.Diagnostics;
using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Runs;

namespace PromptBench.Api.Comparisons;

public sealed class ComparisonRunner
{
    private readonly EvaluationRunner _evaluationRunner;

    public ComparisonRunner(EvaluationRunner evaluationRunner)
    {
        _evaluationRunner = evaluationRunner;
    }

    public async Task<ComparisonResult> RunAsync(
        EvaluationSet evaluationSet,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var comparisonStarted = Stopwatch.GetTimestamp();
        var runs = new List<ComparisonRunResult>(models.Count);

        foreach (var model in models)
        {
            var runStarted = Stopwatch.GetTimestamp();

            try
            {
                var run = await _evaluationRunner.RunAsync(
                    evaluationSet,
                    model,
                    cancellationToken);

                runs.Add(CreateCompletedRun(run));
            }
            catch (OpenRouterException exception)
            {
                runs.Add(new ComparisonRunResult(
                    model,
                    null,
                    "failed",
                    ElapsedMilliseconds(runStarted),
                    null,
                    null,
                    null,
                    [],
                    new ComparisonError(FailureType(exception.Kind), exception.Message)));
            }
        }

        var completedRuns = runs.Count(run => run.Status is "completed");
        var status = completedRuns switch
        {
            0 => "failed",
            var count when count == runs.Count => "completed",
            _ => "partial"
        };

        return new ComparisonResult(
            evaluationSet.Name,
            startedAt,
            ElapsedMilliseconds(comparisonStarted),
            status,
            runs);
    }

    private static ComparisonRunResult CreateCompletedRun(EvaluationRunResult run)
    {
        var actualModels = run.Results
            .Select(result => result.UsedModel)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ComparisonRunResult(
            run.RequestedModel,
            actualModels.Length is 1 ? actualModels[0] : null,
            "completed",
            run.DurationMs,
            SumTokens(run.Results, usage => usage.PromptTokens),
            SumTokens(run.Results, usage => usage.CompletionTokens),
            SumTokens(run.Results, usage => usage.TotalTokens),
            run.Results,
            null);
    }

    private static int? SumTokens(
        IReadOnlyList<EvaluationCaseRunResult> results,
        Func<TokenUsage, int?> selector)
    {
        if (results.Count is 0 || results.Any(result => result.Usage is null || selector(result.Usage) is null))
        {
            return null;
        }

        return results.Sum(result => selector(result.Usage!)!.Value);
    }

    private static string FailureType(OpenRouterFailureKind kind) => kind switch
    {
        OpenRouterFailureKind.Configuration => "configuration",
        OpenRouterFailureKind.InvalidRequest => "invalid_request",
        OpenRouterFailureKind.Authentication => "authentication",
        OpenRouterFailureKind.RateLimit => "rate_limit",
        OpenRouterFailureKind.Timeout => "timeout",
        OpenRouterFailureKind.InvalidResponse => "invalid_response",
        _ => "service_unavailable"
    };

    private static long ElapsedMilliseconds(long started) =>
        (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}
