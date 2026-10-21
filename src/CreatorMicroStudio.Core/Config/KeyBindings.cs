namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using System.Text.Json;
using System.Text.Json.Nodes;

/// User-configurable text macros for the spare keys.
///
/// Read from `$XDG_CONFIG_HOME/creator-micro-studio/config.json`
/// (`~/.config/creator-micro-studio/config.json` by default); the former
/// `micromanager/config.json` location is read as a legacy migration fallback:
///
///     {
///       "keys": {
///         "9": "Open a pull request for this branch",
///         "12": "Run the test suite",
///         "10+11": "Summarise what you are working on"
///       },
///       "agentKeys": [1, 0, 2, 3],
///       "dial": "scroll",
///       "scroll": { "lines": 5 },
///       "claude": { "models": ["fable", "opus"] },
///       "codex":  { "models": ["gpt-5.6-sol", "gpt-5.6-codex"] }
///     }
///
/// `"agentKeys"` assigns any of the thirteen keycaps to agent slots, in physical
/// reading order. Missing or malformed values keep the stock six-key layout; an
/// empty array is valid and assigns none. A custom action on a layout key takes
/// precedence over its agent status.
///
/// A bound string is injected into the focused agent's prompt, unsubmitted.
/// `"10+11"` addresses the wide key as one; `"10"` and `"11"` address its
/// halves separately. Keys the file does not mention keep their defaults —
/// 9 and 12 have the defaults below, the wide key defaults to the voice key.
/// An empty string unbinds a key outright.
///
/// `"dial"` is `"scroll"` or `"effort"` and decides what a turn of the dial
/// does; see `TuneLadder.DialMode`.
///
/// `"scroll"` says how a detent scrolls: `"lines"` is how far Herdr moves a pane
/// whose output it holds, and `"up"` / `"down"` are the keys sent to a pane that
/// draws itself — empty by default, because Herdr can send no wheel and no page
/// key. Turning the dial right sends `"down"` and left sends `"up"`; see
/// `TuneLadder.ScrollSettings`.
public sealed class KeyBindings : IEquatable<KeyBindings>
{
    public Dictionary<int, string> Texts { get; } = new();
    /// <summary>Explicit host-side action overrides, keyed by AG input id.</summary>
    public Dictionary<int, InputAction> Actions { get; } = new();
    /// <summary>Keycaps assigned to agent slots, in physical reading order.</summary>
    public List<int> AgentKeyIds { get; } = new();
    public List<string> ClaudeModels { get; } = new();
    public List<string> ClaudeEfforts { get; } = new();
    public List<string> CodexModels { get; } = new();

    /// What a turn of the dial does. Scrolling by default: a dial is the one
    /// control on the pad that is good at long output, and effort is one config
    /// line away from coming back.
    public TuneLadder.DialMode Dial { get; }

    /// How the dial scrolls, when it is scrolling.
    public TuneLadder.ScrollSettings Scroll { get; }

    /// The word that selects a dial mode, or null when it is not one. Both the
    /// file and the docs use these, so they live in one place.
    public static TuneLadder.DialMode? DialModeNamed(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "scroll" => TuneLadder.DialMode.Scroll,
            "effort" => TuneLadder.DialMode.Effort,
            _ => null,
        };

    public static readonly Dictionary<int, string> Defaults = new()
    {
        [9] = "Open a pull request for this branch",
        [12] = "Run the test suite",
    };

    public static readonly List<string> DefaultClaudeModels = new() { "fable", "opus", "sonnet", "haiku" };
    public static readonly List<string> DefaultClaudeEfforts = new() { "low", "medium", "high", "xhigh", "max" };

    public KeyBindings()
    {
        foreach (var (k, v) in Defaults) Texts[k] = v;
        AgentKeyIds.AddRange(Pad.AgentKeyIds);
        ClaudeModels.AddRange(DefaultClaudeModels);
        ClaudeEfforts.AddRange(DefaultClaudeEfforts);
        Dial = TuneLadder.DialMode.Scroll;
        Scroll = TuneLadder.ScrollSettings.Default;
    }

    public KeyBindings(Dictionary<int, string> texts, List<string> claudeModels, List<string> claudeEfforts,
        List<string> codexModels, TuneLadder.DialMode dial, TuneLadder.ScrollSettings scroll,
        Dictionary<int, InputAction>? actions = null, IEnumerable<int>? agentKeyIds = null)
    {
        foreach (var (k, v) in texts) Texts[k] = v;
        if (actions is not null)
            foreach (var (k, v) in actions.Where(pair => pair.Key is >= 0 and <= Pad.MaxThreadId)) Actions[k] = v;
        AgentKeyIds.AddRange(NormalizeAgentKeys(agentKeyIds ?? Pad.AgentKeyIds) ?? Pad.AgentKeyIds);
        ClaudeModels.AddRange(claudeModels);
        ClaudeEfforts.AddRange(claudeEfforts);
        CodexModels.AddRange(codexModels);
        Dial = dial;
        Scroll = scroll;
    }

