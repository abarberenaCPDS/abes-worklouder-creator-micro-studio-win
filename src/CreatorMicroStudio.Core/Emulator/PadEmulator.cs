namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Emulator;

using System.Text;
using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

/// A stand-in for the pad, for working without the hardware to hand.
///
/// It answers the same vendor RPC the firmware does and pushes the same
/// `v.oai.hid` reports back, so the bridge above it cannot tell the
/// difference. `RpcDevice` routes to one of these instead of HID when no
/// hardware is present.
///
/// It reproduces the firmware's awkward behaviours on purpose, because those
/// are the ones worth catching before you meet them on real hardware:
///
///   * it answers `{"ok":1}` to any lighting payload, right or wrong;
///   * a key only lights if it is bound to `KV_OAI_AG*` on the active layer —
///     an unbound key accepts its colour in silence and stays dark;
///   * an unbound key sends a keystroke rather than a `v.oai.hid` report, so
///     pressing it here does nothing, exactly as it would on the device.
///
/// It starts from the stock F-key keymap, so the app's binding step has real
/// work to do on first connect.
public sealed class PadEmulator : IWlTransport
{
    public const string Firmware = "v0.6.0-rc.10 (emulated)";

    public sealed class Key
    {
        public int Color { get; set; }
        public double Brightness { get; set; }
        public Oai.Effect Effect { get; set; } = Oai.Effect.Off;
        public double Speed { get; set; } = 0.5;

        /// Off is off, whatever colour was last set.
        public bool IsLit => Brightness > 0 && Effect != Oai.Effect.Off;
    }

    /// Per-key state, including ids past the 13 physical keys — the firmware
    /// accepts up to AG19 and so does this.
    public Dictionary<int, Key> Keys { get; } = new();

    /// Which key ids are bound to an AG keycode on the active layer, and so
    /// can light at all. Includes the dial (13, 14) and joystick (15–18).
    public HashSet<int> Bound { get; private set; } = new();

    public Oai.Zone KeysZone { get; private set; } = Oai.Zone.Dark;
    public Oai.Zone AmbientZone { get; private set; } = Oai.Zone.Dark;

    /// A short tail of RPC traffic, so a UI can show what it was told.
    public List<string> Traffic { get; } = new();

    public event Action<(string Method, JsonElement? Parameters)>? OnNotify;

    public string? Product => "Creator Micro 2 (emulated)";
    public int? ProductId => 0x8297;
    public string? Transport => "emulated";
    public string? Serial => "EMULATOR";
    public bool IsConnected { get; private set; }

    public event EventHandler<byte[]>? OnReportReceived;
    public event EventHandler<string>? OnDeviceLog;
    public event EventHandler<string>? OnDisconnect;

    private string _keymapJson = Serialize(StockKeymap());
    private readonly WireProtocol.Reassembler _reassembler = new();

    public PadEmulator()
    {
        RefreshBinding();
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        if (!IsConnected) return;
        IsConnected = false;
        OnDisconnect?.Invoke(this, null);
    }

