namespace SetupSherpa.Core.Execution;

using SetupSherpa.Core.Manifest;

/// <summary>
/// A declared handover with nothing to run: prints a message and waits for the
/// user to press Enter. Used after an install that needs manual follow-up in a
/// web UI (e.g. Portainer's initial setup) before the run continues.
/// </summary>
public sealed class WaitExecutor : IStepExecutor
{
    private readonly TextReader _input;
    private readonly TextWriter _output;

    public WaitExecutor(TextReader? input = null, TextWriter? output = null)
    {
        _input = input ?? Console.In;
        _output = output ?? Console.Out;
    }

    public StepType Type => StepType.Wait;

    public Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine(ctx.Step.Message ?? "Press Enter to continue...");
        _output.WriteLine();
        _output.Write("Press Enter to continue... ");
        _output.Flush();
        _input.ReadLine();
        return Task.FromResult(StepResult.Completed());
    }
}