    /// The string bound to a key, or null when the key does something else.
    public string? TextFor(int key) =>
        Texts.TryGetValue(key, out var text) && text.Length > 0 ? text : null;

    /// The agent slot a keycap answers for, or null when it is not a layout key.
    /// Slots are positions in <see cref="AgentKeyIds"/>, which is physical
    /// reading order rather than key id order.
    public int? AgentSlotFor(int key)
    {
        var slot = AgentKeyIds.IndexOf(key);
        return slot >= 0 ? slot : null;
    }

    public InputAction ActionFor(int input)
    {
        if (Actions.TryGetValue(input, out var action)) return action;
        return DefaultActionFor(input);
    }

    /// The default an input would have if no explicit action overrode it. A key
    /// in the agent layout defaults to Focus Agent: that is what listing it in
    /// `agentKeys` means, and it keeps a hand-written layout working even where
    /// Windows gives a key a different stock default (key 7 cycles tabs).
    private InputAction DefaultActionFor(int input)
    {
        if (input is >= 0 and < Pad.KeyCount && AgentSlotFor(input) is not null)
            return new InputAction(InputActionKind.FocusAgent);
        return StockActionFor(input);
    }

    private InputAction StockActionFor(int input)
    {
        if (TextFor(input) is { } text && (Pad.MacroKeyIds.Contains(input) || Pad.VoiceKeyIds.Contains(input)))
            return new InputAction(InputActionKind.InsertText, text);
        if (Pad.VoiceKeyIds.Contains(input)) return new InputAction(InputActionKind.Voice);
        if (input == Pad.DialClockwiseId || input == Pad.DialCounterClockwiseId)
        {
            if (Dial == TuneLadder.DialMode.Effort)
                return new InputAction(input == Pad.DialClockwiseId ? InputActionKind.EffortUp : InputActionKind.EffortDown);
            var step = Scroll.Lines;
            return new InputAction(InputActionKind.Scroll,
                Direction: input == Pad.DialClockwiseId ? InputDirection.Down : InputDirection.Up,
                Amount: step);
        }
        return InputAction.DefaultFor(input);
    }

    public bool IsActionCustomized(int input) =>
        Actions.TryGetValue(input, out var action) && action != DefaultActionFor(input);

    /// <summary>Persists an action override without changing the device keymap or other config sections.</summary>
    public static void SetAction(int input, InputAction? action, StudioConfigStore? store = null) =>
        SetActions(new[] { input }, action, store);

    /// <summary>Atomically applies one action override to several inputs.</summary>
    public static void SetActions(IEnumerable<int> inputs, InputAction? action, StudioConfigStore? store = null)
    {
        var inputIds = inputs.Distinct().Order().ToArray();
        foreach (var input in inputIds)
            if (input is < 0 or > Pad.MaxThreadId) throw new ArgumentOutOfRangeException(nameof(inputs));
        store ??= StudioConfigStore.Standard;
        store.Update(json =>
        {
            var actions = json["actions"] as JsonObject ?? new JsonObject();
            foreach (var input in inputIds)
            {
                if (input is 10 or 11 && actions.TryGetPropertyValue("10+11", out var paired))
                {
                    var other = input == 10 ? 11 : 10;
                    if (!inputIds.Contains(other) && actions[other.ToString()] is null && paired is not null)
                        actions[other.ToString()] = paired.DeepClone();
                    actions.Remove("10+11");
                }
                foreach (var alias in actions.Select(entry => entry.Key)
                             .Where(name => !int.TryParse(name, out _) && InputIdsFor(name).Contains(input))
                             .ToArray())
                    actions.Remove(alias);
                if (action is null) actions.Remove(input.ToString());
                else actions[input.ToString()] = action.ToJsonNode();
            }
            if (actions.Count == 0) json.Remove("actions");
            else json["actions"] = actions;
        });
    }

    /// Validates, de-duplicates and canonicalises keycaps into physical reading
    /// order, or null when any id is not a keycap.
    public static List<int>? NormalizeAgentKeys(IEnumerable<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var requested = keys.ToList();
        if (requested.Any(key => key < 0 || key >= Pad.KeyCount)) return null;
        var selected = requested.ToHashSet();
        return Pad.DisplayRows.SelectMany(row => row).Where(selected.Contains).ToList();
    }

