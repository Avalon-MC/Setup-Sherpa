namespace SetupSherpa.Core.Execution;

using System.Diagnostics;
using SetupSherpa.Core.Manifest;

/// <summary>
/// The production <see cref="IProcessRunner"/>. Runs processes with correct
/// privilege (dropping from root to the invoking user via setpriv for user
/// steps, D2), and streams output to the real terminal for plain steps.
/// Interactive steps are run under <c>script</c> (a pty) so they can be
/// watched and handed over (D3); the interactive plumbing lives in Phase 4.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly Identity? _invoking;

    public ProcessRunner(Identity? invoking) => _invoking = invoking;

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        bool drop = _invoking is not null && PrivilegeResolver.NeedsDrop(spec.Privilege, _invoking);

        if (spec.Interactive)
            return RunInteractiveAsync(spec, drop, ct);

        var psi = new ProcessStartInfo
        {
            FileName = drop ? "/usr/bin/setpriv" : spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
        };

        if (drop)
        {
            psi.ArgumentList.Add("--reuid");
            psi.ArgumentList.Add(_invoking!.Uid.ToString());
            psi.ArgumentList.Add("--regid");
            psi.ArgumentList.Add(_invoking.Gid.ToString());
            psi.ArgumentList.Add("--init-groups");
            psi.ArgumentList.Add(_invoking.Name);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(spec.FileName);
        }
        foreach (var a in spec.Arguments)
            psi.ArgumentList.Add(a);

        if (spec.CaptureOutput)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }
        // else: inherit handles -> streams to the real terminal (apt gets its tty).

        ApplyEnvironment(psi, spec);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        string? output = null;
        if (spec.CaptureOutput)
        {
            // Read both streams without deadlock.
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            output = stdout.Result + stderr.Result;
        }
        else
        {
            proc.WaitForExit();
        }

        return Task.FromResult(new ProcessResult
        {
            ExitCode = proc.ExitCode,
            Output = output ?? "",
        });
    }

    private static void ApplyEnvironment(ProcessStartInfo psi, ProcessSpec spec)
    {
        if (spec.Environment is null)
            return;
        foreach (var (k, v) in spec.Environment)
            psi.Environment[k] = v;
    }

    /// <summary>
    /// Runs an interactive step under <c>script</c> so it gets a real pty.
    /// The child's stdin is inherited from the tool, so the user's keyboard is
    /// already attached and they can type at it directly. stdout/stderr are
    /// redirected so the <see cref="InteractiveWatchdog"/> can relay them and
    /// offer a takeover hint when the step appears stalled (D3).
    /// </summary>
    private async Task<ProcessResult> RunInteractiveAsync(ProcessSpec spec, bool drop, CancellationToken ct)
    {
        // Build the argv, then serialize to a shell-safe command string for
        // `script -c`. `script` hands the string to /bin/sh -c.
        var argv = new List<string>();
        if (drop)
        {
            argv.Add("/usr/bin/setpriv");
            argv.Add("--reuid"); argv.Add(_invoking!.Uid.ToString());
            argv.Add("--regid"); argv.Add(_invoking.Gid.ToString());
            argv.Add("--init-groups"); argv.Add(_invoking.Name);
            argv.Add("--");
            argv.Add(spec.FileName);
        }
        else
        {
            argv.Add(spec.FileName);
        }
        argv.AddRange(spec.Arguments);

        string commandLine = ShellQuote(argv);
        var psi = new ProcessStartInfo
        {
            FileName = "script",
            Arguments = $"-qefc \"{commandLine}\" /dev/null",
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            // Keyboard stays connected to the child through `script`'s pty.
            RedirectStandardInput = false,
            // Capture output so we can relay + watch for a stall.
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ApplyEnvironment(psi, spec);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var watchdog = new InteractiveWatchdog(
            stallThreshold: TimeSpan.FromSeconds(8),
            rearmDelay: TimeSpan.FromSeconds(30));
        await watchdog.RunAndWatchAsync(proc.StandardOutput, ct).ConfigureAwait(false);
        // Drain stderr (script usually merges; keep it harmless).
        var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
        if (stderr.Length > 0)
            Console.Write(stderr);
        proc.WaitForExit();
        return new ProcessResult { ExitCode = proc.ExitCode };
    }

    /// <summary>
    /// Quotes each argument so the list can be safely passed to <c>sh -c</c>
    /// via <c>script</c>. This is the one place we touch a shell, and only for
    /// the interactive/pty path where a shell is required.
    /// </summary>
    private static string ShellQuote(IReadOnlyList<string> argv)
    {
        var parts = argv.Select(a =>
        {
            if (a.Length > 0 && a.All(c => char.IsLetterOrDigit(c) || "-_./:=@".Contains(c)))
                return a;
            return "'" + a.Replace("'", "'\"'\"'") + "'";
        });
        return string.Join(" ", parts);
    }
}
