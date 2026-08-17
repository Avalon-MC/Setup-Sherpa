namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Copies a file or directory tree into place. <c>source</c> is resolved
/// relative to the manifest's directory (so extra files live alongside the
/// manifest); <c>dest</c> is an absolute path. Idempotency: if the destination
/// already exists, the step is skipped.
/// </summary>
public sealed class CopyExecutor : IStepExecutor
{
    public StepType Type => StepType.Copy;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string source = ResolveSource(ctx);
        string dest = ctx.Step.Dest!;
        if (File.Exists(dest) || Directory.Exists(dest))
            return StepResult.Skipped($"'{dest}' already exists.");

        if (!File.Exists(source) && !Directory.Exists(source))
            throw new StepFailedException($"copy source '{source}' does not exist.");

        // Ensure the parent of dest exists.
        var parent = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        bool ok = await ctx.RunOkAsync("cp", new[] { "-r", source, dest }, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException($"copy '{source}' -> '{dest}' failed (exit != 0).");
        return StepResult.Completed();
    }

    private static string ResolveSource(StepContext ctx)
    {
        string src = ctx.Step.Src!;
        if (Path.IsPathRooted(src))
            return Path.GetFullPath(src);
        var manifestDir = Path.GetDirectoryName(ctx.Manifest.SourcePath) ?? ".";
        return Path.GetFullPath(Path.Combine(manifestDir, src));
    }
}
