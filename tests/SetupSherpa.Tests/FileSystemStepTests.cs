using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;

namespace SetupSherpa.Tests;

public class CopyExecutorTests
{
    [Fact]
    public async Task Copy_CopiesFile_ToDest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "src.txt");
        var dest = Path.Combine(dir, "out", "dest.txt");
        File.WriteAllText(src, "hello");
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Copy, Src = src, Dest = dest };
        var ctx = TestContext.Make(step, runner, manifestDir: dir);

        var result = await new CopyExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        // cp -r <src> <dest> was invoked.
        var call = runner.Calls[0];
        Assert.Equal("cp", call.FileName);
        Assert.Contains(src, call.Arguments);
        Assert.Contains(dest, call.Arguments);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Copy_Skips_WhenDestExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "existing");
        Directory.CreateDirectory(dest);
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Copy, Src = "whatever", Dest = dest };
        var ctx = TestContext.Make(step, runner, manifestDir: dir);

        var result = await new CopyExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Empty(runner.Calls);

        Directory.Delete(dir, recursive: true);
    }
}

public class ExtractExecutorTests
{
    [Fact]
    public async Task Extract_ExtractsArchive_ToDest()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var archive = Path.Combine(dir, "bundle.tar.gz");
        var dest = Path.Combine(dir, "out");
        // Create a real tar.gz with one file.
        var payload = Path.Combine(dir, "payload.txt");
        File.WriteAllText(payload, "data");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "tar",
            ArgumentList = { "-czf", archive, "-C", dir, "payload.txt" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }))
        {
            p!.WaitForExit();
        }
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Extract, Archive = archive, Dest = dest };
        var ctx = TestContext.Make(step, runner, manifestDir: dir);

        var result = await new ExtractExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        var call = runner.Calls[0];
        Assert.Equal("tar", call.FileName);
        Assert.Contains("-xzf", call.Arguments);
        Assert.Contains(archive, call.Arguments);
        Assert.Contains(dest, call.Arguments);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Extract_Skips_WhenDestPopulated()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "out");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "x"), "y");
        var runner = new FakeRunner();
        var step = new Step { Type = StepType.Extract, Archive = "nope.tar.gz", Dest = dest };
        var ctx = TestContext.Make(step, runner, manifestDir: dir);

        var result = await new ExtractExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Skipped, result.Outcome);
        Assert.Empty(runner.Calls);

        Directory.Delete(dir, recursive: true);
    }
}

public class SystemdExecutorTests
{
    [Fact]
    public async Task Systemd_InstallsUnit_AndEnablesStarts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-systemd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var unit = Path.Combine(dir, "myapp.service");
        File.WriteAllText(unit, "[Unit]\nDescription=test\n");
        var runner = new FakeRunner();
        var step = new Step
        {
            Type = StepType.Systemd,
            Unit = unit,
            ServiceName = "myapp",
            Enable = true,
            Start = true,
        };
        var ctx = TestContext.Make(step, runner, manifestDir: dir);

        var result = await new SystemdExecutor().ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
        // cp unit -> /etc/systemd/system, daemon-reload, enable, start
        Assert.Equal("cp", runner.Calls[0].FileName);
        Assert.Contains("systemctl", runner.Calls.Select(c => c.FileName));
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("enable") && c.Arguments.Contains("myapp"));
        Assert.Contains(runner.Calls, c => c.Arguments.Contains("start") && c.Arguments.Contains("myapp"));

        Directory.Delete(dir, recursive: true);
    }
}
