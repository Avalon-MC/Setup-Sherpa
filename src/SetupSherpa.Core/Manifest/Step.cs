namespace SetupSherpa.Core.Manifest;

/// <summary>
/// A single install step within a manifest. Each step maps to one executor.
/// The schema is deliberately loose (raw strings for command-based steps,
/// optional fields per type) so a manifest author isn't forced to decompose a
/// command they already know.
/// </summary>
public sealed class Step
{
    /// <summary>The step type, required.</summary>
    public StepType Type { get; set; }

    /// <summary>
    /// Privilege override. When null, the step type's default applies
    /// (see <see cref="StepDefaults.DefaultPrivilege"/>).
    /// </summary>
    public Privilege? PrivilegeOverride { get; set; }

    /// <summary>
    /// Working directory for this step, or null to inherit the manifest-level
    /// default (or the process cwd if that is also unset).
    /// </summary>
    public string? WorkDir { get; set; }

    /// <summary>Marks the step as interactive (declared handover).</summary>
    public bool Interactive { get; set; }

    // --- apt ---
    public bool Update { get; set; }
    public IReadOnlyList<string> Packages { get; set; } = [];

    // --- repo ---
    public string? Source { get; set; }
    public string? Keyring { get; set; }
    public IReadOnlyList<string> Components { get; set; } = [];
    public string? RepoName { get; set; }

    // --- docker-run / docker-volume ---
    public string? Command { get; set; }

    // --- compose ---
    public string? Project { get; set; }
    public string? File { get; set; }

    // --- bash ---
    public string? Script { get; set; }
}
