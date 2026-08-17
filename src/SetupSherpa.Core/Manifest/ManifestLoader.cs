using Tomlyn;
using Tomlyn.Model;

namespace SetupSherpa.Core.Manifest;

/// <summary>
/// Raised when a manifest cannot be loaded or fails schema validation.
/// Carries a human-readable message with line/column context where available.
/// </summary>
public sealed class ManifestException : Exception
{
    public ManifestException(string message) : base(message) { }
    public ManifestException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Loads and validates a single <see cref="Manifest"/> from a TOML file.
/// Uses Tomlyn 2.10.1 (TomlSerializer into the raw model, which throws
/// TomlException with line/column on parse errors) and manual schema
/// validation so error messages point at the actual problem.
/// </summary>
public static class ManifestLoader
{
    public static Manifest Load(string path)
    {
        string full = Path.GetFullPath(path);
        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ManifestException($"Cannot read manifest '{full}': {ex.Message}", ex);
        }

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text)
                ?? throw new TomlException("null document root");
        }
        catch (TomlException ex)
        {
            throw new ManifestException($"Invalid TOML in '{full}': {ex.Message}", ex);
        }

        return Build(root, full);
    }

    private static Manifest Build(TomlTable table, string full)
    {
        var manifest = new Manifest { SourcePath = full };

        if (!TryString(table, "name", out string? name) || string.IsNullOrWhiteSpace(name))
        {
            throw new ManifestException($"Manifest '{full}' is missing required field 'name' (a string).");
        }
        manifest.Name = name!;

        if (TryString(table, "workdir", out string? workdir))
        {
            manifest.WorkDir = workdir;
        }

        if (table.TryGetValue("installOrder", out var ioValue))
        {
            if (ioValue is long l)
            {
                if (l is < -100 or > 100)
                    throw new ManifestException($"Manifest '{full}' 'installOrder' must be within -100..+100 (got {l}).");
                manifest.InstallOrder = (int)l;
            }
            else
            {
                throw new ManifestException($"Manifest '{full}' 'installOrder' must be an integer -100..+100.");
            }
        }

        if (TryStringArray(table, "depends", out var depends))
        {
            manifest.Depends = depends;
        }

        if (table.TryGetValue("step", out var stepValue) && stepValue is TomlTableArray stepsArr)
        {
            var steps = new List<Step>();
            foreach (var stepTable in stepsArr)
            {
                steps.Add(BuildStep(stepTable, full));
            }
            manifest.Steps = steps;
        }
        else
        {
            throw new ManifestException($"Manifest '{full}' has no '[[step]]' entries.");
        }

        return manifest;
    }

    private static Step BuildStep(TomlTable table, string full)
    {
        if (!TryString(table, "type", out string? typeRaw) || string.IsNullOrWhiteSpace(typeRaw))
        {
            throw new ManifestException($"Manifest '{full}' has a step without a required 'type' (string).");
        }

        if (!StepTypes.TryParse(typeRaw, out var type))
        {
            throw new ManifestException(
                $"Manifest '{full}' step has unknown 'type' = '{typeRaw}'. Valid: " +
                "apt, repo, docker-run, docker-volume, compose, bash.");
        }

        var step = new Step { Type = type };

        if (TryString(table, "privilege", out string? privRaw) &&
            !string.IsNullOrWhiteSpace(privRaw))
        {
            if (!Enum.TryParse<Privilege>(privRaw, true, out var priv))
            {
                throw new ManifestException(
                    $"Manifest '{full}' step '{typeRaw}' has invalid 'privilege' = '{privRaw}'. Valid: root, user.");
            }
            step.PrivilegeOverride = priv;
        }

        if (TryString(table, "workdir", out string? workdir))
        {
            step.WorkDir = workdir;
        }

        if (table.TryGetValue("interactive", out var inter) && inter is bool ib)
        {
            step.Interactive = ib;
        }

        switch (type)
        {
            case StepType.Apt:
                if (table.TryGetValue("update", out var upd) && upd is bool ub)
                    step.Update = ub;
                if (TryStringArray(table, "packages", out var pkgs))
                    step.Packages = pkgs;
                if (step.Packages.Count == 0)
                    throw new ManifestException($"Manifest '{full}' apt step needs at least one 'packages' entry.");
                break;

            case StepType.Repo:
                step.Source = Req(table, full, "source");
                if (TryString(table, "keyring", out string? key)) step.Keyring = key;
                if (TryString(table, "suite", out string? suite)) step.Suite = suite;
                if (TryString(table, "architectures", out string? arch)) step.Architectures = arch;
                if (TryStringArray(table, "components", out var comps)) step.Components = comps;
                if (TryString(table, "repo_name", out string? rn)) step.RepoName = rn;
                break;

            case StepType.DockerRun:
            case StepType.DockerVolume:
                step.Command = Req(table, full, "command");
                if (TryStringArray(table, "expansionTokens", out var expToks))
                    step.ExpansionTokens = expToks;
                break;

            case StepType.Compose:
                if (TryString(table, "project", out string? proj)) step.Project = proj;
                if (TryString(table, "file", out string? file)) step.File = file;
                if (string.IsNullOrWhiteSpace(step.Project) || string.IsNullOrWhiteSpace(step.File))
                    throw new ManifestException($"Manifest '{full}' compose step needs both 'project' and 'file'.");
                break;

            case StepType.Bash:
                step.Script = Req(table, full, "script");
                break;

            case StepType.Wait:
                step.Message = Req(table, full, "message");
                break;

            case StepType.EnvInput:
                step.Variable = Req(table, full, "variable");
                if (table.TryGetValue("secret", out var sec) && sec is bool sb)
                    step.Secret = sb;
                break;

            case StepType.Copy:
                step.Src = Req(table, full, "src");
                step.Dest = Req(table, full, "dest");
                break;

            case StepType.Extract:
                step.Archive = Req(table, full, "archive");
                step.Dest = Req(table, full, "dest");
                break;

            case StepType.Systemd:
                step.Unit = Req(table, full, "unit");
                if (TryString(table, "name", out string? svc)) step.ServiceName = svc;
                if (table.TryGetValue("enable", out var en) && en is bool enb) step.Enable = enb;
                if (table.TryGetValue("start", out var st) && st is bool stb) step.Start = stb;
                break;
        }

        return step;
    }

    private static string Req(TomlTable table, string full, string key)
    {
        if (!TryString(table, key, out string? val) || string.IsNullOrWhiteSpace(val))
            throw new ManifestException($"Manifest '{full}' step of type needs required field '{key}'.");
        return val!;
    }

    private static bool TryString(TomlTable table, string key, out string? value)
    {
        if (table.TryGetValue(key, out var v) && v is string s)
        {
            value = s;
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryStringArray(TomlTable table, string key, out IReadOnlyList<string> value)
    {
        if (table.TryGetValue(key, out var v) && v is TomlArray arr)
        {
            var list = new List<string>();
            foreach (var item in arr)
            {
                if (item is string s)
                    list.Add(s);
                else
                    throw new ManifestException($"Field '{key}' must be an array of strings.");
            }
            value = list;
            return true;
        }
        value = [];
        return false;
    }
}
