namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using System.Text;
using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Emulator;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

/// The bridge's key dispatch listens for `v.oai.hid` notifications and nothing
/// else. That path used to be unreachable in the emulator: it raised its own
/// event but never pushed the input report a real pad sends, so key handling
/// could only ever be tried against hardware.
public class PadKeyNotificationTests
{
    private static List<(string Key, int Act)> CollectPresses(RpcDevice device)
    {
        var presses = new List<(string Key, int Act)>();
        device.OnNotification += (_, notification) =>
        {
            if (notification.Method != Oai.NotifyHID) return;
            if (notification.Parameters is not JsonElement payload) return;
            presses.Add((payload.GetProperty("k").GetString()!, payload.GetProperty("act").GetInt32()));
        };
        return presses;
    }

    [Fact]
    public async Task ABoundKeyPressReachesTheRpcLayer()
    {
        var emulator = new PadEmulator();
        await using var device = new RpcDevice(emulator);
        await device.ConnectAsync();

        var presses = CollectPresses(device);
        emulator.Bound.Add(7);   // bound on the active layer, so it reports itself
        emulator.Press(7);

        Assert.Contains(("AG07", 1), presses);
        Assert.Equal(7, Oai.AgIndex("AG07"));
    }

    [Fact]
    public async Task AnUnboundKeyPressReportsNothing()
    {
        var emulator = new PadEmulator();
        await using var device = new RpcDevice(emulator);
        await device.ConnectAsync();

        var presses = CollectPresses(device);
        emulator.Press(7);   // the stock keymap binds nothing
        await Task.Delay(200);

        Assert.Empty(presses);
    }

    /// The envelope is the trap: it is `m`/`p`, so a handler that matches on
    /// `method` drops every press without ever raising an error.
    [Fact]
    public void TheNotificationUsesTheAbbreviatedEnvelope()
    {
        var emulator = new PadEmulator();
        var reports = new List<string>();
        emulator.OnReportReceived += (_, report) => reports.Add(Decode(report));

        emulator.Bound.Add(3);
        emulator.Press(3);

        var json = string.Concat(reports);
        Assert.StartsWith("{\"m\":\"v.oai.hid\",\"p\":", json);
        Assert.Contains("\"k\":\"AG03\"", json);
    }

    /// The other half of the trap: press and release have to be distinguishable,
    /// or every key would fire twice.
    [Fact]
    public async Task TheReleaseIsReportedSeparatelyFromThePress()
    {
        var emulator = new PadEmulator();
        await using var device = new RpcDevice(emulator);
        await device.ConnectAsync();

        var presses = CollectPresses(device);
        emulator.Bound.Add(0);
        emulator.Press(0);
        await Task.Delay(300);

        Assert.Equal(new[] { ("AG00", 1), ("AG00", 0) }, presses);
    }

    /// Same framing the transport uses: an optional report id, then the channel
    /// and length bytes, then the chunk.
    private static string Decode(byte[] report)
    {
        var start = report.Length > 2 && report[0] == WireProtocol.ReportId ? 1 : 0;
        var length = report[start + 1];
        return Encoding.UTF8.GetString(report, start + 2, length);
    }
}
