namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Adds a custom Debian repository: installs the gpg keyring, then writes a
/// deb822 <c>.sources</c> file under <c>/etc/apt/sources.list.d/</c>.
/// Idempotency: if the sources file already exists, the step is skipped.
///
/// The deb822 block is faithful to real Debian repo layouts (e.g. Docker's):
/// <c>Suites</c> is the distribution/version path, <c>Components</c> is the
/// archive subdirectory, and <c>Architectures</c> is optional. Suites defaults
/// to <c>$VERSION_CODENAME</c> (which apt expands — never a shell, so it's
/// literal and safe); Components defaults to <c>main</c>.
/// </summary>
public sealed class RepoExecutor : IStepExecutor
{
    public StepType Type => StepType.Repo;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string name = ctx.Step.RepoName ?? DeriveName(ctx.Step.Source!);
        string sourcesPath = $"/etc/apt/sources.list.d/{name}.sources";

        // Idempotency: skip when the sources file already exists.
        if (File.Exists(sourcesPath))
            return StepResult.Skipped($"repository '{name}' already configured ({sourcesPath}).");

        string? keyringPath = null;
        if (!string.IsNullOrWhiteSpace(ctx.Step.Keyring))
        {
            // Add the gpg key via apt-key-free mechanism (a keyring in /usr/share/keyrings).
            keyringPath = $"/usr/share/keyrings/{name}-archive-keyring.gpg";
            if (!File.Exists(keyringPath))
            {
                bool fetched = await ctx.RunOkAsync("curl", new[]
                {
                    "-fsSL", ctx.Step.Keyring!, "-o", keyringPath,
                }, ct: ct).ConfigureAwait(false);
                if (!fetched)
                    throw new StepFailedException($"failed to fetch repository keyring from {ctx.Step.Keyring}");
            }
        }

        WriteSourcesFile(ctx, name, keyringPath);
        return StepResult.Completed();
    }

    private static string DeriveName(string source)
    {
        var host = new Uri(source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? source : "https://" + source).Host;
        return host.Split('.')[0];
    }

    private static void WriteSourcesFile(StepContext ctx, string name, string? keyringPath)
    {
        string sourcesPath = $"/etc/apt/sources.list.d/{name}.sources";
        File.WriteAllText(sourcesPath, BuildDeb822Block(ctx.Step.Source!, ctx.Step.Suite,
            ctx.Step.Components, ctx.Step.Architectures, keyringPath));
    }

    /// <summary>
    /// Renders the deb822 <c>.sources</c> block for a repo step. Pure and
    /// testable (no file I/O). Faithful to real Debian repo layouts:
    /// Suites = distribution/version path, Components = archive subdirectory,
    /// Architectures = optional, Signed-By = keyring path.
    /// </summary>
    public static string BuildDeb822Block(
        string source, string? suite, IReadOnlyList<string> components, string? architectures, string? keyringPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Types: deb");
        sb.AppendLine($"URIs: {source}");

        // Suites is the repo's distribution/version path (e.g. trixie, or
        // $VERSION_CODENAME which apt expands). Never a shell — it's written
        // literally into the file, so $VERSION_CODENAME is safe (D5).
        sb.AppendLine($"Suites: {suite ?? "$VERSION_CODENAME"}");

        // Components is the archive subdirectory (e.g. main, stable, contrib).
        var comps = components.Count > 0 ? components : new[] { "main" };
        sb.AppendLine($"Components: {string.Join(' ', comps)}");

        // Architectures is optional (deb822 defaults to the system architecture).
        if (!string.IsNullOrWhiteSpace(architectures))
            sb.AppendLine($"Architectures: {architectures}");

        if (keyringPath is not null)
            sb.AppendLine($"Signed-By: {keyringPath}");

        return sb.ToString();
    }
}
