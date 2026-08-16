using SetupTool.Core.Manifest;
using SetupTool.Core.Planning;

namespace SetupTool.Tests;

public class ManifestLoaderTests
{
    private readonly string _dir;

    public ManifestLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "setuptool-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Loads_FullManifest_WithAllStepTypes()
    {
        var p = Write("m.toml", """
            name = "web"
            depends = ["docker"]
            workdir = "~/apps/web"

            [[step]]
            type = "repo"
            source = "https://example.com/repo"
            keyring = "https://example.com/key.gpg"
            components = ["main"]

            [[step]]
            type = "apt"
            update = true
            packages = ["nginx"]

            [[step]]
            type = "docker-run"
            command = "-d --name web -p 8080:80 nginx"

            [[step]]
            type = "compose"
            project = "web"
            file = "@url:https://example.com/web.yaml"
            """);

        var m = ManifestLoader.Load(p);
        Assert.Equal("web", m.Name);
        Assert.Equal(["docker"], m.Depends);
        Assert.Equal("~/apps/web", m.WorkDir);
        Assert.Equal(4, m.Steps.Count);
        Assert.Equal(StepType.Repo, m.Steps[0].Type);
        Assert.Equal(StepType.Apt, m.Steps[1].Type);
        Assert.Equal(StepType.DockerRun, m.Steps[2].Type);
        Assert.Equal(StepType.Compose, m.Steps[3].Type);
        Assert.True(m.Steps[1].Update);
        Assert.Equal(["nginx"], m.Steps[1].Packages);
        // Step 2 has no explicit workdir; the manifest default is resolved at execution time.
        Assert.Null(m.Steps[2].WorkDir);
    }

    [Fact]
    public void Rejects_UnknownStepType()
    {
        var p = Write("bad.toml", """
            name = "x"
            [[step]]
            type = "aptt"
            packages = ["foo"]
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("aptt", ex.Message);
    }

    [Fact]
    public void Rejects_MissingName()
    {
        var p = Write("bad.toml", """
            [[step]]
            type = "bash"
            script = "echo hi"
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Rejects_InvalidPrivilege()
    {
        var p = Write("bad.toml", """
            name = "x"
            [[step]]
            type = "bash"
            privilege = "rootx"
            script = "echo hi"
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("privilege", ex.Message);
    }

    [Fact]
    public void Reads_InteractiveAndPrivilege()
    {
        var p = Write("m.toml", """
            name = "wiz"
            [[step]]
            type = "bash"
            privilege = "user"
            interactive = true
            script = "./installer.run"
            """);
        var m = ManifestLoader.Load(p);
        Assert.True(m.Steps[0].Interactive);
        Assert.Equal(Privilege.User, m.Steps[0].PrivilegeOverride);
    }

    [Fact]
    public void Rejects_Compose_WithoutProjectOrFile()
    {
        var p = Write("bad.toml", """
            name = "x"
            [[step]]
            type = "compose"
            project = "only-project"
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("file", ex.Message);
    }

    [Fact]
    public void Rejects_Apt_WithoutPackages()
    {
        var p = Write("bad.toml", """
            name = "x"
            [[step]]
            type = "apt"
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("packages", ex.Message);
    }

    [Fact]
    public void Reads_InstallOrder()
    {
        var p = Write("m.toml", """
            name = "x"
            installOrder = 42
            [[step]]
            type = "bash"
            script = "echo hi"
            """);
        var m = ManifestLoader.Load(p);
        Assert.Equal(42, m.InstallOrder);
    }

    [Fact]
    public void InstallOrder_DefaultsToZero()
    {
        var p = Write("m.toml", """
            name = "x"
            [[step]]
            type = "bash"
            script = "echo hi"
            """);
        var m = ManifestLoader.Load(p);
        Assert.Equal(0, m.InstallOrder);
    }

    [Theory]
    [InlineData("-101")]
    [InlineData("101")]
    [InlineData("999")]
    public void Rejects_InstallOrder_OutOfRange(string value)
    {
        var p = Write("bad.toml", $"""
            name = "x"
            installOrder = {value}
            [[step]]
            type = "bash"
            script = "echo hi"
            """);
        var ex = Assert.Throws<ManifestException>(() => ManifestLoader.Load(p));
        Assert.Contains("installOrder", ex.Message);
    }
}

public class DependencyPlannerTests
{
    private static Manifest Make(string name, params string[] deps) => new()
    {
        Name = name,
        Depends = deps,
        Steps = [],
    };

    private static Manifest MakeOrdered(string name, int installOrder, params string[] deps) => new()
    {
        Name = name,
        Depends = deps,
        Steps = [],
        InstallOrder = installOrder,
    };

    [Fact]
    public void Returns_Order_WithDependenciesFirst()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["web"] = Make("web", "portainer"),
            ["portainer"] = Make("portainer", "docker"),
            ["docker"] = Make("docker"),
        };
        var order = new DependencyPlanner().Plan(manifests);
        Assert.Equal(["docker", "portainer", "web"], order.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Returns_DeterministicOrder_WhenIndependent()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["b"] = Make("b"),
            ["a"] = Make("a"),
            ["c"] = Make("c"),
        };
        var order = new DependencyPlanner().Plan(manifests);
        Assert.Equal(["a", "b", "c"], order.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Throws_OnUnknownDependency()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["web"] = Make("web", "nope"),
        };
        var ex = Assert.Throws<PlanException>(() => new DependencyPlanner().Plan(manifests));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Throws_OnCycle_WithReadableMessage()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["a"] = Make("a", "b"),
            ["b"] = Make("b", "c"),
            ["c"] = Make("c", "a"),
        };
        var ex = Assert.Throws<PlanException>(() => new DependencyPlanner().Plan(manifests));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HigherInstallOrder_InstallsFirst_AmongIndependent()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["low"] = MakeOrdered("low", -50),
            ["mid"] = MakeOrdered("mid", 0),
            ["high"] = MakeOrdered("high", 80),
        };
        var order = new DependencyPlanner().Plan(manifests);
        Assert.Equal(["high", "mid", "low"], order.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void InstallOrder_NeverOverridesDependencies()
    {
        // app has a HIGH installOrder, but depends on base with a LOW one.
        // base must still install first because of the dependency.
        var manifests = new Dictionary<string, Manifest>
        {
            ["base"] = MakeOrdered("base", -100),
            ["app"] = MakeOrdered("app", 100, "base"),
        };
        var order = new DependencyPlanner().Plan(manifests);
        Assert.Equal(["base", "app"], order.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void EqualInstallOrder_FallsBackToAlphabetical()
    {
        var manifests = new Dictionary<string, Manifest>
        {
            ["zeta"] = MakeOrdered("zeta", 5),
            ["alpha"] = MakeOrdered("alpha", 5),
            ["beta"] = MakeOrdered("beta", 5),
        };
        var order = new DependencyPlanner().Plan(manifests);
        Assert.Equal(["alpha", "beta", "zeta"], order.Select(m => m.Name).ToArray());
    }
}
