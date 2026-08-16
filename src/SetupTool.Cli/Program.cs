using SetupTool.Core.Execution;
using SetupTool.Core.Manifest;
using SetupTool.Core.Planning;

namespace SetupTool.Cli;

/// <summary>
/// SetupTool CLI. Usage:
///   setuptool run <manifest.toml> [<manifest2.toml> ...]
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

            Console.Error.WriteLine($"Unknown command '{args[0]}'. Use 'setuptool help'.");
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

    private static async Task<int> RunAsync(string[] paths)
    {
        if (paths.Length == 0)
        {
            Console.Error.WriteLine("  ✗ run requires at least one manifest path.");
            PrintUsage();
            return 2;
        }

        // Load the requested manifests.
        var byName = new Dictionary<string, Manifest>(StringComparer.Ordinal);
        var rootManifests = new List<Manifest>();
        foreach (var p in paths)
        {
            var m = ManifestLoader.Load(p);
            if (!byName.TryAdd(m.Name, m))
                throw new ManifestException($"Duplicate manifest name '{m.Name}' from '{p}'.");
            rootManifests.Add(m);
        }

        // Resolve dependencies by loading referenced manifests from the same directory.
        LoadDependencies(rootManifests, byName);

        // Plan install order (topological sort; detects cycles).
        var ordered = new DependencyPlanner().Plan(byName);

        // Resolve the invoking identity for privilege drops.
        var invoking = PrivilegeResolver.ResolveInvokingIdentity();

        // Fail fast if a user-privilege step exists but we're not root (nothing to drop to),
        // or if running as root but SUDO_USER is missing.
        if (PrivilegeResolver.IsRoot && invoking is null)
        {
            Console.Error.WriteLine(
                "  ✗ Running as root but could not determine the invoking user (SUDO_USER). " +
                "Run via 'sudo setuptool run ...' so user steps can drop privileges.");
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
        };
        var orchestrator = new Orchestrator(executors, runner, new HttpDownloader(), invoking);

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
    /// Recursively loads dependencies referenced by the root manifests and each
    /// discovered dependency, looking for <c>{name}.toml</c> in the same directory.
    /// </summary>
    private static void LoadDependencies(List<Manifest> roots, Dictionary<string, Manifest> byName)
    {
        var queue = new Queue<Manifest>(roots);
        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            foreach (var dep in m.Depends)
            {
                if (byName.ContainsKey(dep))
                    continue;

                var dir = Path.GetDirectoryName(m.SourcePath) ?? ".";
                var candidate = Path.Combine(dir, dep + ".toml");
                if (!File.Exists(candidate))
                    continue; // planner will error if still missing

                var depManifest = ManifestLoader.Load(candidate);
                if (byName.TryAdd(depManifest.Name, depManifest))
                    queue.Enqueue(depManifest);
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            SetupTool — install a set of TOML manifests in dependency order.

            Usage:
              setuptool run <manifest.toml> [<manifest2.toml> ...]
                  Load manifests (plus their dependencies), plan install order,
                  and execute steps. Run via sudo so user steps can drop privileges.
            """);
    }
}
