namespace SetupTool.Core.Manifest;

/// <summary>
/// A single installation manifest: one unit of install. The tool loads these,
/// builds a dependency DAG across them, topologically sorts, then executes
/// steps in order.
/// </summary>
public sealed class Manifest
{
    /// <summary>Unique name; other manifests reference it via <see cref="Depends"/>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Names of other manifests that must install first.</summary>
    public IReadOnlyList<string> Depends { get; set; } = [];

    /// <summary>
    /// Manifest-level working-directory default. Steps inherit this unless they
    /// set their own <see cref="Step.WorkDir"/>. May be null (no default).
    /// </summary>
    public string? WorkDir { get; set; }

    /// <summary>
    /// Installation preference, -100..+100. Higher installs closer to first.
    /// Only affects ordering among manifests whose dependencies are already
    /// satisfied; it never overrides a dependency (a manifest still installs
    /// after its dependencies). Default 0.
    /// </summary>
    public int InstallOrder { get; set; }

    /// <summary>The install steps, in order.</summary>
    public IReadOnlyList<Step> Steps { get; set; } = [];

    /// <summary>The absolute path this manifest was loaded from (for relative-path resolution).</summary>
    public string SourcePath { get; set; } = "";
}
