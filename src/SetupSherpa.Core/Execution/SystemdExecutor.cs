namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Installs a systemd unit file and optionally enables/starts the service.
/// <c>unit</c> is the path to the <c>.service</c> file (relative to the
/// manifest's directory); <c>name</c> is the service name used for
/// enable/start. Idempotency: if the unit file already exists in
/// <c>/etc/systemd/system/</c>, the step is skipped.
/// </summary>
public sealed class SystemdExecutor : IStepExecutor
{
    public StepType Type => StepType.Systemd;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string unit = ResolveUnit(ctx);
        string unitName = Path.GetFileName(unit);
        string systemDir = "/etc/systemd/system";
        string dest = Path.Combine(systemDir, unitName);

        if (File.Exists(dest))
            return StepResult.Skipped($"unit '{unitName}' already installed.");

        if (!File.Exists(unit))
            throw new StepFailedException($"systemd unit '{unit}' does not exist.");

        bool ok = await ctx.RunOkAsync("cp", new[] { unit, dest }, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException($"failed to install unit '{unitName}'.");

        await ctx.RunOkAsync("systemctl", new[] { "daemon-reload" }, ct: ct).ConfigureAwait(false);

        string service = ctx.Step.ServiceName ?? unitName;
        if (ctx.Step.Enable)
        {
            await ctx.RunOkAsync("systemctl", new[] { "enable", service }, ct: ct).ConfigureAwait(false);
        }
        if (ctx.Step.Start)
        {
            await ctx.RunOkAsync("systemctl", new[] { "start", service }, ct: ct).ConfigureAwait(false);
        }

        return StepResult.Completed();
    }

    private static string ResolveUnit(StepContext ctx)
    {
        string u = ctx.Step.Unit!;
        if (Path.IsPathRooted(u))
            return Path.GetFullPath(u);
        var manifestDir = Path.GetDirectoryName(ctx.Manifest.SourcePath) ?? ".";
        return Path.GetFullPath(Path.Combine(manifestDir, u));
    }
}
