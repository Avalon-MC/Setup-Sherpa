using SetupSherpa.Core.Execution;
using SetupSherpa.Core.Manifest;

namespace SetupSherpa.Tests;

public class DotEnvFileTests
{
    [Fact]
    public void Parses_KeyValue()
    {
        var map = DotEnvFile.Parse("A=1\nB=two\n");
        Assert.Equal("1", map["A"]);
        Assert.Equal("two", map["B"]);
    }

    [Fact]
    public void SplitsOn_FirstEquals()
    {
        // A value containing '=' must keep everything after the first '='.
        var map = DotEnvFile.Parse("PW=abc=def=ghi\n");
        Assert.Equal("abc=def=ghi", map["PW"]);
    }

    [Fact]
    public void Trims_Whitespace_AroundEquals()
    {
        var map = DotEnvFile.Parse("A = 1\nB=2\n");
        Assert.Equal("1", map["A"]);
        Assert.Equal("2", map["B"]);
    }

    [Fact]
    public void Strips_SurroundingQuotes()
    {
        var map = DotEnvFile.Parse("A=\"double\"\nB='single'\n");
        Assert.Equal("double", map["A"]);
        Assert.Equal("single", map["B"]);
    }

    [Fact]
    public void Ignores_Comments_Blank_AndNoEquals_Lines()
    {
        var map = DotEnvFile.Parse("""
            # a comment
            A=1

            not-an-assignment
            B=2
            """);
        Assert.Equal(2, map.Count);
        Assert.Equal("1", map["A"]);
        Assert.Equal("2", map["B"]);
    }
}

public class EnvSubstitutionTests
{
    private static readonly IReadOnlyDictionary<string, string> Env =
        new Dictionary<string, string> { ["MSSQL_SA_PASSWORD"] = "S3cret!" };

    [Fact]
    public void Replaces_ListedToken_BareForm()
    {
        var result = EnvSubstitution.Expand("-e MSSQL_SA_PASSWORD=$MSSQL_SA_PASSWORD", ["MSSQL_SA_PASSWORD"], Env);
        Assert.Equal("-e MSSQL_SA_PASSWORD=S3cret!", result);
    }

    [Fact]
    public void Replaces_ListedToken_BracedForm()
    {
        var result = EnvSubstitution.Expand("${MSSQL_SA_PASSWORD}", ["MSSQL_SA_PASSWORD"], Env);
        Assert.Equal("S3cret!", result);
    }

    [Fact]
    public void Leaves_UnlistedToken_Literal()
    {
        var result = EnvSubstitution.Expand("-e OTHER=$OTHER -e PW=$MSSQL_SA_PASSWORD", ["MSSQL_SA_PASSWORD"], Env);
        // $OTHER is not listed -> stays literal; $MSSQL_SA_PASSWORD is substituted.
        Assert.Equal("-e OTHER=$OTHER -e PW=S3cret!", result);
    }

    [Fact]
    public void Leaves_Literal_WhenNoTokensListed()
    {
        var result = EnvSubstitution.Expand("$HOME /path", [], Env);
        Assert.Equal("$HOME /path", result);
    }

    [Fact]
    public void Throws_OnListedButMissingToken()
    {
        var ex = Assert.Throws<EnvSubstitutionException>(() =>
            EnvSubstitution.Expand("$NOPE", ["NOPE"], Env));
        Assert.Contains("NOPE", ex.Message);
        Assert.Contains(".env", ex.Message);
    }

    [Fact]
    public void DoesNotReplace_PartialIdentifier()
    {
        // $MSSQL_SA_PASSWORDX is a different identifier; must not be substituted.
        var result = EnvSubstitution.Expand("$MSSQL_SA_PASSWORDX", ["MSSQL_SA_PASSWORD"], Env);
        Assert.Equal("$MSSQL_SA_PASSWORDX", result);
    }
}

