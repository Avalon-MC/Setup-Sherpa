using System.Threading.Channels;

namespace SetupSherpa.Core.Execution;

/// <summary>
/// Streams the output of an interactive step and offers an informational
/// takeover hint when the child appears stalled waiting for input (D3).
///
/// Because the user's keyboard is already attached to the child (stdin is
/// inherited through the pty), the hint is informational only — the user can
/// type at any time without the tool grabbing the keyboard. This means a
/// slow-but-healthy step can never be interrupted; the hint is purely a nudge.
/// </summary>
public sealed class InteractiveWatchdog
{
    private readonly TimeSpan _stallThreshold;
    private readonly TimeSpan _rearmDelay;
    private readonly TimeSpan _pollInterval;

    /// <param name="stallThreshold">No output for this long while the child is alive ⇒ offer takeover.</param>
    /// <param name="rearmDelay">Cooldown before offering again after a decline/no-op.</param>
    public InteractiveWatchdog(TimeSpan stallThreshold, TimeSpan rearmDelay)
        : this(stallThreshold, rearmDelay, TimeSpan.FromMilliseconds(150))
    {
    }

    internal InteractiveWatchdog(TimeSpan stallThreshold, TimeSpan rearmDelay, TimeSpan pollInterval)
    {
        _stallThreshold = stallThreshold;
        _rearmDelay = rearmDelay;
        _pollInterval = pollInterval;
    }

    /// <summary>
    /// Relays the child's stdout to the console and returns when the child's
    /// output is fully drained (EOF). Offers a takeover hint when no output
    /// arrives for the stall threshold. A single background reader feeds a
    /// channel (so there is never more than one pending read on the stream);
    /// the loop polls the channel and the stall timer on a fixed interval.
    /// </summary>
    public async Task RunAndWatchAsync(TextReader readOutput, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>();
        var lastActivity = DateTime.UtcNow;
        var lastOffer = DateTime.MinValue;

        var readerTask = Task.Run(async () =>
        {
            var buffer = new char[256];
            try
            {
                while (true)
                {
                    int n = await readOutput.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                    if (n == 0)
                        break; // EOF
                    await channel.Writer.WriteAsync(new string(buffer, 0, n), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        while (!channel.Reader.Completion.IsCompleted || channel.Reader.TryPeek(out _))
        {
            if (ct.IsCancellationRequested)
                return;

            bool any = false;
            while (channel.Reader.TryRead(out var chunk))
            {
                Console.Write(chunk);
                any = true;
            }
            if (any)
            {
                lastActivity = DateTime.UtcNow;
                continue;
            }

            // No output this tick — possibly a stall.
            if (DateTime.UtcNow - lastActivity >= _stallThreshold &&
                DateTime.UtcNow - lastOffer >= _rearmDelay)
            {
                Console.WriteLine(
                    "\n  ⚠ This step looks like it's waiting for input." +
                    "\n    Your keyboard is already connected — if it's prompting you, type the answer now." +
                    "\n    (Press Enter to dismiss this hint.)");
                lastOffer = DateTime.UtcNow;
            }

            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }

        await readerTask.ConfigureAwait(false); // ensure the reader finished cleanly
    }
}
