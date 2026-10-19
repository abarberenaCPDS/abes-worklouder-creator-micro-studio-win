namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

/// <summary>
/// Studio-owned, host-side lighting preferences. These settings are persisted
/// in config.json and are deliberately independent of the device keymap.
/// </summary>
public sealed class LightingPreferences
{
    public sealed record Look
    {
        private int _color;
        private double? _brightness;
        private double? _speed;

        public int Color { get => _color; set => _color = value & 0xFFFFFF; }
        public Oai.Effect Effect { get; set; }
        /// <summary>Null means full brightness before the global multiplier.</summary>
        public double? Brightness { get => _brightness; set => _brightness = value is { } v ? Unit(v) : null; }
        /// <summary>Null means use the global effect speed.</summary>
        public double? Speed { get => _speed; set => _speed = value is { } v ? Unit(v) : null; }

        public Look(int color, Oai.Effect effect = Oai.Effect.Solid, double? brightness = null, double? speed = null)
        {
            Color = color;
            Effect = effect;
            Brightness = brightness;
            Speed = speed;
        }

        public Oai.Thread Thread(int id, double globalBrightness, double globalSpeed) => new(id)
        {
            Color = Color,
            Brightness = globalBrightness * (Brightness ?? 1),
            Effect = Effect,
            Speed = Speed ?? globalSpeed,
        };

        public Oai.Zone Zone(double globalBrightness, double globalSpeed) =>
            new(Effect, globalBrightness * (Brightness ?? 1), Speed ?? globalSpeed, 1, Color);

        internal JsonObject ToJsonObject()
        {
            var result = new JsonObject
            {
                ["c"] = PackedRgb.HexString(Color),
                ["e"] = EffectName(Effect),
            };
            if (Brightness is { } brightness) result["b"] = brightness;
            if (Speed is { } speed) result["s"] = speed;
            return result;
        }

        internal Look Merge(JsonNode? value)
        {
            if (value is not JsonObject entry) return this with { };
            var result = this with { };
            if (TryColor(Get(entry, "c", "color"), out var color)) result.Color = color;
            if (TryEffect(Get(entry, "e", "effect"), out var effect)) result.Effect = effect;
            if (TryNumber(Get(entry, "b", "brightness"), out var brightness)) result.Brightness = brightness;
            if (TryNumber(Get(entry, "s", "speed"), out var speed)) result.Speed = speed;
            return result;
        }
    }

    public enum UnderglowMode { Status, Custom, Off }
    public enum BacklightMode { Off, Custom }

    public sealed record UnderglowPreference(UnderglowMode Mode, Look Look);
    public sealed record BacklightPreference(BacklightMode Mode, Look Look);

    public static readonly string[] StatusStates = { "blocked", "working", "done", "idle" };

    public static IReadOnlyDictionary<string, Look> DefaultStatus { get; } =
        new Dictionary<string, Look>(StringComparer.Ordinal)
        {
            ["blocked"] = new(0xFF2D2D, Oai.Effect.Breath),
            ["working"] = new(0xFFA000),
            ["done"] = new(0x00B0FF),
            ["idle"] = new(0x00C853),
        };

    public static UnderglowPreference DefaultUnderglow { get; } =
        new(UnderglowMode.Status, new Look(0x00C853, Oai.Effect.Rainbow, 1, 0.5));
    public static BacklightPreference DefaultBacklight { get; } =
        new(BacklightMode.Off, new Look(0xFFA000, Oai.Effect.Solid, 0.3, 0.5));

    private double _brightness = 1;
    private double _speed = 0.5;

    /// <summary>Global multiplier applied to all threads and zones, 0...1.</summary>
    public double Brightness { get => _brightness; set => _brightness = Unit(value); }
    /// <summary>Default effect speed, 0...1.</summary>
    public double Speed { get => _speed; set => _speed = Unit(value); }
    public Dictionary<string, Look> Status { get; } = new(StringComparer.Ordinal);
    public UnderglowPreference Underglow { get; set; } = DefaultUnderglow;
    public BacklightPreference Backlight { get; set; } = DefaultBacklight;
    /// <summary>At-rest looks keyed by physical LED key id (0...12).</summary>
    public Dictionary<int, Look> Keys { get; } = new();

    public LightingPreferences() => CopyDefaultStatus(Status);

    private LightingPreferences(LightingPreferences other)
    {
        Brightness = other.Brightness;
        Speed = other.Speed;
        foreach (var (state, look) in other.Status) Status[state] = look with { };
        Underglow = other.Underglow with { Look = other.Underglow.Look with { } };
        Backlight = other.Backlight with { Look = other.Backlight.Look with { } };
        foreach (var (key, look) in other.Keys) Keys[key] = look with { };
    }

    public static LightingPreferences Defaults => new();

    public Look LookForStatus(string state) =>
        Status.GetValueOrDefault(state) ?? Status.GetValueOrDefault("idle") ?? DefaultStatus["idle"];

