using System.Globalization;
using System.Text;

namespace PromptBench.Api.Runs;

public sealed class PersistedRunReportGenerator
{
    private static readonly CultureInfo PortugueseBrazil =
        CultureInfo.GetCultureInfo("pt-BR");

    public string Generate(PersistedRunComparisonResult comparison)
    {
        var report = new StringBuilder();

        report.AppendLine("# Relatório de comparação de execuções");
        report.AppendLine();
        AppendRunDetails(report, "Baseline", comparison.Baseline);
        AppendRunDetails(report, "Candidate", comparison.Candidate);
        AppendPromptVersionNote(report, comparison);

        report.AppendLine("## Resumo");
        report.AppendLine();
        report.AppendLine($"- Evaluation Set: `{EscapeInline(comparison.Evaluation)}`");
        report.AppendLine($"- Total de casos: {comparison.Summary.Total}");
        report.AppendLine($"- Regressões: {comparison.Summary.Regressions}");
        report.AppendLine($"- Melhorias: {comparison.Summary.Improvements}");
        report.AppendLine($"- Sem alteração — aprovados: {comparison.Summary.UnchangedPass}");
        report.AppendLine($"- Sem alteração — reprovados: {comparison.Summary.UnchangedFail}");
        report.AppendLine($"- Adicionados: {comparison.Summary.Added}");
        report.AppendLine($"- Removidos: {comparison.Summary.Removed}");
        report.AppendLine();

        report.AppendLine("## Métricas");
        report.AppendLine();
        report.AppendLine("| Métrica | Baseline | Candidate | Variação |");
        report.AppendLine("|---|---:|---:|---:|");
        report.AppendLine(
            $"| Taxa de aprovação | {FormatPercentage(comparison.Metrics.PassRate.Baseline)} | " +
            $"{FormatPercentage(comparison.Metrics.PassRate.Candidate)} | " +
            $"{FormatPercentageDifference(comparison.Metrics.PassRate.Difference)} |");
        report.AppendLine(
            $"| Duração | {FormatLong(comparison.Metrics.DurationMs.Baseline, " ms")} | " +
            $"{FormatLong(comparison.Metrics.DurationMs.Candidate, " ms")} | " +
            $"{FormatLongDifference(comparison.Metrics.DurationMs.Difference, " ms")} |");
        report.AppendLine(
            $"| Total de tokens | {FormatLong(comparison.Metrics.TotalTokens.Baseline)} | " +
            $"{FormatLong(comparison.Metrics.TotalTokens.Candidate)} | " +
            $"{FormatLongDifference(comparison.Metrics.TotalTokens.Difference)} |");
        report.AppendLine();
        report.AppendLine(
            "A variação corresponde a `candidate - baseline` e não representa julgamento de qualidade.");
        report.AppendLine();

        report.AppendLine("## Casos");
        report.AppendLine();
        report.AppendLine("| Caso | Baseline | Candidate | Resultado |");
        report.AppendLine("|---|---|---|---|");

        foreach (var item in comparison.Cases)
        {
            report.AppendLine(
                $"| {EscapeTableCell(item.CaseId)} | {FormatPassed(item.BaselinePassed)} | " +
                $"{FormatPassed(item.CandidatePassed)} | {FormatStatus(item.Status)} |");
        }

        report.AppendLine();
        report.AppendLine("## Regressões");
        report.AppendLine();

        var regressions = comparison.Cases
            .Where(item => item.Status is "regression")
            .ToArray();

        if (regressions.Length is 0)
        {
            report.AppendLine("Nenhuma regressão encontrada.");
        }
        else
        {
            foreach (var regression in regressions)
            {
                report.AppendLine(
                    $"- `{EscapeInline(regression.CaseId)}`: passou no baseline e falhou no candidate.");
            }
        }

        return report.ToString();
    }

    private static void AppendRunDetails(
        StringBuilder report,
        string title,
        PersistedRunInfo run)
    {
        report.AppendLine($"## {title}");
        report.AppendLine();
        report.AppendLine($"- ID: `{run.Id:D}`");
        report.AppendLine($"- Data: {run.StartedAt:O}");
        report.AppendLine($"- Modelo solicitado: `{EscapeInline(run.RequestedModel)}`");
        report.AppendLine($"- Prompt: {FormatPrompt(run.PromptName, run.PromptVersion)}");
        report.AppendLine(
            $"- Modelos utilizados: {FormatModels(run.UsedModels, run.RequestedModel)}");
        report.AppendLine();
    }

    private static void AppendPromptVersionNote(
        StringBuilder report,
        PersistedRunComparisonResult comparison)
    {
        if (comparison.BaselinePromptName is null ||
            comparison.BaselinePromptVersion is null ||
            comparison.CandidatePromptName is null ||
            comparison.CandidatePromptVersion is null ||
            !string.Equals(
                comparison.BaselinePromptName,
                comparison.CandidatePromptName,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                comparison.BaselinePromptVersion,
                comparison.CandidatePromptVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        report.AppendLine(
            $"As execuções usam versões diferentes do prompt " +
            $"`{EscapeInline(comparison.BaselinePromptName)}`: " +
            $"baseline `{EscapeInline(comparison.BaselinePromptVersion)}` e " +
            $"candidate `{EscapeInline(comparison.CandidatePromptVersion)}`.");
        report.AppendLine();
    }

    private static string FormatPrompt(string? name, string? version) =>
        name is null || version is null
            ? "Não disponível"
            : $"`{EscapeInline(name)}` (`{EscapeInline(version)}`)";

    private static string FormatModels(
        IReadOnlyList<string> usedModels,
        string requestedModel) =>
        string.Join(
            ", ",
            (usedModels.Count > 0 ? usedModels : [requestedModel])
                .Select(model => $"`{EscapeInline(model)}`"));

    private static string FormatPassed(bool? passed) =>
        passed switch
        {
            true => "Passou",
            false => "Falhou",
            null => "Não presente"
        };

    private static string FormatStatus(string status) =>
        status switch
        {
            "unchanged_pass" => "Sem alteração (passou)",
            "unchanged_fail" => "Sem alteração (falhou)",
            "regression" => "Regressão",
            "improvement" => "Melhoria",
            "added" => "Adicionado",
            "removed" => "Removido",
            _ => EscapeTableCell(status)
        };

    private static string FormatPercentage(double? value) =>
        value is null
            ? "Não disponível"
            : $"{value.Value.ToString("0.##", PortugueseBrazil)}%";

    private static string FormatPercentageDifference(double? value) =>
        value is null
            ? "Não disponível"
            : $"{FormatSign(value.Value)}{value.Value.ToString("0.##", PortugueseBrazil)} p.p.";

    private static string FormatLong(long? value, string suffix = "") =>
        value is null
            ? "Não disponível"
            : $"{value.Value.ToString("N0", PortugueseBrazil)}{suffix}";

    private static string FormatLongDifference(long? value, string suffix = "") =>
        value is null
            ? "Não disponível"
            : $"{FormatSign(value.Value)}{value.Value.ToString("N0", PortugueseBrazil)}{suffix}";

    private static string FormatSign(double value) => value > 0 ? "+" : string.Empty;

    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");

    private static string EscapeInline(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
}
