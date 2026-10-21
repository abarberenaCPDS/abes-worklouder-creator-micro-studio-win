namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

/// A message longer than one 64-byte report goes out as several reports, and
/// two callers sending at once would interleave those chunks into two
/// unparseable frames. The key path makes that reachable: a key press is handled
/// on the device's read thread while the poll loop is painting.
public class RpcDeviceSendGateTests
{
    /// Flags if two sends are ever in flight at the same time.
    private sealed class OverlapDetectingTransport : IWlTransport
    {
        private int _inFlight;

        public bool Overlapped { get; private set; }
        public int Sends;

        public string? Product => "fake";
        public int? ProductId => 0;
        public string? Transport => "fake";
        public string? Serial => null;
        public bool IsConnected { get; private set; }

        public event EventHandler<byte[]>? OnReportReceived { add { } remove { } }
        public event EventHandler<string>? OnDeviceLog { add { } remove { } }
        public event EventHandler<string>? OnDisconnect { add { } remove { } }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public async Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Sends);
            if (Interlocked.Increment(ref _inFlight) > 1) Overlapped = true;
            try
            {
                // Long enough that unserialised sends would certainly overlap.
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    /// A payload that spills over several 64-byte reports.
    private static Dictionary<string, object?> Payload(int length) =>
        new() { ["pad"] = new string('x', length) };

    [Fact]
    public async Task ConcurrentCallsNeverSendAtTheSameTime()
    {
        var transport = new OverlapDetectingTransport();
        await using var device = new RpcDevice(transport);
        await device.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        // Nothing ever answers, so every call ends in cancellation. That is
        // fine — this test is only about what reaches the wire.
        var calls = Enumerable.Range(0, 6)
            .Select(_ => device.RequestAsync("v.oai.thstatus", Payload(2000), cts.Token))
            .ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(calls));

        Assert.True(transport.Sends > 1, "the test did not exercise concurrent sends at all");
        Assert.False(transport.Overlapped);
    }

    /// The gate must serialise, not swallow: every call still goes out.
    [Fact]
    public async Task EveryCallStillReachesTheWire()
    {
        var transport = new OverlapDetectingTransport();
        await using var device = new RpcDevice(transport);
        await device.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var calls = new[]
        {
            device.RequestAsync("v.oai.thstatus", null, cts.Token),
            device.RequestAsync("v.oai.rgbcfg", null, cts.Token),
            device.RequestAsync("device.status", null, cts.Token),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(calls));

        Assert.True(transport.Sends >= calls.Length);
    }

    /// A notification returns as soon as it is sent, so this one needs no
    /// cancellation to finish.
    [Fact]
    public async Task NotificationsAreSerialisedToo()
    {
        var transport = new OverlapDetectingTransport();
        await using var device = new RpcDevice(transport);
        await device.ConnectAsync();

        var sends = Enumerable.Range(0, 4)
            .Select(_ => device.NotifyAsync("v.oai.thstatus", Payload(300)))
            .ToArray();

        await Task.WhenAll(sends);

        Assert.True(transport.Sends > 4, "the payload did not span several reports");
        Assert.False(transport.Overlapped);
    }
}
