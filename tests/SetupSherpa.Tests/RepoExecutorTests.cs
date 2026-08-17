using SetupSherpa.Core.Execution;

namespace SetupSherpa.Tests;

public class RepoExecutorTests
{
    [Fact]
    public void Builds_ValidDeb822_ForDockerRepo()
    {
        // Docker's official repo: trixie suite, stable component, signed by keyring.
        var block = RepoExecutor.BuildDeb822Block(
            source: "https://download.docker.com/linux/debian",
            suite: "trixie",
            components: ["stable"],
            architectures: null,
            keyringPath: "/usr/share/keyrings/docker-archive-keyring.gpg");

        Assert.Equal("""
            Types: deb
            URIs: https://download.docker.com/linux/debian
            Suites: trixie
            Components: stable
            Signed-By: /usr/share/keyrings/docker-archive-keyring.gpg
            """ + "\n", block);
    }

    [Fact]
    public void Defaults_SuitesToVersionCodename_And_ComponentsToMain()
    {
        var block = RepoExecutor.BuildDeb822Block(
            source: "https://example.com/repo",
            suite: null,
            components: [],
            architectures: null,
            keyringPath: null);

        Assert.Contains("Suites: $VERSION_CODENAME", block);
        Assert.Contains("Components: main", block);
        Assert.DoesNotContain("Architectures:", block);
        Assert.DoesNotContain("Signed-By:", block);
    }

    [Fact]
    public void Includes_Architectures_WhenProvided()
    {
        var block = RepoExecutor.BuildDeb822Block(
            source: "https://example.com/repo",
            suite: "bookworm",
            components: ["main", "contrib"],
            architectures: "amd64 arm64",
            keyringPath: "/usr/share/keyrings/x.gpg");

        Assert.Contains("Suites: bookworm", block);
        Assert.Contains("Components: main contrib", block);
        Assert.Contains("Architectures: amd64 arm64", block);
        Assert.Contains("Signed-By: /usr/share/keyrings/x.gpg", block);
    }

    [Fact]
    public void VersionCodename_IsLiteral_NotShellExpanded()
    {
        // $VERSION_CODENAME must be written literally (apt expands it, not a shell).
        var block = RepoExecutor.BuildDeb822Block("https://example.com/r", null, [], null, null);
        Assert.Contains("$VERSION_CODENAME", block); // not "trixie"/"bookworm"
    }
}
