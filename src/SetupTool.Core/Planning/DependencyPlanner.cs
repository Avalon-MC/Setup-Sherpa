namespace SetupTool.Core.Planning;

using SetupTool.Core.Manifest;

/// <summary>
/// Raised when a dependency graph cannot be planned — a manifest references an
/// unknown dependency, or there is a dependency cycle.
/// </summary>
public sealed class PlanException : Exception
{
    public PlanException(string message) : base(message) { }
}

/// <summary>
/// Computes the installation order for a set of manifests given their
/// dependency relationships. Performs a topological sort over the dependency
/// DAG and detects cycles with a readable message.
/// </summary>
public sealed class DependencyPlanner
{
    /// <summary>
    /// Returns manifests in install order (dependencies first).
    /// </summary>
    /// <param name="manifests">All manifests available, keyed by name.</param>
    /// <exception cref="PlanException">on unknown dependency or cycle.</exception>
    public IReadOnlyList<Manifest> Plan(IReadOnlyDictionary<string, Manifest> manifests)
    {
        // deps[X]   = set of manifests X depends on (must install before X).
        // dependents[X] = set of manifests that depend on X.
        var deps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in manifests.Keys)
        {
            deps[name] = new HashSet<string>(StringComparer.Ordinal);
            dependents[name] = new HashSet<string>(StringComparer.Ordinal);
        }
        foreach (var (name, m) in manifests)
        {
            foreach (var d in m.Depends)
            {
                if (!manifests.ContainsKey(d))
                    throw new PlanException($"Manifest '{name}' depends on unknown manifest '{d}'.");
                deps[name].Add(d);
                dependents[d].Add(name);
            }
        }

        // Kahn's algorithm (iterative, no recursion). A node is ready once all
        // of its dependencies are processed, i.e. in-degree (deps remaining) hits 0.
        var remaining = deps.ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
        var ready = new PriorityQueue<string, string>(Comparer<string>.Create(
            (a, b) => string.CompareOrdinal(a, b)));
        foreach (var (name, deg) in remaining)
            if (deg == 0)
                ready.Enqueue(name, name);

        var order = new List<Manifest>();
        while (ready.Count > 0)
        {
            var name = ready.Dequeue();
            order.Add(manifests[name]);
            foreach (var dependent in dependents[name])
            {
                if (--remaining[dependent] == 0)
                    ready.Enqueue(dependent, dependent);
            }
        }

        if (order.Count != manifests.Count)
        {
            var cycle = FindCycle(manifests, remaining);
            throw new PlanException($"Dependency cycle detected: {string.Join(" → ", cycle)}.");
        }

        return order;
    }

    private static List<string> FindCycle(
        IReadOnlyDictionary<string, Manifest> manifests,
        IReadOnlyDictionary<string, int> remaining)
    {
        // Any node with residual remaining-count is part of a cycle. Walk its
        // dependencies (that also have residual count) to report the cycle.
        var start = remaining.First(kv => kv.Value > 0).Key;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string> { start };
        var current = start;
        while (seen.Add(current))
        {
            var next = manifests[current].Depends.FirstOrDefault(d => remaining[d] > 0);
            if (next is null || next == current)
                break;
            path.Add(next);
            current = next;
        }
        var idx = path.IndexOf(current);
        return idx > 0 ? path.Skip(idx).ToList() : path;
    }
}
