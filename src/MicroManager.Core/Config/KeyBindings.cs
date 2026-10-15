namespace MicroManager.Core.Config;

using MicroManager.Core.Pad;
using System.Text.Json;

/// User-configurable text macros for the spare keys.
///
/// Read from `$XDG_CONFIG_HOME/micromanager/config.json`
/// (`~/.config/micromanager/config.json` by default):
///
///     {
///       "keys": {
///         "9": "Open PRs for all active GitButler branches",
///         "12": "Run but pull",
///         "10+11": "Summarise what you are working on"
///       },
///       "claude": { "models": ["fable", "opus"] },
///       "codex":  { "models": ["gpt-5.6-sol", "gpt-5.6-codex"] }
///     }
///
/// A bound string is injected into the focused agent's prompt, unsubmitted.
/// `"10+11"` addresses the wide key as one; `"10"` and `"11"` address its
/// halves separately. Keys the file does not mention keep their defaults —
/// 9 and 12 have the defaults below, the wide key defaults to the voice key.
/// An empty string unbinds a key outright.
public sealed class KeyBindings : IEquatable<KeyBindings>
{
    public Dictionary<int, string> Texts { get; } = new();
    public List<string> ClaudeModels { get; } = new();
    public List<string> ClaudeEfforts { get; } = new();
    public List<string> CodexModels { get; } = new();

    public static readonly Dictionary<int, string> Defaults = new()
    {
        [9] = "Open PRs for all active GitButler branches",
        [12] = "Run but pull",
    };

    public static readonly List<string> DefaultClaudeModels = new() { "fable", "opus", "sonnet", "haiku" };
    public static readonly List<string> DefaultClaudeEfforts = new() { "low", "medium", "high", "xhigh", "max" };

    public KeyBindings()
    {
        foreach (var (k, v) in Defaults) Texts[k] = v;
        ClaudeModels.AddRange(DefaultClaudeModels);
        ClaudeEfforts.AddRange(DefaultClaudeEfforts);
    }

    public KeyBindings(Dictionary<int, string> texts, List<string> claudeModels, List<string> claudeEfforts,
        List<string> codexModels)
    {
        foreach (var (k, v) in texts) Texts[k] = v;
        ClaudeModels.AddRange(claudeModels);
        ClaudeEfforts.AddRange(claudeEfforts);
        CodexModels.AddRange(codexModels);
    }

    /// The string bound to a key, or null when the key does something else.
    public string? TextFor(int key) =>
        Texts.TryGetValue(key, out var text) && text.Length > 0 ? text : null;

    public static string ConfigPath()
    {
        var baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(baseDir, "micromanager", "config.json");
    }

    public static KeyBindings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath())) return new KeyBindings();
            return Parse(File.ReadAllBytes(ConfigPath()));
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

        return new KeyBindings(texts, claudeModels, claudeEfforts, codexModels);

        static List<string> StringArray(JsonElement obj, string key) =>
            obj.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString()))
                    .Select(e => e.GetString()!)
                    .ToList()
                : new List<string>();
    }

    private static List<int> KeyIdsFor(string name)
    {
        if (name == "10+11") return Pad.VoiceKeyIds;
        if (int.TryParse(name, out var id)) return new List<int> { id };
        return new List<int>();
    }

    public bool Equals(KeyBindings? other) =>
        other is not null &&
        Texts.Count == other.Texts.Count && !Texts.Except(other.Texts).Any() &&
        ClaudeModels.SequenceEqual(other.ClaudeModels) &&
        ClaudeEfforts.SequenceEqual(other.ClaudeEfforts) &&
        CodexModels.SequenceEqual(other.CodexModels);

    public override bool Equals(object? obj) => Equals(obj as KeyBindings);
    public override int GetHashCode() => HashCode.Combine(Texts.Count, ClaudeModels, ClaudeEfforts, CodexModels);
}
