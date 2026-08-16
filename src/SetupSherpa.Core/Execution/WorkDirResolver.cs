namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Resolves a step's working directory per D6:
/// <list type="bullet">
/// <item><c>~/...</c> → the step's effective user's home.</item>
/// <item><c>/...</c> → used as-is (created on demand by the caller).</item>
/// <item><c>./...` or a bare relative name</c> → resolved against the manifest's directory.</item>
/// <item>unset → the process current directory.</item>
/// </list>
/// This is a pure path computation (no I/O); the runner/orchestrator handles
/// <c>mkdir -p</c> with the correct privilege.
/// </summary>
public static class WorkDirResolver
{
    /// <summary>
    /// Computes the absolute working directory for a step.
    /// </summary>
    /// <param name="spec">The step's workdir value, or null to use the manifest default.</param>
    /// <param name="manifestDir">Absolute directory of the manifest file.</param>
    /// <param name="effectiveHome">Home of the step's effective user (user steps → the invoking user's home).</param>
    /// <param name="defaultDir">The fallback when nothing is specified (process cwd).</param>
    public static string Resolve(string? spec, string manifestDir, string effectiveHome, string defaultDir)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return defaultDir;

        spec = spec!.Trim();

        if (spec == "~")
            return effectiveHome;
        if (spec.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(effectiveHome, spec[2..].TrimStart('/'));

        if (Path.IsPathRooted(spec))
            return Path.GetFullPath(spec);

        // Relative: resolve against the manifest's directory.
        return Path.GetFullPath(Path.Combine(manifestDir, spec));
    }
}
