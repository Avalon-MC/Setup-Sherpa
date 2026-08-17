namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;
using SetupSherpa.Core.Planning;
using SetupSherpa.Core.State;

/// <summary>The outcome of running the whole ordered set of manifests.</summary>
public sealed class RunReport
{
    public required IReadOnlyList<ManifestStepReport> Steps { get; init; }
    public required bool Succeeded { get; init; }
}

/// <summary>Per-step report line.</summary>
public sealed class ManifestStepReport
{
    public required string Manifest { get; init; }
    public required string StepType { get; init; }
    public required int StepNumber { get; init; }
    public required StepOutcome Outcome { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Executes an ordered list of manifests sequentially, step by step.
/// Responsibilities:
/// <list type="bullet">
/// <item>resolves each step's privilege and working directory,</item>
/// <item>creates the workdir on demand (owned by the step's effective user),</item>
/// <item>dispatches to the matching <see cref="IStepExecutor"/>,</item>
/// <item>stops on the first failure (each step is idempotent, so a crash is
/// safely re-runnable).</item>
/// </list>
/// </summary>
public sealed class Orchestrator
{
    private readonly IReadOnlyDictionary<StepType, IStepExecutor> _executors;
    private readonly IProcessRunner _runner;
    private readonly IHttpDownloader _downloader;
    private readonly Identity? _invoking;
    private readonly SherpaState? _state;
    private readonly string? _statePath;
    private readonly string? _envDir;

    public Orchestrator(
        IEnumerable<IStepExecutor> executors,
        IProcessRunner runner,
        IHttpDownloader downloader,
        Identity? invoking,
        SherpaState? state = null,
        string? statePath = null,
        string? envDir = null)
    {
        _executors = executors.ToDictionary(e => e.Type);
        _runner = runner;
        _downloader = downloader;
        _invoking = invoking;
        _state = state;
        _statePath = statePath;
        _envDir = envDir;
    }

    public async Task<RunReport> RunAsync(IReadOnlyList<Manifest> ordered, CancellationToken ct = default)
    {
        var reports = new List<ManifestStepReport>();
        var manifestDefaultDir = Directory.GetCurrentDirectory();

        // Load .env once from the run target directory, if present. A missing
        // .env is fine: compose only gets --env-file when the file exists, and
        // expansionTokens hard-errors if a listed secret is absent. We do NOT
        // auto-create a blank .env — it's a misleading stray file (the scaffold
        // is .env.example) and would only ever mask compose interpolation.
        string? envPath = _envDir is null ? null : Path.Combine(_envDir, ".env");
        Dictionary<string, string>? env = null;
        if (envPath is not null && File.Exists(envPath))
            env = DotEnvFile.Parse(File.ReadAllText(envPath));

        int stepNumber = 0;
        foreach (var manifest in ordered)
        {
            var manifestDir = Path.GetDirectoryName(manifest.SourcePath) ?? ".";

            // .sherpa: skip a manifest that's already marked installed.
            if (_state is not null && _state.IsInstalled(manifest.Name))
            {
                Console.WriteLine($"  · {manifest.Name}: already installed (in .sherpa), skipping.");
                reports.Add(new ManifestStepReport
                {
                    Manifest = manifest.Name,
                    StepType = "manifest",
                    StepNumber = ++stepNumber,
                    Outcome = StepOutcome.Skipped,
                    Note = "already installed (in .sherpa)",
                });
                continue;
            }

            foreach (var step in manifest.Steps)
            {
                stepNumber++;
                var type = step.Type;
                if (!_executors.TryGetValue(type, out var executor))
                {
                    throw new PlanException($"No executor registered for step type '{type}'.");
                }

                var privilege = step.PrivilegeOverride ?? StepDefaults.DefaultPrivilege(type);
                var effectiveHome = EffectiveHomeFor(privilege);

                // Resolve + create the workdir.
                string workDir = WorkDirResolver.Resolve(
                    step.WorkDir ?? manifest.WorkDir, manifestDir, effectiveHome, manifestDefaultDir);
                await EnsureWorkDirAsync(workDir, privilege, ct).ConfigureAwait(false);

                var ctx = new StepContext
                {
                    Step = step,
                    Env = env,
                    EnvPath = envPath,
                    Manifest = manifest,
                    WorkDir = workDir,
                    Privilege = privilege,
                    EffectiveHome = effectiveHome,
                    Runner = _runner,
                    Downloader = _downloader,
                };

                var note = $"  [{stepNumber}] {manifest.Name}:{step.Type.ToName()}";
                Console.WriteLine(note + " …");
                StepResult result;
                try
                {
                    result = await executor.ExecuteAsync(ctx, ct).ConfigureAwait(false);
                }
                catch (StepFailedException ex)
                {
                    Console.WriteLine($"  ✗ {ex.Message}");
                    reports.Add(new ManifestStepReport
                    {
                        Manifest = manifest.Name,
                        StepType = step.Type.ToName(),
                        StepNumber = stepNumber,
                        Outcome = StepOutcome.Completed,
                        Note = "FAILED: " + ex.Message,
                        Warnings = ctx.Warnings,
                    });
                    return new RunReport { Steps = reports, Succeeded = false };
                }

                foreach (var w in ctx.Warnings)
                    Console.WriteLine($"    ⚠ {w}");

                Console.WriteLine(result.Outcome == StepOutcome.Skipped
                    ? $"  · {result.Note}"
                    : $"  ✓ {manifest.Name}:{step.Type.ToName()}");

                reports.Add(new ManifestStepReport
                {
                    Manifest = manifest.Name,
                    StepType = step.Type.ToName(),
                    StepNumber = stepNumber,
                    Outcome = result.Outcome,
                    Note = result.Note,
                    Warnings = ctx.Warnings,
                });
            }

            // All steps of this manifest succeeded — mark it installed in .sherpa.
            if (_state is not null)
            {
                _state.MarkInstalled(manifest.Name);
                if (_statePath is not null)
                    _state.Save(_statePath);
            }
        }

        return new RunReport { Steps = reports, Succeeded = true };
    }

    private string EffectiveHomeFor(Privilege privilege)
    {
        if (privilege == Privilege.Root)
            return "/root";
        return _invoking?.Home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async Task EnsureWorkDirAsync(string dir, Privilege privilege, CancellationToken ct)
    {
        // Create on demand. For user steps under root, drop first so the dir is
        // owned by the user (D6). Use a direct mkdir -p (shell is fine for this).
        if (Directory.Exists(dir))
            return;

        var argv = new List<string> { "mkdir", "-p", dir };
        if (_invoking is not null && PrivilegeResolver.NeedsDrop(privilege, _invoking))
        {
            var cmd = new List<string> { "/usr/bin/setpriv",
                "--reuid", _invoking.Uid.ToString(),
                "--regid", _invoking.Gid.ToString(),
                "--init-groups", _invoking.Name,
                "--", "mkdir", "-p", dir };
            await RunCaptureAsync(cmd, ct).ConfigureAwait(false);
        }
        else
        {
            await RunCaptureAsync(argv, ct).ConfigureAwait(false);
        }
    }

    private async Task RunCaptureAsync(IReadOnlyList<string> argv, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = argv[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        for (int i = 1; i < argv.Count; i++)
            psi.ArgumentList.Add(argv[i]);
        using var p = new System.Diagnostics.Process { StartInfo = psi };
        p.Start();
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        await Task.WhenAll(o, e).ConfigureAwait(false);
    }
}
