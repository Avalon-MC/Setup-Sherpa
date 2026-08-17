using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;

namespace SetupSherpa.Tests;

public class WaitExecutorTests
{
    [Fact]
    public async Task Wait_ReturnsCompleted()
    {
        var step = new Step { Type = StepType.Wait, Message = "Do the thing, then press Enter." };
        var ctx = TestContext.Make(step);
        var executor = new WaitExecutor(input: new StringReader("\n"), output: TextWriter.Null);

        var result = await executor.ExecuteAsync(ctx, default);

        Assert.Equal(StepOutcome.Completed, result.Outcome);
    }
}

public class EnvInputExecutorTests
{
    [Fact]
    public async Task EnvInput_WritesToEnvFile_AndUpdatesInMemoryMap()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-envinput-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var envPath = Path.Combine(dir, ".env");
        File.WriteAllText(envPath, "EXISTING=old\n");
        var env = new Dictionary<string, string> { ["EXISTING"] = "old" };
        var step = new Step { Type = StepType.EnvInput, Variable = "PORTAINER_API_TOKEN", Secret = false };
        var ctx = TestContext.Make(step, env: env, envPath: envPath);
        var executor = new EnvInputExecutor(input: new StringReader("tok-12345\n"), output: TextWriter.Null);

        var result = await executor.ExecuteAsync(ctx, default);
        Assert.Equal(StepOutcome.Completed, result.Outcome);

        // In-memory map updated for later steps in the same run.
        Assert.Equal("tok-12345", env["PORTAINER_API_TOKEN"]);
        // Existing key preserved.
        Assert.Equal("old", env["EXISTING"]);

        // .env file updated on disk.
        var lines = File.ReadAllLines(envPath);
        Assert.Contains("PORTAINER_API_TOKEN=tok-12345", lines);
        Assert.Contains("EXISTING=old", lines);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EnvInput_ReplacesExistingKey_InEnvFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sherpa-envinput-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var envPath = Path.Combine(dir, ".env");
        File.WriteAllText(envPath, "TOKEN=first\n");
        var env = new Dictionary<string, string> { ["TOKEN"] = "first" };
        var step = new Step { Type = StepType.EnvInput, Variable = "TOKEN", Secret = false };
        var ctx = TestContext.Make(step, env: env, envPath: envPath);
        var executor = new EnvInputExecutor(input: new StringReader("second\n"), output: TextWriter.Null);

        await executor.ExecuteAsync(ctx, default);

        var lines = File.ReadAllLines(envPath);
        Assert.Single(lines, l => l.StartsWith("TOKEN="));
        Assert.Contains("TOKEN=second", lines);
        Assert.Equal("second", env["TOKEN"]);

        Directory.Delete(dir, recursive: true);
    }
}
