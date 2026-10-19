namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

/// <summary>
/// Driving the pad's lights by hand.
///
/// This is the lighting half of the macOS **Inspector** — pick keys, pick a
/// colour and an effect, apply — which is the only place in either
/// implementation where a person chooses a colour. The menu-bar app has no such
/// UI at all; its palette is the compiled-in <c>BridgeConfig</c>.
///
/// On the Mac an Inspector write lasts only until the bridge's next poll, so it
/// is normally used with the bridge stopped. Here the app suspends its own
/// painting instead, which is why a chosen colour stays put.
/// </summary>
public sealed class LightingEditor
{
    private readonly HashSet<int> _selection = new();

    /// The edit applied to the selected keys.
    public int Color { get; set; } = 0xFFA000;

    /// The underglow is the `ambient` zone rather than a key, so it carries its
    /// own edit.
    public int AmbientColor { get; set; } = 0x00C853;

    public Oai.Effect Effect { get; set; } = Oai.Effect.Solid;
    public double Brightness { get; set; } = 1;
    public double Speed { get; set; } = 0.5;

    public Oai.Effect AmbientEffect { get; set; } = Oai.Effect.Solid;
    public double AmbientBrightness { get; set; } = 1;
    public double AmbientSpeed { get; set; } = 0.5;

    public IReadOnlyCollection<int> Selection => _selection;

    public bool IsSelected(int keyId) => _selection.Contains(keyId);

    public void Toggle(int keyId)
    {
        if (!_selection.Add(keyId)) _selection.Remove(keyId);
    }

    public void SelectNone() => _selection.Clear();

    /// Every key, as the Inspector's "All".
    public void SelectEveryKey()
    {
        _selection.Clear();
        for (var id = 0; id < Pad.KeyCount; id++) _selection.Add(id);
    }

    /// The six agent keys, as the Inspector's "Top 6".
    public void SelectAgentKeys()
    {
        _selection.Clear();
        foreach (var id in Pad.AgentKeyIds) _selection.Add(id);
    }

    /// The edit as one thread per selected key, in key order.
    public List<Oai.Thread> SelectedThreads() => _selection.Order().Select(Lit).ToList();

    /// The whole keys zone, painted one colour rather than key by key.
    public Oai.Zone KeysZone() => new(Effect, Brightness, Speed, 1, Color);

    public Oai.Zone AmbientZone() =>
        new(AmbientEffect, AmbientBrightness, AmbientSpeed, 1, AmbientColor);

    private Oai.Thread Lit(int keyId) => new(keyId)
    {
        Color = Color,
        Effect = Effect,
        Brightness = Brightness,
        Speed = Speed,
    };

    /// A dark thread for every id the firmware knows, so clearing reaches the
    /// dial and joystick codes as well — its table runs to AG19. Zones have to
    /// be cleared too: thread state paints over zone state, but a lit zone would
    /// still show around and under the keys.
    public static List<Oai.Thread> AllOffThreads() =>
        Enumerable.Range(0, Pad.MaxThreadId + 1)
            .Select(id => new Oai.Thread(id)
            {
                Brightness = 0,
                Effect = Oai.Effect.Off,
                SyncKeys = false,
                SyncAmbient = false,
            })
            .ToList();

    /// Dark threads for the given keys only, leaving the rest of the pad alone.
    public static List<Oai.Thread> OffThreads(IEnumerable<int> keys) =>
        keys.Order().Select(id => new Oai.Thread(id)
        {
            Brightness = 0,
            Effect = Oai.Effect.Off,
        }).ToList();
}
