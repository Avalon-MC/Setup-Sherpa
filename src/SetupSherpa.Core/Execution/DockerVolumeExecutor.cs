namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

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
        // Expand listed $VAR/${VAR} tokens from .env BEFORE tokenization.
        var command = EnvSubstitution.ExpandCommand(ctx.Step.Command!, ctx.Step.ExpansionTokens, ctx.Env);
        var args = CommandTokenizer.Tokenize(command);
        if (args.Count == 0)
            return StepResult.Completed("no docker command tokens; nothing to run.");

        // The step type is `docker-volume`, so the command is the args AFTER
        // `docker volume` — prepend `volume` so the author writes `create <name>`.
        var volumeArgs = new List<string> { "volume" };
        volumeArgs.AddRange(args);
        args = volumeArgs;

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
        // args = ["volume", "create", <name>, ...]; the name is the 3rd token.
        if (args.Count >= 3 && args[0] == "volume" && args[1] == "create")
            return args[2];
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
