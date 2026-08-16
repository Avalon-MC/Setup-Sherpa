namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Deploys a compose project. Resolves the compose file (downloading it when it
/// is an <c>@url:</c> reference per D7), checks idempotency via
/// <c>docker compose ls</c>, then delegates the actual deployment to an
/// <see cref="IComposeDeployer"/> (local docker-compose today; Portainer API later).
/// </summary>
public sealed class ComposeExecutor : IStepExecutor
{
    private readonly IComposeDeployer _deployer;

    public ComposeExecutor(IComposeDeployer deployer) => _deployer = deployer;

    public StepType Type => StepType.Compose;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string? localFile = null;
        try
        {
            if (ctx.Step.File!.StartsWith("@url:", StringComparison.Ordinal))
            {
                // Fresh-to-temp download, never cached (D7).
                localFile = await ctx.Downloader.DownloadToTempAsync(ctx.Step.File["@url:".Length..], ct).ConfigureAwait(false);
            }
            else
            {
                localFile = Path.IsPathRooted(ctx.Step.File)
                    ? Path.GetFullPath(ctx.Step.File)
                    : Path.GetFullPath(Path.Combine(ctx.Manifest.SourcePath is null ? "." : Path.GetDirectoryName(ctx.Manifest.SourcePath)!, ctx.Step.File));
            }

            // Idempotency: skip if the project is already up.
            if (await ProjectIsRunningAsync(ctx, ct).ConfigureAwait(false))
                return StepResult.Skipped($"compose project '{ctx.Step.Project}' already running.");

            return await _deployer.DeployAsync(ctx, localFile, ct).ConfigureAwait(false);
        }
        finally
        {
            if (localFile is not null && localFile.StartsWith(Path.GetTempPath(), StringComparison.Ordinal))
            {
                try { File.Delete(localFile); } catch { /* best-effort temp cleanup */ }
            }
        }
    }

    private static async Task<bool> ProjectIsRunningAsync(StepContext ctx, CancellationToken ct)
    {
        var result = await ctx.Runner.RunAsync(new ProcessSpec
        {
            FileName = "docker",
            Arguments = new[] { "compose", "ls", "--format", "json" },
            WorkingDirectory = ctx.WorkDir,
            Privilege = ctx.Privilege,
            Interactive = false,
            CaptureOutput = true,
        }, ct).ConfigureAwait(false);
        if (!result.Success)
            return false;
        // The JSON lists running projects; check if ours appears by name.
        return result.Output.Contains($"\"Name\": \"{ctx.Step.Project}\"", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains($"\"name\":\"{ctx.Step.Project}\"", StringComparison.OrdinalIgnoreCase);
    }
}