    /// Decodes a client request report, asks the emulated firmware, and raises
    /// the response as one or more input reports — exactly the framing path
    /// real HID traffic takes, so the bridge above cannot tell the difference.
    ///
    /// Requests larger than a single report arrive fragmented; reassemble them
    /// (as the real firmware does) before dispatching.
    public async Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        foreach (var text in _reassembler.CompleteMessages(report))
        {
            await HandleRequestAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(string text, CancellationToken cancellationToken)
    {
        JsonElement request;
        try { request = JsonSerializer.Deserialize<JsonElement>(text)!; }
        catch { return; }

        if (!request.TryGetProperty("method", out var methodProp) ||
            !request.TryGetProperty("id", out var idProp)) return;

        var method = methodProp.GetString()!;
        var id = idProp.GetInt32();
        var parameters = request.TryGetProperty("params", out var p) ? p : default;

        // A real device is never synchronous; answer on the next turn so
        // callers that assume synchrony would deadlock on hardware.
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return;

        var (resultJson, error) = Handle(method, parameters);
        var response = error is null
            ? $"{{\"id\":{id},\"result\":{resultJson}}}"
            : $"{{\"id\":{id},\"error\":{{\"message\":{JsonSerializer.Serialize(error)}}}}}";

        foreach (var outReport in WireProtocol.EncodeReports(response))
            OnReportReceived?.Invoke(this, outReport);
    }

    /// Returns the result JSON, or an error message for methods this firmware
    /// does not register — the same "Method not found" the real one answers
    /// with.
    public (string? ResultJson, string? Error) Handle(string method, JsonElement? parameters)
    {
        switch (method)
        {
            case "sys.version":
                Note("sys.version");
                return ($"{{\"version\":\"{Firmware}\"}}", null);

            case "device.status":
                Note("device.status");
                return ("{\"battery\":87,\"is_charging\":true,\"layer_index\":1}", null);

            case "fs.list":
                Note("fs.list");
                return ("{\"files\":[\"keymap.json\"]}", null);

            case "fs.read":
            {
                if (parameters is not { } p || p.ValueKind != JsonValueKind.Object ||
                    !p.TryGetProperty("file", out var fileProp) || fileProp.ValueKind != JsonValueKind.String)
                    return (null, "missing `file`");
                var file = fileProp.GetString()!;
                if (file != "keymap.json") return (null, $"no such file: {file}");
                Note("fs.read keymap.json");
                // Double-encoded, exactly as the device returns it: `data` is a
                // JSON *string*, not a nested object.
                return (Serialize(new Dictionary<string, object?> { ["data"] = _keymapJson }), null);
            }

            case "fs.write":
            {
                if (parameters is not { } w || w.ValueKind != JsonValueKind.Object ||
                    !w.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.String)
                    return (null, "bad payload");
                var payload = dataProp.GetString()!;
                if (!IsJsonObject(payload)) return (null, "bad payload");
                _keymapJson = payload;
                RefreshBinding();
                Note($"fs.write keymap.json — {Bound.Count} keys now bound");
                return ("{\"ok\":1}", null);
            }

            case Oai.MethodThreads:
                ApplyThreads(parameters);
                return ("{\"ok\":1}", null);

            case Oai.MethodRgbConfig:
                ApplyZones(parameters);
                return ("{\"ok\":1}", null);

            case "host.focused_app":
                return ("{\"ok\":1}", null);

            default:
                // Notifications are not callable, and neither is anything else
                // the firmware never registered.
                return (null, "Method not found");
            }
        }

    private void ApplyThreads(JsonElement? parameters)
    {
        if (parameters is not { } listEl || listEl.ValueKind != JsonValueKind.Array)
        {
            Note("v.oai.thstatus — malformed, accepted anyway");
            return;
        }

        var changed = 0;
        var litIds = new List<int>();
        foreach (var entry in listEl.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out var idProp) ||
                !TryGetInt(idProp, out var id)) continue;
            litIds.Add(id);
            var key = Keys.TryGetValue(id, out var existing) ? existing : new Key();
            if (entry.TryGetProperty("c", out var cProp) && TryGetInt(cProp, out var c)) key.Color = c;
            if (entry.TryGetProperty("b", out var bProp) && TryGetDouble(bProp, out var b)) key.Brightness = b;
            if (entry.TryGetProperty("e", out var eProp) && TryGetInt(eProp, out var e))
                key.Effect = Enum.IsDefined(typeof(Oai.Effect), e) ? (Oai.Effect)e : Oai.Effect.Off;
            if (entry.TryGetProperty("s", out var sProp) && TryGetDouble(sProp, out var s)) key.Speed = s;
            Keys[id] = key;
            changed++;
        }

        var dark = litIds.Where(id => !Bound.Contains(id) && Keys.TryGetValue(id, out var k) && k.IsLit).ToList();
        Note($"v.oai.thstatus — {changed} key{(changed == 1 ? "" : "s")}" +
             (dark.Count == 0 ? "" : $", {dark.Count} unbound and so still dark"));

