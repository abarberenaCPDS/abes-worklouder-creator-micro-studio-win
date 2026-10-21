namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Keymap;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

/// Device keymap handling.
///
/// Per-key lighting only works on keys bound to `KV_OAI_AG*` keycodes, and only
/// when those bindings are on the ACTIVE layer — parking them on a spare layer
/// does nothing. Nothing reports a mismatch: `v.oai.thstatus` still answers
/// `{"ok":1}` for a key it cannot light, so a wrong keymap looks exactly like a
/// working one from the host side. Hence checking rather than assuming.
///
/// The file itself is double-encoded: `fs.read` returns `{"data": "<json>"}`
/// where the inner string is the real config.
public static class KeymapManager
{
    /// Indexed by key: key N is bound to `agCodes[N]`.
    public static readonly string[] AgCodes =
    {
        "KV_OAI_AG00", "KV_OAI_AG01", "KV_OAI_AG02",
        "KV_OAI_AG03", "KV_OAI_AG04", "KV_OAI_AG05",
        "KV_OAI_AG06", "KV_OAI_AG07", "KV_OAI_AG08",
        "KV_OAI_AG09", "KV_OAI_AG10", "KV_OAI_AG11",
        "KV_OAI_AG12",
    };

    /// The dial: clockwise, counter-clockwise. The third slot — the press — is
    /// left exactly as the pad arrived with it, which is the macOS app's
    /// behaviour: a stock pad ships that slot as the play/pause media key, and
    /// the port takes nothing from it.
    public static readonly string[] DialCodes = { "KV_OAI_AG13", "KV_OAI_AG14" };

    /// The stock key for the dial's press. The port leaves that slot alone, so
    /// anything else found there is somebody else's configuration — with the one
    /// exception below.
    public const string StockDialPressCode = "KC_MPLY";

    /// An earlier build of this port bound the dial's press to `AG19` to use the
    /// click itself. That left the slot carrying a code nothing else writes and
    /// the app no longer listens for, so a pad still holding it gets the stock
    /// key back on the next connect: a value this port is known to have written
    /// is the one thing to undo, and this is where it is noticed.
    public const string FormerDialPressCode = "KV_OAI_AG19";

    /// The joystick's four cardinal sectors, matched by the angle at their
    /// centre in the radial map. Diagonal sectors are left alone.
    public static readonly List<(double Centre, string Code)> JoystickCodes = new()
    {
        (0.25, "KV_OAI_AG15"),
        (0.50, "KV_OAI_AG16"),
        (0.75, "KV_OAI_AG17"),
        (0.00, "KV_OAI_AG18"),
    };

    /// A sector's centre angle, handling the one that wraps through zero.
    public static double SectorCentre(double a1, double a2)
    {
        var centre = a2 >= a1 ? (a1 + a2) / 2 : ((a1 + a2 + 1) / 2);
        return centre % 1;
    }

    /// The keycode each managed key must carry, addressed by its position in
    /// the [2, 4, 4, 3] matrix. Bindings are per key rather than per row: the
    /// stack key shares row 2 with three keys this app has no business
    /// touching, and rewriting the row would take their keycodes with it.
    public static IReadOnlyList<(int Row, int Column, string Code)> Bindings { get; } = InitializeBindings();

    private static List<(int Row, int Column, string Code)> InitializeBindings()
    {
        var bindings = new List<(int Row, int Column, string Code)>();
        foreach (var key in Pad.BoundKeyIds)
        {
            if (key >= AgCodes.Length) continue;
            var at = Pad.PositionOf(key);
            if (at is { } position) bindings.Add((position.Row, position.Column, AgCodes[key]));
        }

        return bindings;
    }

    public enum Failure
    {
        NoProfiles,
        NotAccepted,
        Unreadable
    }

    // MARK: - Parsing

    /// Unwraps the `{"data": "<json string>"}` envelope.
    public static JsonObject Parse(string? raw)
    {
        if (raw is null)
            throw new FailureException(Failure.Unreadable, "unexpected response shape");
        JsonNode? node;
        try { node = JsonNode.Parse(raw); }
        catch { throw new FailureException(Failure.Unreadable, "unexpected response shape"); }
        if (node is not JsonObject obj)
            throw new FailureException(Failure.Unreadable, "unexpected response shape");
        if (!obj.TryGetPropertyValue("data", out var dataNode) ||
            dataNode is not JsonValue dv || !dv.TryGetValue(out string? dataText))
            throw new FailureException(Failure.Unreadable, "missing or malformed `data` field");
        JsonNode? config;
        try { config = JsonNode.Parse(dataText); }
        catch { throw new FailureException(Failure.Unreadable, "missing or malformed `data` field"); }
        if (config is not JsonObject configObj)
            throw new FailureException(Failure.Unreadable, "unexpected response shape");
        return configObj;
    }

