namespace SetupTool.Core.Execution;

using SetupTool.Core.Manifest;

/// <summary>
/// Resolves the identity (uid, gid, home, name) a step should run under.
/// When the tool is run via <c>sudo</c>, the invoking user is found via
/// <c>SUDO_USER</c>; that user's home is the target for user steps (D2/D6).
/// </summary>
public static class PrivilegeResolver
{
    /// <summary>
    /// Returns true if the current process is running with elevated privileges.
    /// </summary>
    public static bool IsRoot => Environment.IsPrivilegedProcess;

    /// <summary>
    /// The invoking user's name. When running under sudo this is SUDO_USER;
    /// otherwise it is the current user.
    /// </summary>
    public static string? InvokingUserName =>
        Environment.GetEnvironmentVariable("SUDO_USER") is { Length: > 0 } su ? su : Environment.UserName;

    /// <summary>
    /// The identity the invoking user runs as, resolved from the system passwd
    /// database. Returns null when the identity cannot be resolved.
    /// </summary>
    public static Identity? ResolveInvokingIdentity()
    {
        string? name = InvokingUserName;
        if (name is null)
            return null;
        return Identity.FromUserName(name);
    }

    /// <summary>
    /// Decides whether a step at the given privilege needs a privilege drop.
    /// </summary>
    /// <param name="stepPrivilege">The step's resolved privilege.</param>
    /// <param name="invoking">The invoking user's identity.</param>
    /// <returns>
    /// True when the current process is root and the step runs as the invoking
    /// (non-root) user — i.e. we must drop from root to that user.
    /// </returns>
    public static bool NeedsDrop(Privilege stepPrivilege, Identity invoking)
    {
        if (!IsRoot)
            return false;                 // not root; nothing to drop
        if (stepPrivilege == Privilege.Root)
            return false;                 // root step stays root
        return invoking.Uid != 0;         // user step under root, invoking user not root
    }
}

/// <summary>
/// A resolved OS identity (uid, gid, home directory, username).
/// </summary>
public sealed record Identity(uint Uid, uint Gid, string Home, string Name)
{
    public static Identity? FromUserName(string name)
    {
        // Resolve uid/gid/home from the system passwd database.
        try
        {
            var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "getent",
                    Arguments = $"passwd {name}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            p.Start();
            var line = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            if (p.ExitCode != 0 || line.Length == 0)
                return null;

            // passwd format: name:passwd:uid:gid:gecos:home:shell
            var parts = line.Split(':');
            if (parts.Length < 6)
                return null;
            uint uid = uint.Parse(parts[2]);
            uint gid = uint.Parse(parts[3]);
            string home = parts[5];
            return new Identity(uid, gid, home, parts[0]);
        }
        catch
        {
            return null;
        }
    }
}
