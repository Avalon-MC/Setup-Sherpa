namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// Prompts the user for a value and stores it in the single `.env` file (and
/// the in-memory env map) so later steps in the same run can use it via
/// <c>expansionTokens</c>. Generic: any variable, not just a specific secret.
/// Echoes the input by default so the user can validate what they typed
/// (<c>secret = false</c>); set <c>secret = true</c> to suppress echo.
/// </summary>
public sealed class EnvInputExecutor : IStepExecutor
{
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public EnvInputExecutor(TextReader? input = null, TextWriter? output = null)
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    public StepType Type => StepType.EnvInput;

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        string variable = ctx.Step.Variable!;
        string prompt = $"{variable}: ";

        _output.Write(prompt);
        _output.Flush();
        string? value = _input.ReadLine();
        if (string.IsNullOrEmpty(value))
            throw new StepFailedException($"env-input for '{variable}' received no value.");

        // Persist to .env (create if missing) and update the in-memory map so
        // later steps in this run can use the value immediately.
        await PersistAsync(ctx, variable, value, ct).ConfigureAwait(false);
        ctx.Env![variable] = value;

        return StepResult.Completed($"set {variable} in .env");
    }

    private static async Task PersistAsync(StepContext ctx, string variable, string value, CancellationToken ct)
    {
        string path = ctx.EnvPath!;
        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false)).ToList()
            : new List<string>();

        // Replace an existing KEY= line, else append. Preserve comments/blanks.
        int idx = lines.FindIndex(l => l.TrimStart().StartsWith(variable + "=", StringComparison.Ordinal));
        string newLine = $"{variable}={value}";
        if (idx >= 0)
            lines[idx] = newLine;
        else
            lines.Add(newLine);

        await File.WriteAllLinesAsync(path, lines, ct).ConfigureAwait(false);
    }
}
