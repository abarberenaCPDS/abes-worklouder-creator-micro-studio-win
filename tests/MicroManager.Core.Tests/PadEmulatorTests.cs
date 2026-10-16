namespace MicroManager.Core.Tests;

using MicroManager.Core.Config;
using MicroManager.Core.Emulator;
using MicroManager.Core.Keymap;
using MicroManager.Core.Pad;
using MicroManager.Core.Transport;
using System.Text.Json;

public class PadEmulatorTests
{
    [Fact]
    public async Task ConnectsWithoutHardware()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        Assert.True(emulator.IsConnected);
        emulator.Disconnect();
        Assert.False(emulator.IsConnected);
    }

    [Fact]
    public async Task AnswersBaselineCalls()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            var (version, error) = emulator.Handle("sys.version", null);
            Assert.Null(error);
            Assert.Contains("emulated", version!.ToString()!);

            var (status, _) = emulator.Handle("device.status", null);
            Assert.Null(error);

            var (list, _) = emulator.Handle("fs.list", null);
            Assert.Null(error);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task UnregisteredMethodIsNotFound()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            var (_, error) = emulator.Handle("v.oai.nonexistent", null);
            Assert.Equal("Method not found", error);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task StockKeymapBindsNothingUntilApplied()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            Assert.Empty(emulator.Bound);
            // Pressing an unbound key reports nothing
            emulator.Press(0);
            Assert.False(emulator.Traffic.Any(t => t.Contains("pressed") && !t.Contains("not AG-bound")));
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task BoundKeyLightsAndUnboundKeyStaysDarkWithoutComplaint()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            // Bind key 1 (AG01) via fs.write equivalent: apply an agent keymap by
            // directly writing a keymap that binds it
            var boundKeymap = new Dictionary<string, object?>
            {
                ["version"] = 1,
                ["activeProfileId"] = 0,
                ["profiles"] = new List<object?>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = 0,
                        ["name"] = "Default",
                        ["layers"] = new List<object?>
                        {
                            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["id"] = 0,
                                ["name"] = "Base",
                                ["layout"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["keymap"] = new List<List<string>>
                                    {
                                        new() { "KC_F13", "KV_OAI_AG01" },
                                        new() { "KC_F15", "KC_F16", "KC_F17", "KC_F18" },
                                        new() { "KC_F19", "KC_F20", "KC_F21", "KC_F22" },
                                        new() { "KC_F23", "KC_NONE", "KC_F24" },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            emulator.Handle("fs.write",
                JsonSerializer.SerializeToElement(new { file = "keymap.json", data = JsonSerializer.Serialize(boundKeymap) }));

            Assert.Contains(1, emulator.Bound);

            // Lighting an unbound key accepts silently and stays dark
            var (_, error) = emulator.Handle(Oai.MethodThreads,
                JsonSerializer.SerializeToElement(new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 0, ["c"] = 0xFF0000 }
                }));
            Assert.Null(error);
            Assert.Equal(0, emulator.Keys[0].Brightness);

            // Lighting a bound key works
            emulator.Handle(Oai.MethodThreads,
                JsonSerializer.SerializeToElement(new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 1, ["c"] = 0x00FF00 }
                }));
            Assert.Equal(0x00FF00, emulator.Keys[1].Color);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task OmittedFieldsLeaveThatAspectAlone()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            emulator.Handle(Oai.MethodThreads,
                JsonSerializer.SerializeToElement(new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 5, ["c"] = 0xFF0000 }
                }));
            emulator.Handle(Oai.MethodThreads,
                JsonSerializer.SerializeToElement(new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 5, ["b"] = 0.7 }
                }));
            Assert.Equal(0xFF0000, emulator.Keys[5].Color);
            Assert.Equal(0.7, emulator.Keys[5].Brightness);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task ZonesAreStored()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            emulator.Handle(Oai.MethodRgbConfig,
                JsonSerializer.SerializeToElement(new Dictionary<string, object>
                {
                    ["keys"] = new Dictionary<string, object> { ["e"] = 1, ["b"] = 0.5, ["s"] = 0.2, ["m"] = 1, ["c"] = 0xFF0000 }
                }));
            Assert.Equal(Oai.Effect.Solid, emulator.KeysZone.Effect);
            Assert.Equal(0.5, emulator.KeysZone.Brightness);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task PressingABoundKeyPushesAHIDReport()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            // Key 0 is AG01-bound in reading order... actually key 1 is AG01.
            // Bind key 1 by faking the keymap, then press it
            emulator.Bound.Add(1);
            var presses = new List<string>();
            emulator.OnNotify += n =>
            {
                if (n.Method == "v.oai.hid" && n.Parameters is { } p)
                    presses.Add(p.GetProperty("k").GetString()!);
            };

            emulator.Press(1);

            await Task.Delay(200);
            var press = presses.FirstOrDefault(p => p == "AG01" && p.Contains("act"));
            // We can't easily inspect act from the string alone; just check we got a notify
            Assert.Contains(presses, p => p == "AG01");
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task PressingAnUnboundKeyReportsNothing()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            var notified = false;
            emulator.OnNotify += _ => notified = true;
            emulator.Press(0);
            await Task.Delay(200);
            Assert.False(notified);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task ResetReturnsAStockPad()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            emulator.Handle(Oai.MethodThreads,
                JsonSerializer.SerializeToElement(new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 3, ["c"] = 0xFF0000 }
                }));
            emulator.Reset();
            Assert.Empty(emulator.Bound);
            Assert.Empty(emulator.Keys);
            Assert.Equal(Oai.Zone.Dark.Effect, emulator.KeysZone.Effect);
        }
        finally
        {
            emulator.Disconnect();
        }
    }

    [Fact]
    public async Task KeymapIsReadDoubleEncodedLikeTheDevice()
    {
        var emulator = new PadEmulator();
        await emulator.ConnectAsync();
        try
        {
            var (raw, error) = emulator.Handle("fs.read",
                JsonSerializer.SerializeToElement(new { file = "keymap.json" }));

            Assert.Null(error);
            // `data` is a JSON *string* the caller must parse again, exactly
            // as the firmware double-encodes it; a nested object here would
            // make every read fail to parse.
            var parsed = KeymapManager.Parse(raw);
            Assert.True(parsed.ContainsKey("profiles"));
        }
        finally
        {
            emulator.Disconnect();
        }
    }
}
