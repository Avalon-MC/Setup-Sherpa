namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

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
        // Expand listed $VAR/${VAR} tokens from .env, shell-quoting each value
        // so it's safe inside the script. Unlisted $VAR stays literal (bash
        // handles it natively).
        var script = EnvSubstitution.ExpandBash(ctx.Step.Script!, ctx.Step.ExpansionTokens, ctx.Env);
        bool ok = await ctx.RunOkAsync("bash", new[] { "-c", script }, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException("bash script failed (exit != 0).");
        return StepResult.Completed();
    }
}