    /// <summary>Parses the complete config document; malformed or missing input gets defaults.</summary>
    public static LightingPreferences Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults;
        try { return Parse(JsonNode.Parse(json)); }
        catch (JsonException) { return Defaults; }
    }

    public static LightingPreferences Parse(byte[] data)
    {
        try { return Parse(JsonNode.Parse(data)); }
        catch (JsonException) { return Defaults; }
    }

    /// <summary>Parses the config root's lighting field (or a lighting object directly).</summary>
    public static LightingPreferences Parse(JsonNode? rootOrLighting)
    {
        var lighting = rootOrLighting is JsonObject root && root.ContainsKey("lighting")
            ? root["lighting"] as JsonObject
            : rootOrLighting as JsonObject;
        var preferences = Defaults;
        if (lighting is null) return preferences;

        if (TryNumber(lighting["brightness"], out var brightness)) preferences.Brightness = brightness;
        if (TryNumber(lighting["speed"], out var speed)) preferences.Speed = speed;

        if (lighting["status"] is JsonObject status)
        {
            var ordered = StatusStates.Where(status.ContainsKey)
                .Concat(status.Select(pair => pair.Key).Where(key => !StatusStates.Contains(key, StringComparer.Ordinal)).Order(StringComparer.Ordinal));
            foreach (var state in ordered)
            {
                var basis = preferences.Status.GetValueOrDefault(state) ?? preferences.LookForStatus("idle");
                preferences.Status[state] = basis.Merge(status[state]);
            }
        }

        if (lighting["underglow"] is JsonObject underglow)
        {
            var mode = TryString(underglow["mode"], out var modeText) &&
                       Enum.TryParse<UnderglowMode>(modeText, true, out var parsedMode) && Enum.IsDefined(parsedMode)
                ? parsedMode : preferences.Underglow.Mode;
            preferences.Underglow = new UnderglowPreference(mode, preferences.Underglow.Look.Merge(underglow));
        }
        if (lighting["backlight"] is JsonObject backlight)
        {
            var mode = TryString(backlight["mode"], out var modeText) &&
                       Enum.TryParse<BacklightMode>(modeText, true, out var parsedMode) && Enum.IsDefined(parsedMode)
                ? parsedMode : preferences.Backlight.Mode;
            preferences.Backlight = new BacklightPreference(mode, preferences.Backlight.Look.Merge(backlight));
        }

        if (lighting["keys"] is JsonObject keys)
        {
            foreach (var (name, entry) in keys)
            {
                if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var key) || key is < 0 or >= Pad.KeyCount)
                    continue;
                if (entry is not JsonObject keyLook || !TryColor(Get(keyLook, "c", "color"), out _)) continue;
                preferences.Keys[key] = new Look(0).Merge(keyLook);
            }
        }

        return preferences;
    }

    /// <summary>The complete lighting value in config.json's compact field format.</summary>
    public JsonObject ToJsonObject()
    {
        var status = new JsonObject();
        foreach (var (state, look) in Status) status[state] = look.ToJsonObject();
        var underglow = Underglow.Look.ToJsonObject();
        underglow["mode"] = ModeName(Underglow.Mode);
        var backlight = Backlight.Look.ToJsonObject();
        backlight["mode"] = ModeName(Backlight.Mode);
        var result = new JsonObject
        {
            ["brightness"] = Brightness,
            ["speed"] = Speed,
            ["status"] = status,
            ["underglow"] = underglow,
            ["backlight"] = backlight,
        };
        if (Keys.Count > 0)
        {
            var keys = new JsonObject();
            foreach (var (key, look) in Keys.Where(pair => pair.Key is >= 0 and < Pad.KeyCount).OrderBy(pair => pair.Key))
                keys[key.ToString(CultureInfo.InvariantCulture)] = look.ToJsonObject();
            if (keys.Count > 0) result["keys"] = keys;
        }
        return result;
    }

    public string ToJsonString(bool indented = false) =>
        ToJsonObject().ToJsonString(new JsonSerializerOptions { WriteIndented = indented });

    public static LightingPreferences Load(StudioConfigStore? store = null)
    {
        store ??= StudioConfigStore.Standard;
        return Parse(store.Read());
    }

    /// <summary>Writes lighting via StudioConfigStore without replacing other config fields.</summary>
    public static void Save(LightingPreferences preferences, StudioConfigStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        (store ?? StudioConfigStore.Standard).Update(json => json["lighting"] = preferences.ToJsonObject());
    }

    /// <summary>Applies a small edit to the current on-disk preferences atomically.</summary>
    public static void Update(StudioConfigStore? store, Action<LightingPreferences> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        (store ?? StudioConfigStore.Standard).Update(json =>
        {
            var current = Parse(json);
            edit(current);
            json["lighting"] = current.ToJsonObject();
        });
    }

    /// <summary>Saves only draft changes, rebased on the latest on-disk value.</summary>
    public static LightingPreferences Save(
        LightingPreferences draft,
        LightingPreferences baseline,
        StudioConfigStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(baseline);
        var written = Defaults;
        (store ?? StudioConfigStore.Standard).Update(json =>
        {
            written = draft.Rebased(Parse(json), baseline);
            json["lighting"] = written.ToJsonObject();
        });
        return written;
    }

    public static void Reset(StudioConfigStore? store = null) =>
        (store ?? StudioConfigStore.Standard).Update(json => json.Remove("lighting"));

    /// <summary>Returns disk with the changes from baseline to this draft applied.</summary>
    public LightingPreferences Rebased(LightingPreferences disk, LightingPreferences baseline)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(baseline);
        var result = new LightingPreferences(disk);
        if (Brightness != baseline.Brightness) result.Brightness = Brightness;
        if (Speed != baseline.Speed) result.Speed = Speed;
        foreach (var state in Status.Keys.Union(baseline.Status.Keys, StringComparer.Ordinal))
        {
            if (Status.GetValueOrDefault(state) == baseline.Status.GetValueOrDefault(state)) continue;
            if (Status.TryGetValue(state, out var look)) result.Status[state] = look with { };
            else result.Status.Remove(state);
        }
        if (Underglow.Mode != baseline.Underglow.Mode) result.Underglow = result.Underglow with { Mode = Underglow.Mode };
        if (Underglow.Look != baseline.Underglow.Look) result.Underglow = result.Underglow with { Look = Underglow.Look with { } };
        if (Backlight.Mode != baseline.Backlight.Mode) result.Backlight = result.Backlight with { Mode = Backlight.Mode };
        if (Backlight.Look != baseline.Backlight.Look) result.Backlight = result.Backlight with { Look = Backlight.Look with { } };
        foreach (var key in Keys.Keys.Union(baseline.Keys.Keys))
        {
            if (Keys.GetValueOrDefault(key) == baseline.Keys.GetValueOrDefault(key)) continue;
            if (Keys.TryGetValue(key, out var look) && key is >= 0 and < Pad.KeyCount) result.Keys[key] = look with { };
            else result.Keys.Remove(key);
        }
        return result;
    }

    private static void CopyDefaultStatus(Dictionary<string, Look> destination)
    {
        foreach (var (state, look) in DefaultStatus) destination[state] = look with { };
    }

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;

    private static JsonNode? Get(JsonObject value, string shortName, string longName) =>
        value[shortName] ?? value[longName];

    private static bool TryString(JsonNode? value, out string text)
    {
        text = string.Empty;
        if (value is not JsonValue json || !json.TryGetValue<string>(out var found)) return false;
        text = found;
        return true;
    }

    private static bool TryNumber(JsonNode? value, out double number)
    {
        number = default;
        if (value is not JsonValue json) return false;
        if (json.TryGetValue<double>(out number)) return double.IsFinite(number);
        if (json.TryGetValue<int>(out var integer)) { number = integer; return true; }
        if (json.TryGetValue<long>(out var longInteger)) { number = longInteger; return true; }
        if (json.TryGetValue<string>(out var text)) return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
        return false;
    }

    private static bool TryColor(JsonNode? value, out int color)
    {
        color = default;
        if (TryString(value, out var text))
        {
            try
            {
                if (PackedRgb.FromHex(text) is { } parsed) { color = parsed; return true; }
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            return false;
        }
        if (!TryNumber(value, out var number) || number != Math.Truncate(number) || number is < 0 or > 0xFFFFFF) return false;
        color = (int)number;
        return true;
    }

    private static bool TryEffect(JsonNode? value, out Oai.Effect effect)
    {
        effect = default;
        if (TryNumber(value, out var number) && number is >= 0 and <= 6 && number == Math.Truncate(number))
        {
            effect = (Oai.Effect)(int)number;
            return true;
        }
        if (!TryString(value, out var text)) return false;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && Enum.IsDefined(typeof(Oai.Effect), numeric))
        {
            effect = (Oai.Effect)numeric;
            return true;
        }
        var normalized = new string(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        if (normalized == "breathing")
        {
            effect = Oai.Effect.Breath;
            return true;
        }
        foreach (var candidate in Enum.GetValues<Oai.Effect>())
        {
            if (normalized == new string(EffectName(candidate).Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray()) ||
                normalized == new string(candidate.ToString().Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray()))
            {
                effect = candidate;
                return true;
            }
        }
        return false;
    }

    private static string EffectName(Oai.Effect effect) => effect switch
    {
        Oai.Effect.Off => "off",
        Oai.Effect.Solid => "solid",
        Oai.Effect.Snake => "snake",
        Oai.Effect.Rainbow => "rainbow",
        Oai.Effect.Breath => "breath",
        Oai.Effect.Gradient => "gradient",
        Oai.Effect.ShallowBreath => "shallow-breath",
        _ => "solid",
    };

    private static string ModeName(UnderglowMode mode) => mode switch
    {
        UnderglowMode.Status => "status", UnderglowMode.Custom => "custom", _ => "off",
    };
    private static string ModeName(BacklightMode mode) => mode == BacklightMode.Custom ? "custom" : "off";
}
