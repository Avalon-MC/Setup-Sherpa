namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Adds a custom Debian repository: installs the gpg keyring, then writes a
/// <c>.sources</c> file under <c>/etc/apt/sources.list.d/</c>. Idempotency:
/// if the sources file already exists, the step is skipped.
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

        if (!string.IsNullOrWhiteSpace(ctx.Step.Keyring))
        {
            // Add the gpg key via apt-key-free mechanism (a keyring in trusted.gpg.d).
            var keyringPath = $"/usr/share/keyrings/{name}-archive-keyring.gpg";
            if (!File.Exists(keyringPath))
            {
                bool fetched = await ctx.RunOkAsync("curl", new[]
                {
                    "-fsSL", ctx.Step.Keyring!, "-o", keyringPath,
                }, ct: ct).ConfigureAwait(false);
                if (!fetched)
                    throw new StepFailedException($"failed to fetch repository keyring from {ctx.Step.Keyring}");
            }
            // Write the deb822 .sources file with signed-by pointing at the keyring.
            WriteSourcesFile(name, ctx.Step.Source!, ctx.Step.Components, keyringPath);
        }
        else
        {
            // No keyring: a plain deb line.
            WriteSourcesFile(name, ctx.Step.Source!, ctx.Step.Components, null);
        }

        return StepResult.Completed();
    }

    private static string DeriveName(string source)
    {
        var host = new Uri(source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? source : "https://" + source).Host;
        return host.Split('.')[0];
    }

    private static void WriteSourcesFile(string name, string source, IReadOnlyList<string> components, string? keyringPath)
    {
        string path = $"/etc/apt/sources.list.d/{name}.sources";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Types: deb");
        sb.AppendLine($"URIs: {source}");
        var suites = components.Count > 0 ? components : new[] { "stable" };
        sb.AppendLine($"Suites: {string.Join(' ', suites)}");
        if (keyringPath is not null)
            sb.AppendLine($"Signed-By: {keyringPath}");
        File.WriteAllText(path, sb.ToString());
    }
}
