using System.Threading.Channels;
using SetupTool.Core.Execution;

namespace SetupTool.Tests;

public class InteractiveWatchdogTests
{
    /// <summary>
    /// A TextReader backed by a Channel. <see cref="Append(string)"/> queues
    /// output (simulating a child writing over time); <see cref="Close()"/>
    /// completes the channel (EOF). The watchdog consumes it like any stream.
    /// </summary>
    private sealed class ChannelTextReader : TextReader
    {
        private readonly Channel<string> _ch = Channel.CreateUnbounded<string>();

        public void Append(string s) => _ch.Writer.TryWrite(s);
        public void EndOfStream() => _ch.Writer.TryComplete();

        public override async Task<int> ReadAsync(char[] buffer, int index, int count)
        {
            if (!await _ch.Reader.WaitToReadAsync().ConfigureAwait(false))
                return 0; // EOF
            var chunk = await _ch.Reader.ReadAsync().ConfigureAwait(false);
            int n = Math.Min(chunk.Length, count);
            chunk.CopyTo(0, buffer, index, n);
            return n;
        }
    }

    [Fact]
    public async Task Relays_Output_And_Returns_OnEOF()
    {
        var reader = new ChannelTextReader();
        reader.Append("Hello from the child\n");

        var watchdog = new InteractiveWatchdog(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100));
        var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var watch = watchdog.RunAndWatchAsync(reader, default);
            reader.EndOfStream(); // EOF -> watch should return
            await watch;
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("Hello from the child", sw.ToString());
    }

    [Fact]
    public async Task Offers_Hint_WhenStalled_ThenRecoversOnOutput()
    {
        var reader = new ChannelTextReader();
        reader.Append("prompt: ");

        var watchdog = new InteractiveWatchdog(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100));
        var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var watch = watchdog.RunAndWatchAsync(reader, default);
            // Wait past the stall threshold so the hint fires.
            await Task.Delay(500);
            reader.Append("late output\n");
            await Task.Delay(200);
            reader.EndOfStream();
            await watch;
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("waiting for input", sw.ToString());
        Assert.Contains("late output", sw.ToString());
        Assert.Contains("prompt: ", sw.ToString());
    }

    [Fact]
    public async Task DoesNotOfferHint_WhenOutputKeepsFlowing()
    {
        var reader = new ChannelTextReader();
        var watchdog = new InteractiveWatchdog(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(100));
        var sw = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var watch = watchdog.RunAndWatchAsync(reader, default);
            // Keep writing every 40ms — well under the 150ms threshold.
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(40);
                reader.Append($"tick{i} ");
            }
            reader.EndOfStream();
            await watch;
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain("waiting for input", sw.ToString());
        Assert.Contains("tick0", sw.ToString());
        Assert.Contains("tick7", sw.ToString());
    }
}