    /// <summary>
    /// Saves a physical-reading-order layout and resets newly assigned keys to
    /// Focus Agent in the same atomic config update. Removed keys keep their
    /// current actions. Duplicates are collapsed and order is normalised.
    /// When supplied, <paramref name="expectedBindings"/> refuses to replace a
    /// layout or key action that changed since it was reviewed.
    /// </summary>
    public static void SetAgentKeys(IEnumerable<int> keys, KeyBindings? expectedBindings = null,
        StudioConfigStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var requested = keys.ToList();
        var normalized = NormalizeAgentKeys(requested);
        if (normalized is null)
        {
            var invalid = requested.FirstOrDefault(key => key < 0 || key >= Pad.KeyCount, -1);
            throw new AgentLayoutEditException(
                $"Key {invalid} is not a physical keycap that can be assigned to an agent slot.");
        }

        var layout = normalized;
        (store ?? StudioConfigStore.Standard).Update(json =>
        {
            if (expectedBindings is not null)
            {
                var current = Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
                var unchanged = current.AgentKeyIds.SequenceEqual(expectedBindings.AgentKeyIds) &&
                    Pad.ConfigurableActionKeyIds
                        .All(input => current.ActionFor(input) == expectedBindings.ActionFor(input));
                if (!unchanged)
                    throw new AgentLayoutEditException(
                        "The agent-key layout or key actions changed since you reviewed them. Review the current bindings and try again.",
                        configurationChanged: true);
            }

            var existing = ParseAgentKeys(json);
            var newlyAdded = layout.Where(key => !existing.Contains(key)).ToList();
            var actions = json["actions"] as JsonObject ?? new JsonObject();
            foreach (var key in newlyAdded)
            {
                foreach (var alias in actions.Select(entry => entry.Key)
                             .Where(name => !int.TryParse(name, out _) && InputIdsFor(name).SequenceEqual(new[] { key }))
                             .ToArray())
                {
                    actions.Remove(alias);
                }
                actions[key.ToString()] = new InputAction(InputActionKind.FocusAgent).ToJsonNode();
            }

            json["agentKeys"] = new JsonArray(layout.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
            if (actions.Count == 0) json.Remove("actions");
            else json["actions"] = actions;
        });
    }

    public static string ConfigPath()
    {
        var baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(baseDir, "creator-micro-studio", "config.json");
    }

    /// <summary>Pre-rebrand config location, read only until the Studio file is created.</summary>
    public static string LegacyConfigPath()
    {
        var baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "micromanager", "config.json");
    }

    public static KeyBindings Load(StudioConfigStore? store = null)
    {
        try
        {
            var json = (store ?? StudioConfigStore.Standard).Read();
            return json.Count == 0 ? new KeyBindings() : Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        }
        catch
        {
            return new KeyBindings();
        }
    }