    /// `layer_index` from `device.status` is 1-based against this list, so the
    /// active layer is the first one.
    public static JsonArray? ActiveLayerKeymap(JsonObject config)
    {
        if (ActiveProfile(config) is not { } profileObj) return null;
        if (!AsArray(profileObj["layers"], out var layers)) return null;
        var layer = layers.FirstOrDefault();
        if (layer is not JsonObject layerObj) return null;
        if (!AsObject(layerObj["layout"], out var layout)) return null;
        if (!AsArray(layout["keymap"], out var keymap)) return null;
        return keymap;
    }

    public static JsonObject? ActiveLayerLayout(JsonObject config)
    {
        if (ActiveProfile(config) is not { } profileObj) return null;
        if (!AsArray(profileObj["layers"], out var layers)) return null;
        var layer = layers.FirstOrDefault();
        if (layer is not JsonObject layerObj) return null;
        return AsObject(layerObj["layout"], out var result) ? result : null;
    }

    private static JsonObject? ActiveProfile(JsonObject config)
    {
        if (!AsArray(config["profiles"], out var profiles) || profiles.Count == 0) return null;
        if (AsInt(config["activeProfileId"]) is not { } activeId)
            return profiles.OfType<JsonObject>().FirstOrDefault();
        return profiles.OfType<JsonObject>().FirstOrDefault(profile => AsInt(profile["id"]) == activeId);
    }

    /// <summary>
    /// AG input ids the active device profile can actually report after the
    /// managed bindings are applied. Missing stick sectors and dial push are
    /// capabilities, not failed user bindings.
    /// </summary>
    public static IReadOnlySet<int> AvailableInputIds(JsonObject config)
    {
        var available = new HashSet<int>();
        var keymap = ActiveLayerKeymap(config);
        var layout = ActiveLayerLayout(config);
        if (keymap is null || layout is null) return available;

        foreach (var (row, column, code) in Bindings)
        {
            if (row >= keymap.Count || keymap[row] is not JsonArray rowArray || column >= rowArray.Count ||
                rowArray[column]?.GetValue<string>() != code) continue;
            var input = Array.IndexOf(AgCodes, code);
            if (input >= 0) available.Add(input);
        }

        if (AsArray(layout["encoders"], out var encoders) && encoders.FirstOrDefault() is JsonArray dial)
        {
            if (dial.Count > 0 && dial[0]?.GetValue<string>() == DialCodes[0]) available.Add(Pad.DialClockwiseId);
            if (dial.Count > 1 && dial[1]?.GetValue<string>() == DialCodes[1]) available.Add(Pad.DialCounterClockwiseId);
            // AG19 is deliberately not rebound: firmware may keep stock KC_MPLY here.
        }

        if (AsObject(layout["joystick"], out var joystick) &&
            AsArray(joystick["sectors"], out var sectors))
        {
            foreach (var sector in sectors.OfType<JsonObject>())
            {
                if (!AsDouble(sector["a1"], out var a1) || !AsDouble(sector["a2"], out var a2)) continue;
                var centre = SectorCentre(a1!.Value, a2!.Value);
                var mapping = JoystickCodes.FirstOrDefault(entry => Math.Abs(entry.Centre - centre) < 0.01);
                if (mapping.Code is null) continue;
                if (sector["k"]?.GetValue<string>() != mapping.Code) continue;
                var id = mapping.Code switch
                {
                    "KV_OAI_AG15" => Pad.JoyNorthId,
                    "KV_OAI_AG16" => Pad.JoyWestId,
                    "KV_OAI_AG17" => Pad.JoySouthId,
                    "KV_OAI_AG18" => Pad.JoyEastId,
                    _ => -1,
                };
                if (id >= 0) available.Add(id);
            }
        }

        return available;
    }

    /// True when every managed key, the dial and the joystick's exposed
    /// cardinal sectors carry their AG codes on the active layer.
    public static bool IsAgentKeymapApplied(JsonObject config)
    {
        var keymap = ActiveLayerKeymap(config);
        if (keymap is null) return false;
        foreach (var (row, column, code) in Bindings)
        {
            if (row >= keymap.Count) return false;
            var rowArray = keymap[row] as JsonArray;
            if (rowArray is null || column >= rowArray.Count) return false;
            if (rowArray[column]?.GetValue<string>() != code) return false;
        }

        var layout = ActiveLayerLayout(config);
        if (layout is null) return false;
        if (AsArray(layout["encoders"], out var encoders) &&
            encoders.FirstOrDefault() is JsonArray dial)
        {
            if (dial.Count >= 2 &&
                (dial[0]?.GetValue<string>() != DialCodes[0] || dial[1]?.GetValue<string>() != DialCodes[1]))
                return false;

            // A pad an earlier build of this port already wrote `AG19` into is
            // not as it was found: the repair in `Apply` puts the stock key back.
            if (dial.Count >= 3 && dial[2]?.GetValue<string>() == FormerDialPressCode) return false;
        }

        if (AsObject(layout["joystick"], out var joystick) &&
            AsArray(joystick["sectors"], out var sectors))
        {
            // Only the cardinal sectors the device actually exposes need
            // binding. A radial stick may carry two (firmware 0.6.3 answers
            // with just 0.25/0.75) and the host cannot invent the missing
            // ones, so demanding all four would make the keymap impossible
            // to satisfy. Any exposed cardinal with the wrong code still
            // fails, so an unbound stick is still caught.
            foreach (var sector in sectors.OfType<JsonObject>())
            {
                if (!AsDouble(sector["a1"], out var a1) ||
                    !AsDouble(sector["a2"], out var a2)) continue;
                var centre = SectorCentre(a1!.Value, a2!.Value);
                var match = JoystickCodes.FirstOrDefault(c => Math.Abs(c.Centre - centre) < 0.01);
                if (string.IsNullOrEmpty(match.Code)) continue;
                if (sector["k"]?.GetValue<string>() != match.Code) return false;
            }
        }

        return true;
    }

