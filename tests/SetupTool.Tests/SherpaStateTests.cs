using SetupTool.Core.Execution;
using SetupTool.Core.Manifest;
using SetupTool.Core.State;

namespace SetupTool.Tests;

public class SherpaStateTests
{
    [Fact]
    public void Load_ReturnsEmpty_WhenFileMissing()
    {
        var state = SherpaState.Load("/nonexistent/.sherpa");
        Assert.Empty(state.Installed);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sherpa-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".sherpa");

        var state = new SherpaState();
        state.MarkInstalled("docker");
        state.MarkInstalled("portainer");
        state.Save(path);

        var loaded = SherpaState.Load(path);
        Assert.Equal(["docker", "portainer"], loaded.Installed);
        Assert.True(loaded.IsInstalled("docker"));
        Assert.False(loaded.IsInstalled("web"));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void MarkInstalled_IsIdempotent()
    {
        var state = new SherpaState();
        state.MarkInstalled("docker");
        state.MarkInstalled("docker");
        Assert.Single(state.Installed);
    }

    [Fact]
    public void Load_HandlesCorruptFile_Gracefully()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sherpa-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".sherpa");
        File.WriteAllText(path, "not valid toml [[[");

        var state = SherpaState.Load(path);
        Assert.Empty(state.Installed); // treated as empty, not a crash

        Directory.Delete(dir, true);
    }
}

public class OrchestratorStateTests
{
    private static (Orchestrator orch, FakeRunner runner, string statePath) BuildWithState(
        IStepExecutor[] executors, string[] preInstalled)
    {
        var runner = new FakeRunner();
        var dir = Path.Combine(Path.GetTempPath(), "sherpa-orch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var statePath = Path.Combine(dir, ".sherpa");
        var state = new SherpaState();
        foreach (var n in preInstalled)
            state.MarkInstalled(n);
        state.Save(statePath);
        var orch = new Orchestrator(executors, runner, new FakeDownloader(), invoking: null, state, statePath);
        return (orch, runner, statePath);
    }

    [Fact]
    public async Task Skips_Manifest_AlreadyInState()
    {
        var bash = new BashExecutor();
        var (orch, runner, _) = BuildWithState([bash], ["m"]);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [new Step { Type = StepType.Bash, Script = "echo 1" }],
        };

        var report = await orch.RunAsync([manifest]);

        Assert.True(report.Succeeded);
        Assert.Equal(StepOutcome.Skipped, report.Steps[0].Outcome);
        Assert.Empty(runner.Calls); // no steps ran
    }

    [Fact]
    public async Task Marks_Manifest_AfterSuccess()
    {
        var bash = new BashExecutor();
        var (orch, runner, statePath) = BuildWithState([bash], []);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [new Step { Type = StepType.Bash, Script = "echo 1" }],
        };

        await orch.RunAsync([manifest]);

        var state = SherpaState.Load(statePath);
        Assert.True(state.IsInstalled("m"));
    }

    [Fact]
    public async Task DoesNotMark_WhenManifestFails()
    {
        var bash = new BashExecutor();
        var (orch, runner, statePath) = BuildWithState([bash], []);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [new Step { Type = StepType.Bash, Script = "exit 1" }],
        };
        runner.Responses.Enqueue((1, "")); // step fails

        var report = await orch.RunAsync([manifest]);

        Assert.False(report.Succeeded);
        var state = SherpaState.Load(statePath);
        Assert.False(state.IsInstalled("m")); // not marked on failure
    }

    [Fact]
    public async Task ReRun_AfterSuccess_Skips()
    {
        var bash = new BashExecutor();
        var (orch, runner, statePath) = BuildWithState([bash], []);
        var manifest = new Manifest
        {
            Name = "m",
            SourcePath = "/mnt/m.toml",
            Steps = [new Step { Type = StepType.Bash, Script = "echo 1" }],
        };

        // First run: executes.
        await orch.RunAsync([manifest]);
        Assert.Single(runner.Calls);

        // Second run with a fresh orchestrator reading the same state: skips.
        var state2 = SherpaState.Load(statePath);
        var orch2 = new Orchestrator([bash], new FakeRunner(), new FakeDownloader(), null, state2, statePath);
        var report2 = await orch2.RunAsync([manifest]);
        Assert.Equal(StepOutcome.Skipped, report2.Steps[0].Outcome);
    }
}
