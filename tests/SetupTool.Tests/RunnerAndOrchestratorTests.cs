using SetupTool.Core.Execution;
using SetupTool.Core.Manifest;

namespace SetupTool.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task Runs_NonRoot_Echo()
    {
        var runner = new ProcessRunner(invoking: null);
        var result = await runner.RunAsync(new ProcessSpec
        {
            FileName = "echo",
            Arguments = ["hello world"],
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Privilege = Privilege.User,
            Interactive = false,
            CaptureOutput = true,
        });
        Assert.True(result.Success);
        Assert.Contains("hello world", result.Output);
    }

    [Fact]
    public async Task Captures_ExitCode_OnFailure()
    {
        var runner = new ProcessRunner(invoking: null);
        var result = await runner.RunAsync(new ProcessSpec
        {
            FileName = "sh",
            Arguments = ["-c", "exit 3"],
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Privilege = Privilege.User,
            Interactive = false,
            CaptureOutput = true,
        });
        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task Respects_WorkingDirectory()
    {
        var runner = new ProcessRunner(invoking: null);
        var tmp = Path.Combine(Path.GetTempPath(), "setuptool-wd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var result = await runner.RunAsync(new ProcessSpec
        {
            FileName = "pwd",
            Arguments = [],
            WorkingDirectory = tmp,
            Privilege = Privilege.User,
            Interactive = false,
            CaptureOutput = true,
        });
        Assert.Contains(tmp, result.Output);
        Directory.Delete(tmp);
    }

    [Fact]
    public async Task Applies_Environment()
    {
        var runner = new ProcessRunner(invoking: null);
        var result = await runner.RunAsync(new ProcessSpec
        {
            FileName = "sh",
            Arguments = ["-c", "echo \"$TEST_VAR\""],
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Privilege = Privilege.User,
            Interactive = false,
            CaptureOutput = true,
            Environment = new Dictionary<string, string> { ["TEST_VAR"] = "abc123" },
        });
        Assert.Contains("abc123", result.Output);
    }
}

public class OrchestratorTests
{
    private static (Orchestrator orch, FakeRunner runner) Build(IStepExecutor[] executors)
    {
        var runner = new FakeRunner();
        var orch = new Orchestrator(executors, runner, new FakeDownloader(), invoking: null);
        return (orch, runner);
    }

    [Fact]
    public async Task Runs_StepsInOrder_AndReports()
    {
        var bash = new BashExecutor();
        var runner = new FakeRunner();
        var orch = new Orchestrator([bash], runner, new FakeDownloader(), invoking: null);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [
                new Step { Type = StepType.Bash, Script = "echo 1" },
                new Step { Type = StepType.Bash, Script = "echo 2" },
            ],
        };

        var report = await orch.RunAsync([manifest]);

        Assert.True(report.Succeeded);
        Assert.Equal(2, report.Steps.Count);
        Assert.Equal(StepOutcome.Completed, report.Steps[0].Outcome);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Stops_OnFirstFailure()
    {
        var bash = new BashExecutor();
        var runner = new FakeRunner();
        var orch = new Orchestrator([bash], runner, new FakeDownloader(), invoking: null);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [
                new Step { Type = StepType.Bash, Script = "echo 1" },
                new Step { Type = StepType.Bash, Script = "echo 2" },
                new Step { Type = StepType.Bash, Script = "echo 3" },
            ],
        };
        runner.Responses.Enqueue((0, ""));
        runner.Responses.Enqueue((1, "")); // step 2 fails

        var report = await orch.RunAsync([manifest]);

        Assert.False(report.Succeeded);
        Assert.Equal(2, runner.Calls.Count); // stopped after step 2
        Assert.Contains("FAILED", report.Steps[1].Note);
    }

    [Fact]
    public async Task Resolves_ManifestWorkdir_Default()
    {
        var bash = new BashExecutor();
        var runner = new FakeRunner();
        var orch = new Orchestrator([bash], runner, new FakeDownloader(), invoking: null);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            WorkDir = "~/apps",
            Steps = [new Step { Type = StepType.Bash, Script = "echo 1" }],
        };

        await orch.RunAsync([manifest]);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "apps"), runner.Calls[0].WorkingDirectory);
    }
}
