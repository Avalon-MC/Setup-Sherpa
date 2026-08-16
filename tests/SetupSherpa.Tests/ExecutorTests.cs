using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;

namespace SetupSherpa.Tests;

public class DockerRunExecutorTests
{
    [Fact]
    public async Task Runs_WhenNoNamePresent()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerRun, Command = "-d --name web -p 8080:80 nginx" };
        var ctx = TestContext.Make(step, runner);

        // Response queue: inspect -> absent (exit 1)
        runner.Responses.Enqueue((1, ""));
        var result = await new DockerRunExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        // inspect call + run call
        Assert.Equal("inspect", runner.Calls[0].Arguments[0]);
        Assert.Equal("docker", runner.Calls[1].FileName);
        Assert.Equal(["-d", "--name", "web", "-p", "8080:80", "nginx"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Skips_WhenContainerRunning()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerRun, Command = "-d --name web nginx" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "true")); // inspect says running

        var result = await new DockerRunExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Single(runner.Calls); // only the inspect; no run
    }

    [Fact]
    public async Task Starts_WhenContainerStopped()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerRun, Command = "-d --name web nginx" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "false")); // inspect says stopped
        runner.Responses.Enqueue((0, ""));      // docker start succeeds

        var result = await new DockerRunExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("start", runner.Calls[1].Arguments[0]);
    }

    [Fact]
    public async Task Warns_WhenNoName()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerRun, Command = "-d nginx" };
        var ctx = TestContext.Make(step, runner);

        var result = await new DockerRunExecutor().ExecuteAsync(ctx, default);

        Assert.Single(ctx.Warnings);
        Assert.Contains("--name", ctx.Warnings[0]);
    }

    [Fact]
    public async Task Handles_NameEqualsForm()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerRun, Command = "-d --name=web nginx" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "true"));

        var result = await new DockerRunExecutor().ExecuteAsync(ctx, default);
        Assert.Equal(StepOutcome.Skipped, result.Outcome);
    }
}

public class DockerVolumeExecutorTests
{
    [Fact]
    public async Task Creates_WhenVolumeAbsent()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerVolume, Command = "create portainer_data" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((1, "")); // inspect says absent

        var result = await new DockerVolumeExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["volume", "inspect", "portainer_data"], runner.Calls[0].Arguments);
        Assert.Equal(["create", "portainer_data"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task Skips_WhenVolumeExists()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.DockerVolume, Command = "create portainer_data" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "")); // inspect says exists

        var result = await new DockerVolumeExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Single(runner.Calls); // only inspect
    }
}

public class AptExecutorTests
{
    [Fact]
    public async Task Skips_WhenAllPackagesInstalled()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Apt, Packages = ["nginx", "curl"] };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "")); // nginx installed
        runner.Responses.Enqueue((0, "")); // curl installed

        var result = await new AptExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Equal(2, runner.Calls.Count); // two dpkg-query, no install
    }

    [Fact]
    public async Task Installs_WhenSomeMissing_WithNonInteractiveEnv()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Apt, Packages = ["nginx", "curl"] };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "")); // nginx installed
        runner.Responses.Enqueue((1, "")); // curl NOT installed

        var result = await new AptExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        // dpkg-query x2, then apt-get install
        Assert.Equal("apt-get", runner.Calls[2].FileName);
        Assert.Equal(["install", "-y", "nginx", "curl"], runner.Calls[2].Arguments);
        Assert.Equal("noninteractive", runner.Calls[2].Environment!["DEBIAN_FRONTEND"]);
    }

    [Fact]
    public async Task RunsUpdate_WhenRequested()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Apt, Update = true, Packages = ["nginx"] };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((1, "")); // nginx NOT installed

        var result = await new AptExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        Assert.Equal(["update"], runner.Calls[1].Arguments);
        Assert.Equal(["install", "-y", "nginx"], runner.Calls[2].Arguments);
    }
}

public class ComposeExecutorTests
{
    [Fact]
    public async Task Downloads_UrlFile_Fresh_ThenDeploys()
    {
        var runner = new FakeRunner();
        var downloader = new FakeDownloader { Path = "/tmp/fake-compose.yaml" };
        var step = new Step
        {
            Type = StepType.Compose,
            Project = "portainer",
            File = "@url:https://downloads.portainer.io/ce-sts/portainer-compose.yaml",
        };
        var ctx = TestContext.Make(step, runner, downloader);
        runner.Responses.Enqueue((1, "")); // compose ls says not running

        var result = await new ComposeExecutor(new LocalComposeDeployer()).ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        Assert.Equal(["https://downloads.portainer.io/ce-sts/portainer-compose.yaml"], downloader.Urls);
        // compose ls, then compose up -f <temp> -p portainer
        var runCall = runner.Calls[1];
        Assert.Equal(["compose", "-p", "portainer", "-f", "/tmp/fake-compose.yaml", "up", "-d"], runCall.Arguments);
    }

    [Fact]
    public async Task Skips_WhenProjectRunning()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Compose, Project = "web", File = "./compose.yaml" };
        var ctx = TestContext.Make(step, runner);
        runner.Responses.Enqueue((0, "\"Name\": \"web\"")); // compose ls lists it

        var result = await new ComposeExecutor(new LocalComposeDeployer()).ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Single(runner.Calls); // only compose ls
    }

    [Fact]
    public async Task Resolves_RelativeFile_AgainstManifestDir()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Compose, Project = "web", File = "./compose.yaml" };
        var ctx = TestContext.Make(step, runner, manifestDir: "/mnt/manifests");
        runner.Responses.Enqueue((1, ""));

        var result = await new ComposeExecutor(new LocalComposeDeployer()).ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        // compose ls (index 0), then compose -p web -f <file> up -d
        Assert.Equal("compose", runner.Calls[1].Arguments[0]);
        Assert.Equal("-f", runner.Calls[1].Arguments[3]);
        Assert.EndsWith("/mnt/manifests/compose.yaml", runner.Calls[1].Arguments[4]);
    }
}

public class BashExecutorTests
{
    [Fact]
    public async Task Runs_ThroughBashDashC()
    {
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Bash, Script = "echo hi\nexit 0" };
        var ctx = TestContext.Make(step, runner);

        var result = await new BashExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        Assert.Equal("bash", runner.Calls[0].FileName);
        Assert.Equal(["-c", "echo hi\nexit 0"], runner.Calls[0].Arguments);
    }
}
