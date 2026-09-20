using System.Diagnostics;
using PromptBench.Api.Evals;
using PromptBench.Api.OpenRouter;
using PromptBench.Api.Prompts;

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
        PromptDefinition prompt,
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
                Render(prompt.Template, evaluationCase.Input),
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
            results,
            prompt.Name,
            prompt.Version);
    }

    private static string Render(string template, string input) =>
        template.Replace("{{input}}", input, StringComparison.Ordinal);

    private static long ElapsedMilliseconds(long started) =>
        (long)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
}
