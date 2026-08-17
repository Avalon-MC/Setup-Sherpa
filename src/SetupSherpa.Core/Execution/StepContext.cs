namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Carries everything an executor needs to run one step. Constructed by the
/// orchestrator, which has already resolved privilege and working directory.
/// </summary>
public sealed class StepContext
{
    public required Step Step { get; init; }
    public required Manifest Manifest { get; init; }
    public required string WorkDir { get; init; }
    public required Privilege Privilege { get; init; }
    public required string EffectiveHome { get; init; }
    public required IProcessRunner Runner { get; init; }
    public required IHttpDownloader Downloader { get; init; }

    /// <summary>
    /// The parsed `.env` map from the run target directory, or null when no
    /// `.env` was present / no step needs it. Loaded once by the orchestrator.
    /// Mutable so an <c>env-input</c> step can add/update a value that later
    /// steps in the same run see immediately.
    /// </summary>
    public Dictionary<string, string>? Env { get; init; }

    /// <summary>Absolute path to the `.env` file (may not exist).</summary>
    public string? EnvPath { get; init; }

    /// <summary>Warnings surfaced for this step (e.g. a docker-run without --name).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Runs a process with the step's privilege + workdir and returns success.</summary>
    public async Task<bool> RunOkAsync(string file, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var result = await Runner.RunAsync(new ProcessSpec
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = WorkDir,
            Privilege = Privilege,
            Interactive = Step.Interactive,
            Environment = env,
        }, ct);
        return result.Success;
    }
}

/// <summary>Whether a step executed or was skipped because it was already satisfied.</summary>
public enum StepOutcome { Skipped, Completed }

public sealed class StepResult
{
    public required StepOutcome Outcome { get; init; }
    public string? Note { get; init; }

    public static StepResult Skipped(string note) => new() { Outcome = StepOutcome.Skipped, Note = note };
    public static StepResult Completed(string? note = null) => new() { Outcome = StepOutcome.Completed, Note = note };
}

/// <summary>
/// A per-step-type executor. It decides whether the step is already satisfied
/// (idempotency), then runs the required commands via the runner.
/// </summary>
public interface IStepExecutor
{
    StepType Type { get; }
    Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct);
}
