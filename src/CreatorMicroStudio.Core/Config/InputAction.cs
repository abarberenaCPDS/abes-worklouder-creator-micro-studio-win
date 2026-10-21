namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

using System.Text.Json;
using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

/// <summary>A host-side action for one physical pad input; never a firmware keycode.</summary>
public sealed record InputAction(
    InputActionKind Kind,
    string? Value = null,
    int? Slot = null,
    InputDirection? Direction = null,
    int? Amount = null,
    MediaKey? Media = null)
{
    public static readonly IReadOnlyList<InputActionKind> AllKinds = Enum.GetValues<InputActionKind>();

    public static InputAction DefaultFor(int input) => input switch
    {
        7 => new(InputActionKind.NextTab),
        >= 0 and < Pad.KeyCount => new(InputActionKind.FocusAgent),
        Pad.DialClockwiseId => new(InputActionKind.Scroll, Direction: InputDirection.Down, Amount: 5),
        Pad.DialCounterClockwiseId => new(InputActionKind.Scroll, Direction: InputDirection.Up, Amount: 5),
        Pad.JoyNorthId => new(InputActionKind.ModelPrevious),
        Pad.JoySouthId => new(InputActionKind.ModelNext),
        Pad.JoyEastId => new(InputActionKind.ModelPickerConfirm),
        Pad.JoyWestId => new(InputActionKind.ModelPickerCancel),
        _ => new(InputActionKind.None),
    };

    public string Title => Kind switch
    {
        InputActionKind.None => "Do nothing",
        InputActionKind.FocusAgent => "Focus agent",
        InputActionKind.FocusSlot => "Focus agent slot",
        InputActionKind.NextTab => "Next tab",
        InputActionKind.PreviousTab => "Previous tab",
        InputActionKind.NextWorkspace => "Next workspace",
        InputActionKind.PreviousWorkspace => "Previous workspace",
        InputActionKind.FocusPane => "Focus pane",
        InputActionKind.ZoomPane => "Zoom pane",
        InputActionKind.SendKeys => "Send keys to agent",
        InputActionKind.HerdrCommand => "Herdr command",
        InputActionKind.InsertText => "Insert text",
        InputActionKind.SubmitPrompt => "Submit prompt",
        InputActionKind.EffortUp => "Effort up",
        InputActionKind.EffortDown => "Effort down",
        InputActionKind.ModelNext => "Next model",
        InputActionKind.ModelPrevious => "Previous model",
        InputActionKind.ModelPickerConfirm => "Confirm model picker",
        InputActionKind.ModelPickerCancel => "Cancel model picker",
        InputActionKind.Scroll => $"Scroll {Direction?.ToString().ToLowerInvariant()} ×{Amount}",
        InputActionKind.KeyboardShortcut => "Keyboard shortcut",
        InputActionKind.MediaKey => $"Media key: {Media}",
        InputActionKind.Voice => "Voice typing",
        InputActionKind.TypeText => "Type text",
        InputActionKind.OpenUrl => "Open URL, file, or app",
        InputActionKind.ShellCommand => "Run shell command",
        _ => Kind.ToString(),
    };

    /// <summary>
    /// The title plus its configured parameter, for lists, tooltips and
    /// confirmations: "Focus agent slot 3", "Submit prompt: Run the tests".
    /// </summary>
    public string Summary => Kind switch
    {
        InputActionKind.FocusSlot => $"Focus agent slot {Slot ?? 1}",
        InputActionKind.FocusPane => Direction is { } direction
            ? $"Focus pane {direction.ToString().ToLowerInvariant()}"
            : Title,
        InputActionKind.Scroll or InputActionKind.MediaKey => Title,
        InputActionKind.SendKeys or InputActionKind.HerdrCommand or InputActionKind.InsertText or
        InputActionKind.SubmitPrompt or InputActionKind.KeyboardShortcut or InputActionKind.TypeText or
        InputActionKind.OpenUrl or InputActionKind.ShellCommand =>
            Value is { Length: > 0 } value ? $"{Title}: {value}" : $"{Title} (not set)",
        _ => Title,
    };

    public string Category => Kind switch
    {
        InputActionKind.FocusAgent or InputActionKind.FocusSlot or InputActionKind.NextTab or
        InputActionKind.PreviousTab or InputActionKind.NextWorkspace or InputActionKind.PreviousWorkspace or
        InputActionKind.FocusPane or InputActionKind.ZoomPane or InputActionKind.SendKeys or
        InputActionKind.HerdrCommand => "Herdr / agents",
        InputActionKind.InsertText or InputActionKind.SubmitPrompt => "Prompts",
        InputActionKind.EffortUp or InputActionKind.EffortDown or InputActionKind.ModelNext or
        InputActionKind.ModelPrevious or InputActionKind.ModelPickerConfirm or
        InputActionKind.ModelPickerCancel => "Model & effort",
        InputActionKind.Scroll or InputActionKind.KeyboardShortcut or InputActionKind.MediaKey or
        InputActionKind.Voice or InputActionKind.TypeText => "System",
        InputActionKind.OpenUrl or InputActionKind.ShellCommand => "Launch",
        _ => "General",
    };

    public bool NeedsSystemInput => Kind is InputActionKind.Scroll or InputActionKind.KeyboardShortcut or
        InputActionKind.MediaKey or InputActionKind.Voice or InputActionKind.TypeText;

    public JsonNode ToJsonNode()
    {
        var name = InputActionKindNames.Name(Kind);
        if (Kind is not (InputActionKind.Voice or InputActionKind.FocusSlot or InputActionKind.FocusPane or
            InputActionKind.Scroll or InputActionKind.SendKeys or InputActionKind.HerdrCommand or
            InputActionKind.InsertText or InputActionKind.SubmitPrompt or InputActionKind.KeyboardShortcut or
            InputActionKind.MediaKey or InputActionKind.TypeText or InputActionKind.OpenUrl or
            InputActionKind.ShellCommand))
            return JsonValue.Create(name)!;

        var obj = new JsonObject { ["type"] = name };
        switch (Kind)
        {
            case InputActionKind.FocusSlot: obj["slot"] = Slot ?? 1; break;
            case InputActionKind.FocusPane: obj["direction"] = Direction?.ToString().ToLowerInvariant() ?? "right"; break;
            case InputActionKind.Scroll:
                obj["direction"] = Direction?.ToString().ToLowerInvariant() ?? "down";
                obj["amount"] = Math.Clamp(Amount ?? 3, 1, 20);
                break;
            case InputActionKind.SendKeys: obj["keys"] = Value ?? ""; break;
            case InputActionKind.HerdrCommand: obj["command"] = Value ?? ""; break;
            case InputActionKind.InsertText:
            case InputActionKind.SubmitPrompt:
            case InputActionKind.TypeText: obj["text"] = Value ?? ""; break;
            case InputActionKind.KeyboardShortcut: obj["keys"] = Value ?? ""; break;
            case InputActionKind.MediaKey: obj["key"] = (Media ?? MediaKey.PlayPause).ConfigName(); break;
            case InputActionKind.OpenUrl: obj["url"] = Value ?? ""; break;
            case InputActionKind.ShellCommand: obj["command"] = Value ?? ""; break;
        }
        return obj;
    }

    public static InputAction? Parse(JsonElement value, string? legacyText = null)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString() ?? "";
            if (InputActionKindNames.Parse(raw) is not { } kind) return null;
            if (raw.Equals("macro", StringComparison.OrdinalIgnoreCase))
                return new(InputActionKind.InsertText, legacyText ?? "");
            if (raw.Equals("voice", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(legacyText))
                return new(InputActionKind.InsertText, legacyText);
            return new(kind);
        }
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("type", out var typeNode) || typeNode.ValueKind != JsonValueKind.String ||
            InputActionKindNames.Parse(typeNode.GetString() ?? "") is not { } objectKind)
            return null;

        string? Text(params string[] names)
        {
            foreach (var name in names)
                if (value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String)
                    return field.GetString();
            return null;
        }
        int? Number(params string[] names)
        {
            foreach (var name in names)
                if (value.TryGetProperty(name, out var field) && field.TryGetInt32(out var number)) return number;
            return null;
        }
        var direction = Text("direction")?.ToLowerInvariant() switch
        {
            "up" => InputDirection.Up, "down" => InputDirection.Down,
            "left" => InputDirection.Left, "right" => InputDirection.Right, _ => (InputDirection?)null,
        };

        return objectKind switch
        {
            InputActionKind.FocusSlot => new(objectKind, Slot: Math.Clamp(Number("slot") ?? 1, 1, Pad.KeyCount)),
            InputActionKind.FocusPane => new(objectKind, Direction: direction ?? InputDirection.Right),
            InputActionKind.Scroll => new(objectKind, Direction: direction ?? InputDirection.Down,
                Amount: Math.Clamp(Number("amount", "lines") ?? 3, 1, 20)),
            InputActionKind.SendKeys => new(objectKind, Text("keys", "text") ?? ""),
            InputActionKind.HerdrCommand => new(objectKind, Text("command", "id") ?? ""),
            InputActionKind.InsertText or InputActionKind.SubmitPrompt or InputActionKind.TypeText =>
                new(objectKind, Text("text") ?? legacyText ?? ""),
            InputActionKind.KeyboardShortcut => new(objectKind, Text("keys", "shortcut") ?? ""),
            InputActionKind.MediaKey => new(objectKind, Media: MediaKeyNames.Parse(Text("key", "media")) ?? MediaKey.PlayPause),
            InputActionKind.OpenUrl => new(objectKind, Text("url", "target", "path", "app") ?? ""),
            InputActionKind.ShellCommand => new(objectKind, Text("command", "cmd") ?? ""),
            _ => new(objectKind),
        };
    }
}

