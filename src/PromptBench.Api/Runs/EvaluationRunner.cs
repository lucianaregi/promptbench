using System.Diagnostics;
using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;

namespace PromptBench.Api.Runs;

public sealed class EvaluationRunner
{
    private readonly OpenRouterClient _openRouterClient;
    private readonly LlmJudgeEvaluator _llmJudgeEvaluator;

    public EvaluationRunner(
        OpenRouterClient openRouterClient,
        LlmJudgeEvaluator llmJudgeEvaluator)
    {
        _openRouterClient = openRouterClient;
        _llmJudgeEvaluator = llmJudgeEvaluator;
    }

    public async Task<EvaluationRunResult> RunAsync(
        EvaluationSet evaluationSet,
        string model,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var runStarted = Stopwatch.GetTimestamp();
        var results = new List<EvaluationCaseRunResult>(evaluationSet.Cases.Count);

        foreach (var evaluationCase in evaluationSet.Cases)
        {
            var caseStarted = Stopwatch.GetTimestamp();
            var completion = await _openRouterClient.CompleteAsync(
                model,
                evaluationCase.Input,
                cancellationToken);

            var generationDurationMs = ElapsedMilliseconds(caseStarted);
            var evaluation = evaluationSet.Evaluation is null
                ? null
                : await _llmJudgeEvaluator.EvaluateAsync(
                    evaluationSet.Evaluation,
                    evaluationCase,
                    completion.Output,
                    cancellationToken);

            results.Add(new EvaluationCaseRunResult(
                evaluationCase.Id,
                evaluationCase.Input,
                evaluationCase.Expected,
                completion.Output,
                completion.Model,
                generationDurationMs,
                completion.Usage,
                evaluation));
        }

        return new EvaluationRunResult(
            Guid.NewGuid(),
            evaluationSet.Name,
            model,
            startedAt,
            ElapsedMilliseconds(runStarted),
            results);
    }

    private static long ElapsedMilliseconds(long started) =>
        (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}
