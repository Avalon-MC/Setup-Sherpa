using SetupSherpa.Core.Execution;

namespace SetupSherpa.Tests;

public class CommandTokenizerTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("docker run -d", 3)]
    [InlineData("   docker   run   -d   ", 3)]
    [InlineData("-d -p 8000:8000 --name portainer portainer/portainer-ce:lts", 6)]
    public void Splits_BasicCommands(string cmd, int expected)
    {
        Assert.Equal(expected, CommandTokenizer.Tokenize(cmd).Count);
    }

    [Fact]
    public void Keeps_QuotedValueWithSpace_AsSingleToken()
    {
        // A bind mount whose host path contains a space must stay one token.
        var tokens = CommandTokenizer.Tokenize("-v '/path/with space:/dest'");
        Assert.Equal(["-v", "/path/with space:/dest"], tokens);
    }

    [Fact]
    public void Keeps_DollarSign_Literal()
    {
        // No $HOME expansion (D5): $HOME must come through literally.
        var tokens = CommandTokenizer.Tokenize("-v $HOME:/data");
        Assert.Equal(["-v", "$HOME:/data"], tokens);
    }

    [Fact]
    public void Keeps_Glob_Literal()
    {
        // No globbing (D5): a lone * must not expand.
        var tokens = CommandTokenizer.Tokenize("echo *");
        Assert.Equal(["echo", "*"], tokens);
    }

    [Fact]
    public void Keeps_ShellOperators_Literal()
    {
        // No ;/&&/| interpretation (D5).
        var tokens = CommandTokenizer.Tokenize("cmd; other && more | pipe");
        Assert.Equal(["cmd;", "other", "&&", "more", "|", "pipe"], tokens);
    }

    [Fact]
    public void DoubleQuotes_AllowEscapedQuote()
    {
        var tokens = CommandTokenizer.Tokenize("-e \"a\\\"b\"");
        Assert.Equal(["-e", "a\"b"], tokens);
    }

    [Fact]
    public void SingleQuotes_KeepBackslash_Literal()
    {
        var tokens = CommandTokenizer.Tokenize("'a\\b'");
        Assert.Equal(["a\\b"], tokens);
    }
}

public class WorkDirResolverTests
{
    private const string ManifestDir = "/mnt/manifests";
    private const string UserHome = "/home/peter";
    private const string DefaultDir = "/cwd";

    [Fact]
    public void Unset_UsesDefault()
        => Assert.Equal(DefaultDir, WorkDirResolver.Resolve(null, ManifestDir, UserHome, DefaultDir));

    [Fact]
    public void Tilde_ResolvesToHome()
        => Assert.Equal(UserHome, WorkDirResolver.Resolve("~", ManifestDir, UserHome, DefaultDir));

    [Fact]
    public void TildeSlash_ResolvesIntoHome()
        => Assert.Equal("/home/peter/apps/portainer", WorkDirResolver.Resolve("~/apps/portainer", ManifestDir, UserHome, DefaultDir));

    [Fact]
    public void Absolute_UsedAsIs()
        => Assert.Equal("/var/lib/x", WorkDirResolver.Resolve("/var/lib/x", ManifestDir, UserHome, DefaultDir));

    [Fact]
    public void Relative_ResolvesAgainstManifestDir()
        => Assert.Equal("/mnt/manifests/compose", WorkDirResolver.Resolve("compose", ManifestDir, UserHome, DefaultDir));

    [Fact]
    public void DotSlash_ResolvesAgainstManifestDir()
        => Assert.Equal("/mnt/manifests/sub", WorkDirResolver.Resolve("./sub", ManifestDir, UserHome, DefaultDir));
}
