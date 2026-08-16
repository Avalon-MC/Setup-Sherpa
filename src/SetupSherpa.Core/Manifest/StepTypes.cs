namespace SetupSherpa.Core.Manifest;

/// <summary>
/// Maps between the manifest string names (e.g. "docker-run", "apt") and the
/// <see cref="StepType"/> enum. Uses the kebab-case names the manifest format
/// defines; a plain Enum.TryParse would fail on the hyphen in "docker-run".
/// </summary>
public static class StepTypes
{
    private static readonly IReadOnlyDictionary<string, StepType> ByName =
        new Dictionary<string, StepType>(StringComparer.OrdinalIgnoreCase)
        {
            ["apt"] = StepType.Apt,
            ["repo"] = StepType.Repo,
            ["docker-run"] = StepType.DockerRun,
            ["docker-volume"] = StepType.DockerVolume,
            ["compose"] = StepType.Compose,
            ["bash"] = StepType.Bash,
        };

    public static bool TryParse(string? name, out StepType type)
    {
        if (name is not null && ByName.TryGetValue(name, out type))
            return true;
        type = default;
        return false;
    }

    public static string ToName(this StepType type) => type switch
    {
        StepType.Apt => "apt",
        StepType.Repo => "repo",
        StepType.DockerRun => "docker-run",
        StepType.DockerVolume => "docker-volume",
        StepType.Compose => "compose",
        StepType.Bash => "bash",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
