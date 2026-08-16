namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Describes a process the runner should execute on behalf of a step.
/// The runner is responsible for privilege handling (see Phase 3) and for
/// allocating/relaying a pty so interactive programs behave like a real terminal.
/// </summary>
public sealed class ProcessSpec
{
    /// <summary>Executable to run (resolved on PATH or absolute).</summary>
    public required string FileName { get; init; }

    /// <summary>Arguments, already tokenized. Never shell-interpreted.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Working directory (absolute, resolved).</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Privilege this step runs under.</summary>
    public required Privilege Privilege { get; init; }

    /// <summary>When true, the process needs a human at the terminal.</summary>
    public bool Interactive { get; init; }

    /// <summary>Environment overrides to set (e.g. DEBIAN_FRONTEND).</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// When true, the runner captures stdout as text into
    /// <see cref="ProcessResult.Output"/> instead of streaming it to the
    /// terminal. Used by idempotency checks. Never combined with a pty relay.
    /// </summary>
    public bool CaptureOutput { get; init; }
}

/// <summary>The result of running a single process.</summary>
public sealed class ProcessResult
{
    public required int ExitCode { get; init; }
    public bool Success => ExitCode == 0;

    /// <summary>
    /// The process's standard output, captured as text. Only populated when the
    /// runner is in capture mode (used by idempotency checks); a pty-relay run
    /// that streams to the terminal leaves this empty.
    /// </summary>
    public string Output { get; init; } = "";
}

/// <summary>
/// Executes a <see cref="ProcessSpec"/>. The concrete implementation handles
/// privilege dropping, pty allocation/relay, and interactive handover.
/// Executors depend on this abstraction so they can be tested with a fake.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
}
