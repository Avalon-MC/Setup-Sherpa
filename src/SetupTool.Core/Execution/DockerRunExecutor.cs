namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Deploys a container via <c>docker run</c>, using a raw command string (D5)
/// tokenized deterministically. Idempotency keys off <c>--name</c>: if a
/// container with that name is running it is skipped; if it exists but is
/// stopped it is started; otherwise it is run. A step without <c>--name</c>
/// cannot be idempotent and is warned about.
/// </summary>
public sealed class DockerRunExecutor : IStepExecutor
{
    public StepType Type => StepType.DockerRun;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var args = CommandTokenizer.Tokenize(ctx.Step.Command!);
        if (args.Count == 0)
            return StepResult.Completed("no docker command tokens; nothing to run.");

        var name = ExtractName(args);

        // Idempotency: only when we have a stable name.
        if (name is not null)
        {
            var state = await GetStateAsync(ctx, name, ct).ConfigureAwait(false);
            switch (state)
            {
                case ContainerState.Running:
                    return StepResult.Skipped($"container '{name}' already running.");
                case ContainerState.Stopped:
                    return await StartAsync(ctx, name, ct).ConfigureAwait(false)
                        ? StepResult.Completed($"container '{name}' existed but was stopped; started it.")
                        : StepResult.Skipped($"container '{name}' start failed.");
            }
        }
        else
        {
            // No --name: can't check idempotency, so a re-run deploys again.
            ctx.Warnings.Add(
                "docker-run step has no --name, so idempotency can't be checked; a re-run will deploy again.");
        }

        bool ok = await ctx.RunOkAsync("docker", args, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException($"docker run failed (exit != 0).");
        return StepResult.Completed();
    }

    private static string? ExtractName(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Count)
                return args[i + 1];
            if (args[i].StartsWith("--name=", StringComparison.Ordinal))
                return args[i]["--name=".Length..];
        }
        return null;
    }

    private async Task<ContainerState> GetStateAsync(StepContext ctx, string name, CancellationToken ct)
    {
        // docker inspect -f '{{.State.Running}}' <name> -> "true"/"false"; exit 1 if absent.
        var result = await ctx.Runner.RunAsync(new ProcessSpec
        {
            FileName = "docker",
            Arguments = new[] { "inspect", "-f", "{{.State.Running}}", name },
            WorkingDirectory = ctx.WorkDir,
            Privilege = ctx.Privilege,
            Interactive = false,
            CaptureOutput = true,
        }, ct).ConfigureAwait(false);

        if (!result.Success)
            return ContainerState.Absent;
        return result.Output?.Trim() == "true" ? ContainerState.Running : ContainerState.Stopped;
    }

    private async Task<bool> StartAsync(StepContext ctx, string name, CancellationToken ct)
        => (await ctx.Runner.RunAsync(new ProcessSpec
        {
            FileName = "docker",
            Arguments = new[] { "start", name },
            WorkingDirectory = ctx.WorkDir,
            Privilege = ctx.Privilege,
            Interactive = false,
        }, ct).ConfigureAwait(false)).Success;
}

internal enum ContainerState { Absent, Running, Stopped }

/// <summary>Raised when a step's process exits nonzero.</summary>
public sealed class StepFailedException : Exception
{
    public StepFailedException(string message) : base(message) { }
}