        OnNotify?.Invoke((Oai.MethodThreads, parameters));
    }

    private void ApplyZones(JsonElement? parameters)
    {
        if (parameters is not { } payloadEl || payloadEl.ValueKind != JsonValueKind.Object) return;
        if (payloadEl.TryGetProperty("keys", out var keysSide) && keysSide.ValueKind == JsonValueKind.Object)
            KeysZone = ZoneFromWire(keysSide);
        if (payloadEl.TryGetProperty("ambient", out var ambient) && ambient.ValueKind == JsonValueKind.Object)
            AmbientZone = ZoneFromWire(ambient);
        Note("v.oai.rgbcfg — zones");
    }

    private static Oai.Zone ZoneFromWire(JsonElement wire)
    {
        var effect = TryGetInt(wire.GetProperty("e"), out var eRaw)
            ? (Oai.Effect)eRaw : Oai.Effect.Off;
        return new Oai.Zone(
            effect,
            TryGetDouble(wire.GetProperty("b"), out var b) ? b : 0,
            TryGetDouble(wire.GetProperty("s"), out var s) ? s : 0.5,
            TryGetDouble(wire.GetProperty("m"), out var m) ? m : 1,
            TryGetInt(wire.GetProperty("c"), out var c) ? c : 0);
    }

    private static bool TryGetInt(JsonElement value, out int result)
    {
        result = 0;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var i)) { result = i; return true; }
            result = (int)value.GetDouble();
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(JsonElement value, out double result)
    {
        result = 0;
        if (value.ValueKind == JsonValueKind.Number)
        {
            result = value.TryGetInt64(out var l) ? (double)l : value.GetDouble();
            return true;
        }

        return false;
    }

    /// Press a key on the virtual pad.
    ///
    /// Only AG-bound keys report themselves; the rest send a keystroke to the
    /// OS, which is not this object's business — so pressing an unbound key
    /// here does nothing, just as it would on the pad.
    public void Press(int key)
    {
        if (!Bound.Contains(key))
        {
            Note($"key {key} pressed — not AG-bound, so it sent a keystroke instead");
            return;
        }

        Note($"key {key} pressed");
        Notify(Oai.NotifyHID, new Dictionary<string, object?> { ["k"] = $"AG{key:D2}", ["act"] = 1 });
        _ = Task.Delay(80).ContinueWith(_ => Notify(Oai.NotifyHID,
            new Dictionary<string, object?> { ["k"] = $"AG{key:D2}", ["act"] = 0 }));
    }

    /// The joystick's continuous report, for anything that reads it directly.
    public void DeflectJoystick(double angle, double distance) =>
        Notify(Oai.NotifyJoystick,
            new Dictionary<string, object?> { ["a"] = angle, ["d"] = distance });

    private void Notify(string method, Dictionary<string, object?> parameters)
    {
        var payload = JsonSerializer.SerializeToElement(parameters);
        OnNotify?.Invoke((method, payload));

        // The firmware pushes these as *input reports*, and in the abbreviated
        // envelope (`m`/`p`, not `method`/`params`). Everything above the
        // transport only ever sees reports, so without this the key path is
        // unreachable without hardware to hand — which is the one thing the
        // emulator exists to avoid.
        var envelope = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["m"] = method,
            ["p"] = parameters,
        });
        foreach (var report in WireProtocol.EncodeReports(envelope))
            OnReportReceived?.Invoke(this, report);
    }

    private void Note(string line)
    {
        Traffic.Add(line);
        if (Traffic.Count > 80) Traffic.RemoveRange(0, Traffic.Count - 80);
    }

    /// Reset to a factory pad: stock keymap, every light off.
    public void Reset()
    {
        _keymapJson = Serialize(StockKeymap());
        Keys.Clear();
        KeysZone = Oai.Zone.Dark;
        AmbientZone = Oai.Zone.Dark;
        Traffic.Clear();
        RefreshBinding();
        Note("reset to the stock F-key keymap");
    }

    private static bool IsJsonObject(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payload).ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    /// The map a pad ships with: F13–F24 across the keys, volume on the dial,
    /// a numpad ring on the joystick. Matches a real `fs.read` off a stock
    /// device, which is what makes the app's binding step meaningful here.
    private static string Serialize(Dictionary<string, object?> dict) =>
        JsonSerializer.Serialize(dict);

    private static Dictionary<string, object?> StockKeymap() =>
        new(StringComparer.OrdinalIgnoreCase)
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
                                    new() { "KC_F13", "KC_F14" },
                                    new() { "KC_F15", "KC_F16", "KC_F17", "KC_F18" },
                                    new() { "KC_F19", "KC_F20", "KC_F21", "KC_F22" },
                                    new() { "KC_F23", "KC_NONE", "KC_F24" },
                                },
                                ["encoders"] = new List<List<string>> { new() { "KC_VOLU", "KC_VOLD", "KC_MPLY" } },
                                ["joystick"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["type"] = "RADIAL",
                                    ["sectors"] = new List<Dictionary<string, object?>>
                                    {
                                        new() { ["k"] = "KI_X", ["a1"] = 0.1875, ["a2"] = 0.3125 },
                                        new() { ["k"] = "KC_P1", ["a1"] = 0.3125, ["a2"] = 0.4375 },
                                        new() { ["k"] = "KC_P2", ["a1"] = 0.4375, ["a2"] = 0.5625 },
                                        new() { ["k"] = "KC_P3", ["a1"] = 0.5625, ["a2"] = 0.6875 },
                                        new() { ["k"] = "KC_P4", ["a1"] = 0.6875, ["a2"] = 0.8125 },
                                        new() { ["k"] = "KC_P5", ["a1"] = 0.8125, ["a2"] = 0.9375 },
                                        new() { ["k"] = "KC_P6", ["a1"] = 0.9375, ["a2"] = 0.0625 },
                                        new() { ["k"] = "KC_P7", ["a1"] = 0.0625, ["a2"] = 0.1875 },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

    /// Every key id bound to a `KV_OAI_AG*` code anywhere on the active layer
    /// — the key grid, the encoder, or a joystick sector.
    private void RefreshBinding()
    {
        var found = new HashSet<int>();
        JsonElement doc;
        try { doc = JsonSerializer.Deserialize<JsonElement>(_keymapJson); }
        catch { Bound = found; return; }

        ScanValue(doc, found);
        Bound = found;

        static void ScanValue(JsonElement value, HashSet<int> found)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var text = value.GetString();
                        if (text is not null && text.StartsWith("KV_OAI_AG", StringComparison.Ordinal) &&
                            int.TryParse(text.AsSpan(9), out var n)) found.Add(n);
                        break;
                    }
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray()) ScanValue(item, found);
                    break;
                case JsonValueKind.Object:
                    foreach (var prop in value.EnumerateObject()) ScanValue(prop.Value, found);
                    break;
            }
        }
    }
}
