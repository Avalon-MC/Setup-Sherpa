namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Creates a docker volume via <c>docker volume create</c> (raw command string,
/// D5). Idempotency: parses the volume name from the command and checks
/// <c>docker volume inspect</c> first.
/// </summary>
public sealed class DockerVolumeExecutor : IStepExecutor
{
    public StepType Type => StepType.DockerVolume;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var args = CommandTokenizer.Tokenize(ctx.Step.Command!);
        if (args.Count == 0)
            return StepResult.Completed("no docker command tokens; nothing to run.");

        var name = ExtractVolumeName(args);

        if (name is not null)
        {
            bool exists = await VolumeExistsAsync(ctx, name, ct).ConfigureAwait(false);
            if (exists)
                return StepResult.Skipped($"volume '{name}' already exists.");
        }

        bool ok = await ctx.RunOkAsync("docker", args, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException("docker volume create failed (exit != 0).");
        return StepResult.Completed();
    }

    private static string? ExtractVolumeName(IReadOnlyList<string> args)
    {
        // "create" is the first token; the volume name is the next arg.
        if (args.Count >= 2 && args[0] == "create")
            return args[1];
        return null;
    }

    private async Task<bool> VolumeExistsAsync(StepContext ctx, string name, CancellationToken ct)
    {
        var result = await ctx.Runner.RunAsync(new ProcessSpec
        {
            FileName = "docker",
            Arguments = new[] { "volume", "inspect", name },
            WorkingDirectory = ctx.WorkDir,
            Privilege = ctx.Privilege,
            Interactive = false,
            CaptureOutput = true,
        }, ct).ConfigureAwait(false);
        return result.Success;
    }
}
