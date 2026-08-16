namespace SetupTool.Core.Manifest;

/// <summary>
/// The type of an install step. Maps to a concrete executor in
/// <see cref="Execution.IStepExecutor"/>.
/// </summary>
public enum StepType
{
    Apt,
    Repo,
    DockerRun,
    DockerVolume,
    Compose,
    Bash,
}

/// <summary>
/// The privilege a step runs under. Defaults to the step type's default
/// (<see cref="StepDefaults"/>); a step may override via the manifest.
/// </summary>
public enum Privilege
{
    Root,
    User,
}

/// <summary>
/// The default privilege for each step type. This is the "sane default" a
/// normal user rarely needs to override.
/// </summary>
public static class StepDefaults
{
    public static Privilege DefaultPrivilege(this StepType type) => type switch
    {
        StepType.Apt or StepType.Repo or StepType.DockerRun or StepType.DockerVolume or StepType.Compose
            => Privilege.Root,
        StepType.Bash => Privilege.User,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