    /// A malformed file falls back to the defaults rather than a dead pad.
    public static KeyBindings Parse(byte[] data)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(data);
        }
        catch
        {
            return new KeyBindings();
        }

        using var _ = doc;
        var root = doc.RootElement;

        var texts = new Dictionary<int, string>(Defaults);
        if (root.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in keys.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var text = prop.Value.GetString() ?? string.Empty;
                foreach (var key in KeyIdsFor(prop.Name))
                {
                    if (text.Length == 0) texts.Remove(key);
                    else texts[key] = text;
                }
            }
        }

        var claudeModels = DefaultClaudeModels;
        var claudeEfforts = DefaultClaudeEfforts;
        if (root.TryGetProperty("claude", out var claude) && claude.ValueKind == JsonValueKind.Object)
        {
            var models = StringArray(claude, "models");
            if (models.Count > 0) claudeModels = models;
            var efforts = StringArray(claude, "efforts");
            if (efforts.Count > 0) claudeEfforts = efforts;
        }

        var codexModels = new List<string>();
        if (root.TryGetProperty("codex", out var codex) && codex.ValueKind == JsonValueKind.Object)
        {
            codexModels = StringArray(codex, "models");
        }

        // An unrecognised word keeps the default rather than leaving the dial
        // dead — the same forgiving reading the rest of this file gets.
        var dial = TuneLadder.DialMode.Scroll;
        if (root.TryGetProperty("dial", out var dialProp) && dialProp.ValueKind == JsonValueKind.String &&
            DialModeNamed(dialProp.GetString()) is { } named)
        {
            dial = named;
        }

        var scroll = TuneLadder.ScrollSettings.Default;
        if (root.TryGetProperty("scroll", out var scrollProp) && scrollProp.ValueKind == JsonValueKind.Object)
        {
            var lines = scroll.Lines;
            if (scrollProp.TryGetProperty("lines", out var linesProp) &&
                linesProp.ValueKind == JsonValueKind.Number &&
                linesProp.TryGetInt32(out var parsedLines) && parsedLines > 0)
            {
                lines = parsedLines;
            }

            scroll = new TuneLadder.ScrollSettings(
                lines,
                KeyName(scrollProp, "up", scroll.Up),
                KeyName(scrollProp, "down", scroll.Down));
        }

        var actions = new Dictionary<int, InputAction>();
        if (root.TryGetProperty("actions", out var actionMap) && actionMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in actionMap.EnumerateObject()
                         .OrderBy(prop => InputKeyRank(prop.Name))
                         .ThenBy(prop => prop.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var id in InputIdsFor(prop.Name))
                {
                    var action = InputAction.Parse(prop.Value, texts.GetValueOrDefault(id));
                    if (action is not null) actions[id] = action;
                }
            }
        }

        return new KeyBindings(texts, claudeModels, claudeEfforts, codexModels, dial, scroll, actions,
            ParseAgentKeys(root));

        static string KeyName(JsonElement obj, string name, string fallback) =>
            obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : fallback;

        static List<string> StringArray(JsonElement obj, string key) =>
            obj.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString()))
                    .Select(e => e.GetString()!)
                    .ToList()
                : new List<string>();
    }

    /// Missing or malformed layouts preserve the stock layout. An empty array
    /// is valid and intentionally assigns no keycaps.
    private static List<int> ParseAgentKeys(JsonElement root)
    {
        if (!root.TryGetProperty("agentKeys", out var value) || value.ValueKind != JsonValueKind.Array)
            return Pad.AgentKeyIds;

        var keys = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) ||
                !double.IsFinite(number) || number != Math.Truncate(number) ||
                number < 0 || number >= Pad.KeyCount)
                return Pad.AgentKeyIds;
            keys.Add((int)number);
        }

        return NormalizeAgentKeys(keys) ?? Pad.AgentKeyIds;
    }

    private static List<int> ParseAgentKeys(JsonObject json)
    {
        if (json["agentKeys"] is not JsonArray array) return Pad.AgentKeyIds;

        var keys = new List<int>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !TryNumber(value, out var number) ||
                number != Math.Truncate(number) || number < 0 || number >= Pad.KeyCount)
                return Pad.AgentKeyIds;
            keys.Add((int)number);
        }

        return NormalizeAgentKeys(keys) ?? Pad.AgentKeyIds;
    }

    private static bool TryNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue<double>(out number)) return true;
        if (value.TryGetValue<int>(out var integer))
        {
            number = integer;
            return true;
        }

        number = 0;
        return false;
    }

    private static List<int> KeyIdsFor(string name)
    {
        if (name == "10+11") return Pad.VoiceKeyIds;
        if (int.TryParse(name, out var id)) return new List<int> { id };
        return new List<int>();
    }

    private static List<int> InputIdsFor(string name)
    {
        if (name.Trim() == "10+11") return Pad.VoiceKeyIds;
        if (int.TryParse(name, out var id) && id is >= 0 and <= Pad.MaxThreadId) return new() { id };
        return name.Trim().ToLowerInvariant() switch
        {
            "dial-right" or "dial-cw" or "dial-clockwise" => new() { Pad.DialClockwiseId },
            "dial-left" or "dial-ccw" or "dial-counter-clockwise" => new() { Pad.DialCounterClockwiseId },
            "dial-push" or "dial-press" or "dial-click" => new() { Pad.DialPushId },
            "joy-up" or "joy-north" or "joystick-up" => new() { Pad.JoyNorthId },
            "joy-left" or "joy-west" or "joystick-left" => new() { Pad.JoyWestId },
            "joy-down" or "joy-south" or "joystick-down" => new() { Pad.JoySouthId },
            "joy-right" or "joy-east" or "joystick-right" => new() { Pad.JoyEastId },
            _ => new(),
        };
    }

    private static int InputKeyRank(string name) =>
        name.Trim() == "10+11" ? 0 : int.TryParse(name, out _) ? 2 : 1;

    public bool Equals(KeyBindings? other) =>
        other is not null &&
        Texts.Count == other.Texts.Count && !Texts.Except(other.Texts).Any() &&
        Actions.Count == other.Actions.Count && !Actions.Except(other.Actions).Any() &&
        AgentKeyIds.SequenceEqual(other.AgentKeyIds) &&
        ClaudeModels.SequenceEqual(other.ClaudeModels) &&
        ClaudeEfforts.SequenceEqual(other.ClaudeEfforts) &&
        CodexModels.SequenceEqual(other.CodexModels) &&
        Dial == other.Dial &&
        Scroll == other.Scroll;

    public override bool Equals(object? obj) => Equals(obj as KeyBindings);
    public override int GetHashCode() => HashCode.Combine(
        Texts.Count, Actions.Count, string.Join(',', AgentKeyIds),
        ClaudeModels, ClaudeEfforts, CodexModels, Dial, Scroll);
}

/// <summary>Raised when an agent layout cannot be saved.</summary>
public sealed class AgentLayoutEditException(string message, bool configurationChanged = false) : Exception(message)
{
    /// <summary>True when the on-disk layout or key actions changed since review.</summary>
    public bool ConfigurationChanged { get; } = configurationChanged;
}
