namespace MicroManager.Core.Pad;

/// The Work Louder vendor lighting API, recovered from
/// `@worklouder/device-kit-oai`.
///
/// Two details are easy to get wrong, and both fail *silently* because the
/// firmware answers `{"ok":1}` to any payload it is handed:
///
///   1. Field names are abbreviated on the wire: `c b e s m`, not the full
///      names `lights.preview` uses.
///   2. `effect` is an integer here, not one of the effect strings.
public static class Oai
{
    public const string MethodThreads = "v.oai.thstatus";
    public const string MethodRgbConfig = "v.oai.rgbcfg";
    public const string MethodPreview = "lights.preview";

    public const string NotifyHID = "v.oai.hid";
    public const string NotifyJoystick = "v.oai.rad";

    public enum Effect : int
    {
        Off = 0,
        Solid = 1,
        Snake = 2,
        Rainbow = 3,
        Breath = 4,
        Gradient = 5,
        ShallowBreath = 6
    }

    public sealed class Thread
    {
        public int Id { get; set; }
        public int? Color { get; set; }
        public double? Brightness { get; set; }
        public Effect? Effect { get; set; }
        public double? Speed { get; set; }
        public bool? SyncKeys { get; set; }
        public bool? SyncAmbient { get; set; }

        public Thread(int id)
        {
            Id = id;
        }

        /// Only `Id` is required; omitted fields leave that aspect unchanged.
        public Dictionary<string, object?> Wire()
        {
            var wire = new Dictionary<string, object?> { ["id"] = Id };
            if (Color is { } c) wire["c"] = c;
            if (Brightness is { } b) wire["b"] = b;
            if (Effect is { } e) wire["e"] = (int)e;
            if (Speed is { } s) wire["s"] = s;
            if (SyncKeys is { } sk) wire["sk"] = sk ? 1 : 0;
            if (SyncAmbient is { } sa) wire["sa"] = sa ? 1 : 0;
            return wire;
        }
    }

    public sealed class Zone
    {
        public Effect Effect { get; set; } = Effect.Solid;
        public double Brightness { get; set; } = 1;
        public double Speed { get; set; } = 0.5;
        public double Magic { get; set; } = 1;
        public int Color { get; set; }

        public Dictionary<string, object?> Wire() =>
            new()
            {
                ["e"] = (int)Effect,
                ["b"] = Brightness,
                ["s"] = Speed,
                ["m"] = Magic,
                ["c"] = Color,
            };

        public static Zone Dark { get; } = new(Effect.Off, 0, 0.5, 1, 0);

        public Zone() { }
        public Zone(Effect effect, double brightness, double speed, double magic, int color)
        {
            Effect = effect;
            Brightness = brightness;
            Speed = speed;
            Magic = magic;
            Color = color;
        }
    }

    public static int? AgIndex(string? name)
    {
        if (name is null || !name.StartsWith("AG", StringComparison.Ordinal)) return null;
        return int.TryParse(name.AsSpan(2), out var n) ? n : null;
    }

    public static List<Dictionary<string, object?>> ThreadsParams(List<Thread> threads) =>
        threads.Select(t => t.Wire()).ToList();

    public static Dictionary<string, object?> RgbConfigParams(Zone keys, Zone ambient) =>
        new() { ["keys"] = keys.Wire(), ["ambient"] = ambient.Wire() };
}

/// The pad's physical key rows, matching the firmware's [2, 4, 4, 3] matrix.
/// Key index runs row-major from 0, so the six agent keys are 0...5.
public static class Pad
{
    public static readonly List<List<int>> Rows = new()
    {
        new() { 0, 1 },
        new() { 2, 3, 4, 5 },
        new() { 6, 7, 8, 9 },
        new() { 10, 11, 12 },
    };

    public const int KeyCount = 13;

    /// The firmware's keycode table goes to AG19, so clear the whole id space
    /// when turning everything off, not just the visible keys.
    public const int MaxThreadId = 19;

    /// Agent slot N lights and answers to `agentKeyIDs[N]`, in READING order:
    /// left to right, top to bottom as you look at the pad. The firmware's top
    /// row is wired right to left — key 0 is the top-RIGHT key — so reading
    /// order starts 1, 0. The other rows run left to right in index order.
    public static readonly List<int> AgentKeyIds = new() { 1, 0, 2, 3, 4, 5 };

    /// The rows as you look at the pad, for on-screen mirrors. `Rows` stays in
    /// firmware order because the keymap file is addressed that way.
    public static readonly List<List<int>> DisplayRows = new()
    {
        new() { 1, 0 },
        new() { 2, 3, 4, 5 },
        new() { 6, 7, 8, 9 },
        new() { 10, 11, 12 },
    };

    /// The agent slot a key answers for, or null for non-agent keys.
    public static int? AgentSlotFor(int key) => AgentKeyIds.IndexOf(key) >= 0 ? AgentKeyIds.IndexOf(key) : null;

    public const int StackKeyId = 6;
    public const int TabCycleKeyId = 7;
    public const int LandKeyId = 8;
    public static readonly List<int> VoiceKeyIds = new() { 10, 11 };
    public static readonly List<int> MacroKeyIds = new() { 9, 12 };

    public static readonly List<int> BoundKeyIds =
        AgentKeyIds.Concat(new[] { StackKeyId, TabCycleKeyId, LandKeyId })
            .Concat(VoiceKeyIds).Concat(MacroKeyIds).ToList();

    /// The dial and joystick emit keycodes like the keys do, so binding them
    /// to the AG codes above 12 turns rotations and deflections into the same
    /// `v.oai.hid` reports. These IDs never light — nothing is under them.
    ///
    /// The dial's encoder list is `[clockwise, counter-clockwise, press]`. Only
    /// the two rotations are bound: the press is left as the pad shipped it —
    /// the play/pause media key — so a click reaches the host as a keystroke and
    /// is nothing to the app, the way the macOS app leaves it too.
    public const int DialClockwiseId = 13;
    public const int DialCounterClockwiseId = 14;
    public const int JoyNorthId = 15;
    public const int JoyWestId = 16;
    public const int JoySouthId = 17;
    public const int JoyEastId = 18;

    public enum JoystickDirection { North, South, East, West }

    public static JoystickDirection? JoystickDirectionForKeyId(int keyId) => keyId switch
    {
        JoyNorthId => JoystickDirection.North,
        JoySouthId => JoystickDirection.South,
        JoyEastId => JoystickDirection.East,
        JoyWestId => JoystickDirection.West,
        _ => null
    };

    public static int? RowOf(int key) => Rows.FindIndex(keys => keys.Contains(key));

    /// Where a key sits in `Rows`, which is the shape the device keymap uses.
    public static (int Row, int Column)? PositionOf(int key)
    {
        for (var row = 0; row < Rows.Count; row++)
        {
            var keys = Rows[row];
            var column = keys.IndexOf(key);
            if (column >= 0) return (row, column);
        }

        return null;
    }
}

/// Packed 0xRRGGBB, which is how colours go on the wire.
public static class PackedRgb
{
    public static string HexString(int packed) => $"#{packed & 0xFFFFFF:X6}";

    /// `"#RRGGBB"` / `"#RGB"` → packed int. Mirrors `toColorInt` in lib/oai.js.
    public static int? FromHex(string hex)
    {
        var text = hex.StartsWith("#") ? hex[1..] : hex;
        if (text.Length == 3) text = string.Concat(text.Select(c => $"{c}{c}"));
        if (text.Length != 6) return null;
        return Convert.ToInt32(text, 16);
    }
}
