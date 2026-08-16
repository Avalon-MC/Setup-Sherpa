namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Runs a bash script via <c>bash -c</c>. Bash is deliberately the one step
/// type that goes through a shell (D5) — it is a script, not a tokenized
/// command. Has no built-in idempotency; the author controls that.
/// </summary>
public sealed class BashExecutor : IStepExecutor
{
    public StepType Type => StepType.Bash;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        bool ok = await ctx.RunOkAsync("bash", new[] { "-c", ctx.Step.Script! }, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException("bash script failed (exit != 0).");
        return StepResult.Completed();
    }
}
