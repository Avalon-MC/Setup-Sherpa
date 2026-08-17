namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Extracts a <c>.tar.gz</c> archive into a destination directory. The archive
/// is resolved relative to the manifest's directory; <c>dest</c> is absolute.
/// Idempotency: if the destination directory already exists and is non-empty,
/// the step is skipped.
/// </summary>
public sealed class ExtractExecutor : IStepExecutor
{
    public StepType Type => StepType.Extract;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string archive = ResolveArchive(ctx);
        string dest = ctx.Step.Dest!;

        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            return StepResult.Skipped($"'{dest}' already populated.");

        if (!File.Exists(archive))
            throw new StepFailedException($"extract archive '{archive}' does not exist.");

        Directory.CreateDirectory(dest);

        bool ok = await ctx.RunOkAsync("tar", new[] { "-xzf", archive, "-C", dest }, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException($"extract '{archive}' -> '{dest}' failed (exit != 0).");
        return StepResult.Completed();
    }

    private static string ResolveArchive(StepContext ctx)
    {
        string arc = ctx.Step.Archive!;
        if (Path.IsPathRooted(arc))
            return Path.GetFullPath(arc);
        var manifestDir = Path.GetDirectoryName(ctx.Manifest.SourcePath) ?? ".";
        return Path.GetFullPath(Path.Combine(manifestDir, arc));
    }
}
