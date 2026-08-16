namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Installs packages via apt-get. With <c>update = true</c> it runs
/// <c>apt-get update</c> first. Idempotency: runs <c>dpkg-query -s</c> for each
/// package; a step is skipped when every package is already installed.
/// Non-interactive via DEBIAN_FRONTEND=noninteractive unless declared interactive.
/// </summary>
public sealed class AptExecutor : IStepExecutor
{
    public StepType Type => StepType.Apt;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        // Idempotency: skip when all packages are installed.
        var allInstalled = true;
        foreach (var pkg in ctx.Step.Packages)
        {
            if (!await IsInstalledAsync(ctx, pkg, ct).ConfigureAwait(false))
            {
                allInstalled = false;
                break;
            }
        }
        if (allInstalled)
            return StepResult.Skipped($"all packages already installed: {string.Join(", ", ctx.Step.Packages)}");

        var env = NonInteractiveEnv(ctx);

        if (ctx.Step.Update)
        {
            if (!await ctx.RunOkAsync("apt-get", new[] { "update" }, env: env, ct: ct).ConfigureAwait(false))
                throw new StepFailedException("apt-get update failed (exit != 0).");
        }

        var installArgs = new List<string> { "install", "-y" };
        installArgs.AddRange(ctx.Step.Packages);
        if (!await ctx.RunOkAsync("apt-get", installArgs, env: env, ct: ct).ConfigureAwait(false))
            throw new StepFailedException("apt-get install failed (exit != 0).");

        return StepResult.Completed();
    }

    private static IReadOnlyDictionary<string, string> NonInteractiveEnv(StepContext ctx)
    {
        // If the step is declared interactive, let it prompt normally; otherwise
        // suppress debconf prompts so the install doesn't hang waiting for input.
        if (ctx.Step.Interactive)
            return new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "teletype" };
        return new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" };
    }

    private static async Task<bool> IsInstalledAsync(StepContext ctx, string pkg, CancellationToken ct)
    {
        var result = await ctx.Runner.RunAsync(new ProcessSpec
        {
            FileName = "dpkg-query",
            Arguments = new[] { "-s", pkg },
            WorkingDirectory = ctx.WorkDir,
            Privilege = ctx.Privilege,
            Interactive = false,
            CaptureOutput = true,
        }, ct).ConfigureAwait(false);
        return result.Success;
    }
}
