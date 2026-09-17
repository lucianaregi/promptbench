using PromptBench.Api.Runs;

namespace PromptBench.Tests;

public sealed class PersistedRunReportGeneratorTests
{
    [Fact]
    public void GeneratesFactualMarkdownInPortugueseForEveryCaseStatus()
    {
        var baselineId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var candidateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var comparison = new PersistedRunComparisonResult(
            baselineId,
            candidateId,
            "summarization-basic",
            new PersistedRunInfo(
                baselineId,
                DateTimeOffset.Parse("2026-09-16T17:30:00Z"),
                "provedor/modelo-a:free",
                ["provedor/modelo-a-real"]),
            new PersistedRunInfo(
                candidateId,
                DateTimeOffset.Parse("2026-09-17T09:15:00Z"),
                "provedor/modelo-b:free",
                ["provedor/modelo-b-real"]),
            new PersistedRunComparisonSummary(6, 1, 1, 1, 1, 1, 1),
            new PersistedRunComparisonMetrics(
                new RunDoubleMetricComparison(60, 40, -20),
                new RunLongMetricComparison(800, 1_000, 200),
                new RunLongMetricComparison(100, 120, 20)),
            [
                new("sem-alteracao-aprovado", "unchanged_pass", true, true),
                new("sem-alteracao-reprovado", "unchanged_fail", false, false),
                new("regressao", "regression", true, false),
                new("melhoria", "improvement", false, true),
                new("adicionado", "added", null, false),
                new("removido", "removed", true, null)
            ]);
        var generator = new PersistedRunReportGenerator();

        var markdown = generator.Generate(comparison);

        Assert.Contains("# Relatório de comparação de execuções", markdown);
        Assert.Contains(baselineId.ToString(), markdown);
        Assert.Contains(candidateId.ToString(), markdown);
        Assert.Contains("2026-09-16T17:30:00.0000000+00:00", markdown);
        Assert.Contains("provedor/modelo-a-real", markdown);
        Assert.Contains("Evaluation Set: `summarization-basic`", markdown);
        Assert.Contains("- Regressões: 1", markdown);
        Assert.Contains("- Melhorias: 1", markdown);
        Assert.Contains("| regressao | Passou | Falhou | Regressão |", markdown);
        Assert.Contains("| melhoria | Falhou | Passou | Melhoria |", markdown);
        Assert.Contains("| adicionado | Não presente | Falhou | Adicionado |", markdown);
        Assert.Contains("| removido | Passou | Não presente | Removido |", markdown);
        Assert.Contains("`regressao`: passou no baseline e falhou no candidate.", markdown);
        Assert.Contains("| Taxa de aprovação | 60% | 40% | -20 p.p. |", markdown);
        Assert.Contains("candidate - baseline", markdown);
        Assert.DoesNotContain("vencedor", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("melhor modelo", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