public class EnvSubstitutionExecutorTests
{
    [Fact]
    public async Task DockerRun_SubstitutesSecret_BeforeTokenizer()
    {
        var runner = new FakeRunner();
        var env = new Dictionary<string, string> { ["MSSQL_SA_PASSWORD"] = "RealPass123" };
        var step = new Step
        {
            Type = StepType.DockerRun,
            ExpansionTokens = ["MSSQL_SA_PASSWORD"],
            Command = "-d -e MSSQL_SA_PASSWORD=$MSSQL_SA_PASSWORD --name sql mcr/sql:latest",
        };
        var ctx = TestContext.Make(step, runner, env: env);
        runner.Responses.Enqueue((1, "")); // inspect -> absent

        await new DockerRunExecutor().ExecuteAsync(ctx, default);

        // The docker run call must carry the substituted secret, not the literal token.
        var runCall = runner.Calls[1];
        Assert.Contains("MSSQL_SA_PASSWORD=RealPass123", runCall.Arguments);
        Assert.DoesNotContain(runCall.Arguments, a => a.Contains("$MSSQL_SA_PASSWORD"));
    }

    [Fact]
    public async Task DockerVolume_Substitutes_Too()
    {
        var runner = new FakeRunner();
        var env = new Dictionary<string, string> { ["VOL_NAME"] = "myvol" };
        var step = new Step
        {
            Type = StepType.DockerVolume,
            ExpansionTokens = ["VOL_NAME"],
            Command = "create ${VOL_NAME}",
        };
        var ctx = TestContext.Make(step, runner, env: env);
        runner.Responses.Enqueue((1, "")); // volume inspect -> absent

        await new DockerVolumeExecutor().ExecuteAsync(ctx, default);

        Assert.Contains("myvol", runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task DockerRun_Throws_WhenListedTokenMissing()
    {
        var runner = new FakeRunner();
        var env = new Dictionary<string, string> { }; // MISSING token
        var step = new Step
        {
            Type = StepType.DockerRun,
            ExpansionTokens = ["MISSING"],
            Command = "-e PW=$MISSING --name sql mcr/sql:latest",
        };
        var ctx = TestContext.Make(step, runner, env: env);

        await Assert.ThrowsAsync<EnvSubstitutionException>(() =>
            new DockerRunExecutor().ExecuteAsync(ctx, default));
    }
}

public class ComposeEnvFileTests
{
    [Fact]
    public async Task Compose_PassesEnvFile_WhenFileExists()
    {
        var runner = new FakeRunner();
        var envPath = Path.Combine(Path.GetTempPath(), $"sherpa-test-{Guid.NewGuid():N}", ".env");
        Directory.CreateDirectory(Path.GetDirectoryName(envPath)!);
        File.WriteAllText(envPath, "A=1\n");
        try
        {
            var step = new Step { Type = StepType.Compose, Project = "web", File = "./compose.yaml" };
            var ctx = TestContext.Make(step, runner, env: new Dictionary<string, string>(), envPath: envPath);
            runner.Responses.Enqueue((1, "")); // compose ls -> not running

            await new ComposeExecutor(new LocalComposeDeployer()).ExecuteAsync(ctx, default);

            // compose -p web -f file --env-file <path> up -d
            var runCall = runner.Calls[1];
            Assert.Contains("--env-file", runCall.Arguments);
            Assert.Contains(envPath, runCall.Arguments);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(envPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task Compose_OmitsEnvFile_WhenFileMissing()
    {
        var runner = new FakeRunner();
        var envPath = "/does/not/exist/.env"; // no .env present
        var step = new Step { Type = StepType.Compose, Project = "web", File = "./compose.yaml" };
        var ctx = TestContext.Make(step, runner, env: new Dictionary<string, string>(), envPath: envPath);
        runner.Responses.Enqueue((1, "")); // compose ls -> not running

        await new ComposeExecutor(new LocalComposeDeployer()).ExecuteAsync(ctx, default);

        // No .env file -> no --env-file; compose uses its own defaults.
        var runCall = runner.Calls[1];
        Assert.DoesNotContain("--env-file", runCall.Arguments);
    }
}