public enum InputDirection { Up, Down, Left, Right }

public enum MediaKey { PlayPause, Next, Previous, VolumeUp, VolumeDown, Mute, BrightnessUp, BrightnessDown }

public enum InputActionKind
{
    None, FocusAgent, FocusSlot, NextTab, PreviousTab, NextWorkspace, PreviousWorkspace,
    FocusPane, ZoomPane, SendKeys, HerdrCommand, InsertText, SubmitPrompt,
    EffortUp, EffortDown, ModelNext, ModelPrevious, ModelPickerConfirm, ModelPickerCancel,
    Scroll, KeyboardShortcut, MediaKey, Voice, TypeText, OpenUrl, ShellCommand,
}

internal static class InputActionKindNames
{
    public static string Name(InputActionKind kind) => kind switch
    {
        InputActionKind.None => "none", InputActionKind.FocusAgent => "focus-agent",
        InputActionKind.FocusSlot => "focus-slot", InputActionKind.NextTab => "next-tab",
        InputActionKind.PreviousTab => "previous-tab", InputActionKind.NextWorkspace => "next-workspace",
        InputActionKind.PreviousWorkspace => "previous-workspace", InputActionKind.FocusPane => "focus-pane",
        InputActionKind.ZoomPane => "zoom-pane", InputActionKind.SendKeys => "send-keys",
        InputActionKind.HerdrCommand => "herdr-command", InputActionKind.InsertText => "insert-text",
        InputActionKind.SubmitPrompt => "submit-prompt", InputActionKind.EffortUp => "effort-up",
        InputActionKind.EffortDown => "effort-down", InputActionKind.ModelNext => "model-next",
        InputActionKind.ModelPrevious => "model-previous", InputActionKind.ModelPickerConfirm => "model-picker-confirm",
        InputActionKind.ModelPickerCancel => "model-picker-cancel", InputActionKind.Scroll => "scroll",
        InputActionKind.KeyboardShortcut => "keyboard-shortcut", InputActionKind.MediaKey => "media-key",
        InputActionKind.Voice => "voice", InputActionKind.TypeText => "type-text",
        InputActionKind.OpenUrl => "open-url", InputActionKind.ShellCommand => "shell-command",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static InputActionKind? Parse(string name)
    {
        var normal = Normalize(name);
        if (normal is "macro") return InputActionKind.InsertText;
        if (normal is "cycletabs") return InputActionKind.NextTab;
        if (normal is "shortcut") return InputActionKind.KeyboardShortcut;
        if (normal is "open") return InputActionKind.OpenUrl;
        if (normal is "shell") return InputActionKind.ShellCommand;
        if (normal is "media") return InputActionKind.MediaKey;
        if (normal is "prompt") return InputActionKind.SubmitPrompt;
        foreach (var kind in Enum.GetValues<InputActionKind>())
            if (Normalize(Name(kind)) == normal) return kind;
        return null;
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

internal static class MediaKeyNames
{
    public static string ConfigName(this MediaKey key) => key switch
    {
        MediaKey.PlayPause => "play-pause", MediaKey.VolumeUp => "volume-up", MediaKey.VolumeDown => "volume-down",
        MediaKey.BrightnessUp => "brightness-up", MediaKey.BrightnessDown => "brightness-down",
        _ => key.ToString().ToLowerInvariant(),
    };

    public static MediaKey? Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "play-pause" or "playpause" => MediaKey.PlayPause,
        "next" => MediaKey.Next, "previous" => MediaKey.Previous,
        "volume-up" or "volumeup" => MediaKey.VolumeUp,
        "volume-down" or "volumedown" => MediaKey.VolumeDown,
        "mute" => MediaKey.Mute,
        "brightness-up" or "brightnessup" => MediaKey.BrightnessUp,
        "brightness-down" or "brightnessdown" => MediaKey.BrightnessDown,
        _ => null,
    };
}
