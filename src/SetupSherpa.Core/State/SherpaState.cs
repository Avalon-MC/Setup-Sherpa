using Tomlyn;

namespace SetupSherpa.Core.State;

/// <summary>
/// The on-disk state of which manifests have been installed for a config
/// directory. Lives at <c>.sherpa</c> inside the directory Sherpa is pointed at.
/// Always-skip-if-marked: a manifest listed here is treated as already
/// installed and skipped on re-runs, regardless of edits (per the user's
/// choice). No hashes or timestamps — just the names.
/// </summary>
public sealed class SherpaState
{
    public int Version { get; set; } = 1;

    /// <summary>Names of manifests that have been fully installed.</summary>
    public List<string> Installed { get; set; } = [];

    public static SherpaState Load(string path)
    {
        if (!File.Exists(path))
            return new SherpaState();
        try
        {
            return TomlSerializer.Deserialize<SherpaState>(File.ReadAllText(path)) ?? new SherpaState();
        }
        catch (TomlException)
        {
            // A corrupt state file shouldn't brick the run; treat as empty.
            return new SherpaState();
        }
    }

    public void Save(string path)
    {
        var text = TomlSerializer.Serialize(this);
        File.WriteAllText(path, text);
    }

    public bool IsInstalled(string name) => Installed.Contains(name);

    public void MarkInstalled(string name)
    {
        if (!Installed.Contains(name))
            Installed.Add(name);
    }
}
