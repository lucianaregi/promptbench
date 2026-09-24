extern alias PromptBenchCli;
using PromptBench.Api.Runs;
using System.Text.Json;
using RegressionGateCommand = PromptBenchCli::PromptBench.Cli.RegressionGateCommand;

namespace PromptBench.Tests;

public sealed class RegressionGateCommandTests
{
    [Fact]
    public async Task ReturnsZeroWhenGatePasses()
    {
        using var directory = new TemporaryDirectory();
        var baseline = CreateRun(true);
        var candidate = CreateRun(true);
        await SaveAsync(directory.Path, baseline, candidate);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            Args(baseline.Id, candidate.Id), output, error, directory.Path);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        AssertGateJson(json.RootElement, true, baseline.Id, candidate.Id, 0, []);
        Assert.Empty(error.ToString());
    }
    [Fact]
    public async Task ReturnsOneAndListsRegressionCases()
    {
        using var directory = new TemporaryDirectory();
        var baseline = CreateRun(true);
        var candidate = CreateRun(false);
        await SaveAsync(directory.Path, baseline, candidate);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            Args(baseline.Id, candidate.Id), output, error, directory.Path);
        Assert.Equal(1, code);
        using var json = JsonDocument.Parse(output.ToString());
        AssertGateJson(json.RootElement, false, baseline.Id, candidate.Id, 1, ["caso"]);
        Assert.Empty(error.ToString());
    }
    [Fact]
    public async Task ReturnsTwoForInvalidUse()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            ["regression-gate", "--baseline", "inválido",
                "--candidate", Guid.NewGuid().ToString()], output, error);
        Assert.Equal(2, code);
        Assert.Contains("GUIDs válidos", error.ToString());
        Assert.Empty(output.ToString());
    }
    [Fact]
    public async Task ReturnsTwoWhenBaselineDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            Args(Guid.NewGuid(), Guid.NewGuid()), output, error, directory.Path);
        Assert.Equal(2, code);
        Assert.Contains("baseline não existe", error.ToString());
        Assert.Empty(output.ToString());
    }
    [Fact]
    public async Task FileModeReturnsJsonAndZeroWhenGatePasses()
    {
        using var directory = new TemporaryDirectory();
        var baseline = CreateRun(true);
        var candidate = CreateRun(true);
        var files = await SaveAsFilesAsync(directory.Path, baseline, candidate);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            FileArgs(files.Baseline, files.Candidate), output, error);
        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        AssertGateJson(json.RootElement, true, baseline.Id, candidate.Id, 0, []);
        Assert.Empty(error.ToString());
    }
    [Fact]
    public async Task FileModeReturnsJsonAndOneForRegression()
    {
        using var directory = new TemporaryDirectory();
        var baseline = CreateRun(true);
        var candidate = CreateRun(false);
        var files = await SaveAsFilesAsync(directory.Path, baseline, candidate);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            FileArgs(files.Baseline, files.Candidate), output, error);
        Assert.Equal(1, code);
        using var json = JsonDocument.Parse(output.ToString());
        AssertGateJson(json.RootElement, false, baseline.Id, candidate.Id, 1, ["caso"]);
        Assert.Empty(error.ToString());
    }
    [Fact]
    public async Task FileModeReturnsTwoForMissingFile()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            FileArgs("baseline-ausente.json", "candidate-ausente.json"), output, error);
        Assert.Equal(2, code);
        Assert.Empty(output.ToString());
        Assert.Contains("baseline não existe", error.ToString());
    }
    [Fact]
    public async Task FileModeReturnsTwoForInvalidJson()
    {
        using var directory = new TemporaryDirectory();
        var baselinePath = Path.Combine(directory.Path, "baseline.json");
        await File.WriteAllTextAsync(baselinePath, "{ json inválido }");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            FileArgs(baselinePath, "candidate.json"), output, error);
        Assert.Equal(2, code);
        Assert.Empty(output.ToString());
        Assert.Contains("inválido", error.ToString());
    }
    [Fact]
    public async Task FileModeReturnsTwoForIncompatibleRuns()
    {
        using var directory = new TemporaryDirectory();
        var baseline = CreateRun(true, "resumos");
        var candidate = CreateRun(true, "classificação");
        var files = await SaveAsFilesAsync(directory.Path, baseline, candidate);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await RegressionGateCommand.RunAsync(
            FileArgs(files.Baseline, files.Candidate), output, error);
        Assert.Equal(2, code);
        Assert.Empty(output.ToString());
        Assert.Contains("mesmo Evaluation Set", error.ToString());
    }
    private static void AssertGateJson(
        JsonElement json, bool passed, Guid baselineId, Guid candidateId,
        int regressions, IReadOnlyList<string> caseIds)
    {
        Assert.Equal(5, json.EnumerateObject().Count());
        Assert.Equal(passed, json.GetProperty("passed").GetBoolean());
        Assert.Equal(baselineId, json.GetProperty("baselineId").GetGuid());
        Assert.Equal(candidateId, json.GetProperty("candidateId").GetGuid());
        Assert.Equal(regressions, json.GetProperty("regressions").GetInt32());
        Assert.Equal(caseIds, json.GetProperty("regressionCaseIds")
            .EnumerateArray().Select(item => item.GetString()!).ToArray());
    }
    private static EvaluationRunResult CreateRun(
        bool passed, string evaluation = "resumos") =>
        PersistedRunComparisonServiceTests.CreateRun(
            Guid.NewGuid(), evaluation, 100, 10, ("caso", passed));
    private static string[] Args(Guid baseline, Guid candidate) =>
        ["regression-gate", "--baseline", baseline.ToString(),
            "--candidate", candidate.ToString()];
    private static string[] FileArgs(string baseline, string candidate) =>
        ["regression-gate", "--baseline-file", baseline,
            "--candidate-file", candidate];
    private static async Task<(string Baseline, string Candidate)> SaveAsFilesAsync(
        string path, EvaluationRunResult baseline, EvaluationRunResult candidate)
    {
        await SaveAsync(path, baseline, candidate);
        var baselineFile = Path.Combine(path, "baseline.json");
        var candidateFile = Path.Combine(path, "candidate.json");
        File.Move(Path.Combine(path, $"{baseline.Id:D}.json"), baselineFile);
        File.Move(Path.Combine(path, $"{candidate.Id:D}.json"), candidateFile);
        return (baselineFile, candidateFile);
    }
    private static async Task SaveAsync(
        string path, EvaluationRunResult baseline, EvaluationRunResult candidate)
    {
        var store = new RunStore(path);
        await store.SaveAsync(baseline);
        await store.SaveAsync(candidate);
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() =>
            Path = Directory.CreateTempSubdirectory("promptbench-cli-tests-").FullName;
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