    /// Rebinds the managed keys, leaving every other key, layer, encoder,
    /// joystick and lighting setting untouched.
    public static void Apply(JsonObject config)
    {
        var profileObj = ActiveProfile(config);
        if (profileObj is null)
            throw new FailureException(Failure.NoProfiles, "no profiles");
        if (!AsArray(profileObj["layers"], out var layers) || layers.Count == 0)
            throw new FailureException(Failure.NoProfiles, "no layers");
        var layer = layers[0];
        if (layer is not JsonObject layerObj)
            throw new FailureException(Failure.NoProfiles, "no layer");
        if (!AsObject(layerObj["layout"], out var layout) ||
            !AsArray(layout["keymap"], out var keymap))
            throw new FailureException(Failure.NoProfiles, "no layout or keymap");

        foreach (var (row, column, code) in Bindings)
        {
            if (row < keymap.Count && column < keymap[row].AsArray().Count)
                keymap[row].AsArray()[column] = code;
        }

        // The dial's rotations. Its press is the third slot, and is left as it
        // was found — except for the code an earlier build of this port wrote
        // there, which is given back to the stock media key.
        if (AsArray(layout["encoders"], out var encoders) &&
            encoders.FirstOrDefault() is JsonArray dial && dial.Count >= 2)
        {
            dial[0] = DialCodes[0];
            dial[1] = DialCodes[1];
            if (dial.Count >= 3 && dial[2]?.GetValue<string>() == FormerDialPressCode)
                dial[2] = StockDialPressCode;
        }

        // The joystick's cardinal sectors; diagonals keep their keycodes.
        if (AsObject(layout["joystick"], out var joystick) &&
            AsArray(joystick["sectors"], out var sectors))
        {
            foreach (var sector in sectors.OfType<JsonObject>())
            {
                if (!AsDouble(sector["a1"], out var a1) ||
                    !AsDouble(sector["a2"], out var a2)) continue;
                var centre = SectorCentre(a1!.Value, a2!.Value);
                var match = JoystickCodes.FirstOrDefault(c => Math.Abs(c.Centre - centre) < 0.01);
                if (!string.IsNullOrEmpty(match.Code)) sector["k"] = match.Code;
            }
        }
    }

    // MARK: - Device I/O

    public static async Task<JsonObject> ReadAsync(IWlDevice device)
    {
        var response = await device.RequestAsync("fs.read",
            new Dictionary<string, object?> { ["file"] = "keymap.json" });
        if (response.Error is not null)
            throw new FailureException(Failure.Unreadable, response.Error);
        return Parse(response.Result as string);
    }

    /// Returns true when it had to change something.
    public static async Task<bool> ApplyAsync(IWlDevice device)
    {
        var config = await ReadAsync(device);
        if (IsAgentKeymapApplied(config)) return false;

        Apply(config);
        var text = config.ToJsonString();
        var writeResponse = await device.RequestAsync("fs.write",
            new Dictionary<string, object?> { ["file"] = "keymap.json", ["data"] = text });
        if (writeResponse.Error is not null)
            throw new FailureException(Failure.NotAccepted, "write not accepted");

        var after = await ReadAsync(device);
        if (!IsAgentKeymapApplied(after)) throw new FailureException(Failure.NotAccepted, "keymap not accepted");
        return true;
    }

    // MARK: - Helpers

    private static int? AsInt(JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)l;
            if (v.TryGetValue(out double d)) return (int)d;
        }

        return null;
    }

    private static bool AsArray(JsonNode? node, [NotNullWhen(true)] out JsonArray? result)
    {
        result = node as JsonArray;
        return result is not null;
    }

    private static bool AsObject(JsonNode? node, [NotNullWhen(true)] out JsonObject? result)
    {
        result = node as JsonObject;
        return result is not null;
    }

    private static bool AsDouble(JsonNode? node, [NotNullWhen(true)] out double? result)
    {
        result = null;
        if (node is JsonValue v)
        {
            if (v.TryGetValue(out double d)) { result = d; return true; }
            if (v.TryGetValue(out int i)) { result = i; return true; }
            if (v.TryGetValue(out long l)) { result = l; return true; }
        }

        return false;
    }
}

public sealed class FailureException : Exception
{
    public KeymapManager.Failure Failure { get; }
    public FailureException(KeymapManager.Failure failure, string message) : base(message) => Failure = failure;
}
