using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;

namespace SetupSherpa.Tests;

/// <summary>
/// A test double for <see cref="IProcessRunner"/>. Records every ProcessSpec
/// it receives and returns a configurable result. A scripted response queue
/// lets tests drive idempotency branches deterministically.
/// </summary>
internal sealed class FakeRunner : IProcessRunner
{
    public List<ProcessSpec> Calls { get; } = [];

    /// <summary>
    /// Queue of (exitCode, output) to return, in order. When empty, returns (0, "").
    /// </summary>
    public Queue<(int exitCode, string output)> Responses { get; } = new();

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        Calls.Add(spec);
        var (code, output) = Responses.Count > 0 ? Responses.Dequeue() : (0, "");
        return Task.FromResult(new ProcessResult { ExitCode = code, Output = output });
    }
}

/// <summary>A fake <see cref="IHttpDownloader"/> that returns a local temp file.</summary>
internal sealed class FakeDownloader : IHttpDownloader
{
    public string Path { get; set; } = "/tmp/fake-compose.yaml";
    public List<string> Urls { get; } = [];

    public Task<string> DownloadToTempAsync(string url, CancellationToken ct = default)
    {
        Urls.Add(url);
        return Task.FromResult(Path);
    }
}

internal static class TestContext
{
    public static StepContext Make(Step step, FakeRunner? runner = null, IHttpDownloader? downloader = null,
        string? manifestDir = null, Dictionary<string, string>? env = null, string? envPath = null)
    {
        var r = runner ?? new FakeRunner();
        var d = downloader ?? new FakeDownloader();
        var manifest = new Manifest
        {
            Name = "test",
            SourcePath = Path.Combine(manifestDir ?? "/mnt/manifests", "test.toml"),
            Steps = [step],
        };
        return new StepContext
        {
            Step = step,
            Manifest = manifest,
            WorkDir = "/mnt/manifests",
            Privilege = step.PrivilegeOverride ?? StepDefaults.DefaultPrivilege(step.Type),
            EffectiveHome = "/home/peter",
            Runner = r,
            Downloader = d,
            Env = env,
            EnvPath = envPath,
        };
    }
}
