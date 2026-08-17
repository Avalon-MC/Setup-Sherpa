using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;
using SetupSherpa.Core.Planning;
using SetupSherpa.Core.State;

namespace SetupSherpa.Cli;

/// <summary>
/// Setup-Sherpa CLI. Usage:
///   sherpa run <manifest.toml> [<manifest2.toml> ...]
/// Loads the named manifests plus their dependencies, plans install order,
/// then executes steps sequentially (root steps as root, user steps dropped
/// to the invoking user). See PLAN.md / DECISIONS.md.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            if (args[0] == "run")
                return await RunAsync(args.Skip(1).ToArray());

            if (args[0] == "plan")
                return await PlanAsync(args.Skip(1).ToArray());

            Console.Error.WriteLine($"Unknown command '{args[0]}'. Use 'sherpa help'.");
            return 2;
        }
        catch (ManifestException ex)
        {
            Console.Error.WriteLine($"  ✗ {ex.Message}");
            return 1;
        }
        catch (PlanException ex)
        {
            Console.Error.WriteLine($"  ✗ {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] targets)
    {
        var ordered = LoadAndPlan(targets, out var stateDir);
        if (ordered is null)
            return 2;

        // Resolve the invoking identity for privilege drops.
        var invoking = PrivilegeResolver.ResolveInvokingIdentity();

        // Fail fast if a user-privilege step exists but we're not root (nothing to drop to),
        // or if running as root but SUDO_USER is missing.
        if (PrivilegeResolver.IsRoot && invoking is null)
        {
            Console.Error.WriteLine(
                "  ✗ Running as root but could not determine the invoking user (SUDO_USER). " +
                "Run via 'sudo sherpa run ...' so user steps can drop privileges.");
            return 1;
        }

        var runner = new ProcessRunner(invoking);
        var executors = new IStepExecutor[]
        {
            new AptExecutor(),
            new RepoExecutor(),
            new DockerRunExecutor(),
            new DockerVolumeExecutor(),
            new ComposeExecutor(new LocalComposeDeployer()),
            new BashExecutor(),
            new WaitExecutor(),
            new EnvInputExecutor(),
            new CopyExecutor(),
            new ExtractExecutor(),
            new SystemdExecutor(),
        };

        // .sherpa state lives in the target directory (the first directory target,
        // or the directory of the first file target). It tracks which manifests
        // are already installed so re-runs skip them.
        SherpaState? state = null;
        string? statePath = null;
        if (stateDir is not null)
        {
            statePath = Path.Combine(stateDir, ".sherpa");
            state = SherpaState.Load(statePath);
        }

        var orchestrator = new Orchestrator(executors, runner, new HttpDownloader(), invoking, state, statePath, stateDir);

        Console.WriteLine($"Plan: {string.Join(" → ", ordered.Select(m => m.Name))}");
        Console.WriteLine();

        var report = await orchestrator.RunAsync(ordered);

        Console.WriteLine();
        if (report.Succeeded)
        {
            Console.WriteLine("✓ All steps completed.");
            return 0;
        }

        Console.WriteLine("✗ Run stopped at a failed step. Because steps are idempotent,");
        Console.WriteLine("  you can re-run and it will resume from the failure.");
        return 1;
    }

    /// <summary>
    /// Plan-only mode: resolves the manifest set and install order, dumps the
    /// ordered plan, and exits WITHOUT installing anything or touching .sherpa.
    /// </summary>
    private static async Task<int> PlanAsync(string[] targets)
    {
        var ordered = LoadAndPlan(targets, out _);
        if (ordered is null)
            return 2;

        Console.WriteLine("Install order:");
        foreach (var m in ordered)
            Console.WriteLine($"  {m.Name}" + (m.InstallOrder != 0 ? $"  (installOrder={m.InstallOrder})" : ""));
        return 0;
    }

    /// <summary>
    /// Shared between <c>run</c> and <c>plan</c>: expands targets into manifest
    /// files, loads them plus their dependencies, and computes install order.
    /// On a bad target or empty set, prints an error and returns null.
    /// </summary>
    private static IReadOnlyList<Manifest>? LoadAndPlan(string[] targets, out string? stateDir)
    {
        stateDir = null;
        if (targets.Length == 0)
        {
            Console.Error.WriteLine("  ✗ requires a manifest directory or file.");
            return null;
        }

        // Expand each target: a directory yields all its .toml files (recursively,
        // up to 2 sub-directories deep); a file is used as-is.
        var manifestPaths = new List<string>();
        var searchDirs = new List<string>();
        foreach (var t in targets)
        {
            if (Directory.Exists(t))
            {
                searchDirs.Add(t);
                manifestPaths.AddRange(FindManifests(t, maxDepth: 2));
            }
            else if (File.Exists(t))
            {
                manifestPaths.Add(t);
                searchDirs.Add(Path.GetDirectoryName(Path.GetFullPath(t)) ?? ".");
            }
            else
            {
                throw new ManifestException($"Target '{t}' is neither a directory nor a manifest file.");
            }
        }

        if (manifestPaths.Count == 0)
            throw new ManifestException("No .toml manifests found in the given target(s).");

        // Load the requested manifests.
        var byName = new Dictionary<string, Manifest>(StringComparer.Ordinal);
        var rootManifests = new List<Manifest>();
        foreach (var p in manifestPaths)
        {
            var m = ManifestLoader.Load(p);
            if (!byName.TryAdd(m.Name, m))
                throw new ManifestException($"Duplicate manifest name '{m.Name}' from '{p}'.");
            rootManifests.Add(m);
        }

        // Resolve dependencies by loading referenced manifests from the target tree.
        LoadDependencies(rootManifests, byName, searchDirs);

        // Plan install order (topological sort; detects cycles).
        var ordered = new DependencyPlanner().Plan(byName);

        stateDir = targets
            .Select(t => Directory.Exists(t) ? t : Path.GetDirectoryName(Path.GetFullPath(t)))
            .FirstOrDefault(d => d is not null);
        return ordered;
    }

    /// <summary>
    /// Recursively loads dependencies referenced by the root manifests and each
    /// discovered dependency. Resolution is by the manifest's <c>name</c> field
    /// (which may differ from its filename): every .toml in the target tree is
    /// loaded and matched on its <c>name</c>.
    /// </summary>
    private static void LoadDependencies(List<Manifest> roots, Dictionary<string, Manifest> byName, List<string> searchDirs)
    {
        var queue = new Queue<Manifest>(roots);
        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            foreach (var dep in m.Depends)
            {
                if (byName.ContainsKey(dep))
                    continue;

                // Search the whole target tree (up to 2 levels deep) for a
                // manifest whose `name` matches the dependency.
                foreach (var dir in searchDirs)
                {
                    foreach (var candidate in FindManifests(dir, maxDepth: 2))
                    {
                        var depManifest = ManifestLoader.Load(candidate);
                        if (depManifest.Name == dep && byName.TryAdd(depManifest.Name, depManifest))
                        {
                            queue.Enqueue(depManifest);
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds all <c>*.toml</c> files under <paramref name="root"/>, recursing up
    /// to <paramref name="maxDepth"/> sub-directories deep (0 = just the root).
    /// </summary>
    private static IEnumerable<string> FindManifests(string root, int maxDepth)
    {
        var results = new List<string>();
        Walk(root, 0);
        return results.OrderBy(f => f, StringComparer.Ordinal);

        void Walk(string dir, int depth)
        {
            results.AddRange(Directory.GetFiles(dir, "*.toml"));
            if (depth >= maxDepth)
                return;
            foreach (var sub in Directory.GetDirectories(dir))
                Walk(sub, depth + 1);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Setup-Sherpa — install a set of TOML manifests in dependency order.

            Usage:
              sherpa run <directory> [<directory2> ...]
                  Load every .toml manifest in the directory (plus their
                  dependencies), plan install order, and execute steps.
                  Run via sudo so user steps can drop privileges.

              sherpa run <manifest.toml> [<manifest2.toml> ...]
                  Load specific manifest files instead of a whole directory.

              sherpa plan <directory-or-file> [...]
                  Walk the dependency tree and print the ordered install plan.
                  Does NOT install anything and does not touch .sherpa.
            """);
    }
}
