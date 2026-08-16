namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Deploys a compose project. The local implementation shells out to
/// <c>docker compose</c>; a future Portainer implementation will target the
/// Portainer API. Manifests do not change between the two (the seam is D7 /
/// "IComposeDeployer now, Portainer later").
/// </summary>
public interface IComposeDeployer
{
    /// <summary>
    /// Deploys the compose project. <paramref name="resolvedFilePath"/> is the
    /// local compose file (already downloaded to temp if it was an @url: ref).
    /// </summary>
    Task<StepResult> DeployAsync(StepContext ctx, string resolvedFilePath, CancellationToken ct);
}

/// <summary>
/// Local docker-compose deployer: runs <c>docker compose -f &lt;file&gt; up -d</c>
/// with the project name. This is the v1 implementation behind the seam.
/// </summary>
public sealed class LocalComposeDeployer : IComposeDeployer
{
    public async Task<StepResult> DeployAsync(StepContext ctx, string resolvedFilePath, CancellationToken ct)
    {
        var args = new List<string> { "compose" };
        if (!string.IsNullOrWhiteSpace(ctx.Step.Project))
        {
            args.Add("-p");
            args.Add(ctx.Step.Project!);
        }
        args.Add("-f");
        args.Add(resolvedFilePath);
        args.Add("up");
        args.Add("-d");

        bool ok = await ctx.RunOkAsync("docker", args, ct: ct).ConfigureAwait(false);
        if (!ok)
            throw new StepFailedException("docker compose up failed (exit != 0).");
        return StepResult.Completed();
    }
}
