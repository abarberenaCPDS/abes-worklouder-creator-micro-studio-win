namespace MicroManager.Core.Config;

using MicroManager.Core.Herdr;
using MicroManager.Core.Pad;

/// Maps a set of Herdr agents to device lighting.
///
/// Keys are individually addressable, so each agent gets its own key. The
/// underglow keeps a worst-state-wins aggregate, which is the part you can read
/// from across the room without counting keys.
///
/// Kept in step with `lib/status.js`; the defaults below are that file's.
public sealed class BridgeConfig
{
    /// Highest priority first: the first state present across all agents wins
    /// the underglow.
    public List<string> Priority { get; set; } = new() { "blocked", "working", "unknown", "idle", "done" };

    public Dictionary<string, int> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blocked"] = 0xFF2D2D,
        ["working"] = 0xFFA000,
        // Herdr reports "done" for an agent that has finished and that you
        // have not looked at yet, and "idle" once it is focused. That is a
        // real difference - one is waiting to be read, the other is just
        // quiet - so give them different colours.
        ["done"] = 0x00B0FF,
        ["idle"] = 0x00C853,
        ["unknown"] = 0x00C853,
    };

    public Dictionary<string, Oai.Effect> Effects { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blocked"] = Oai.Effect.Breath,
        ["working"] = Oai.Effect.Solid,
        ["idle"] = Oai.Effect.Solid,
        ["done"] = Oai.Effect.Solid,
        ["unknown"] = Oai.Effect.Solid,
    };

    public int StackColor { get; set; } = 0x7C4DFF;
    public int TabCycleColor { get; set; } = 0x00BFA5;
    public int LandColor { get; set; } = 0xE91E63;
    public int VoiceColor { get; set; } = 0xECEFF1;
    public int VoiceActiveColor { get; set; } = 0xFF3B30;
    public int MacroColor { get; set; } = 0x90A4AE;
    public double Brightness { get; set; } = 1;
    public double Speed { get; set; } = 0.5;

    /// Leave the key-backlight ZONE alone: keys are painted per-agent instead,
    /// and a zone colour would only compete with them.
    public bool DriveBacklight { get; set; }

    /// Bind the keys this app drives to `KV_OAI_AG*` on startup. Required for
    /// per-key colour to work at all.
    public bool ManageKeymap { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2.5);
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(100);

    public int ColorFor(string state) => Colors.TryGetValue(state, out var c) ? c : 0xFFFFFF;
    public Oai.Effect EffectFor(string state) => Effects.TryGetValue(state, out var e) ? e : Oai.Effect.Solid;
}

public static class StatusMapper
{
    /// The winning state across all agents, or null when none are running.
    public static string? Aggregate(List<HerdrAgent> agents, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        if (agents.Count == 0) return null;
        var present = new HashSet<string>(agents.Select(a => a.AgentStatus), StringComparer.OrdinalIgnoreCase);
        foreach (var state in cfg.Priority)
        {
            if (present.Contains(state)) return state;
        }

        return "unknown";
    }

    /// One thread entry per agent key: agent slot N takes `Pad.agentKeyIDs[N]`
    /// — reading order on the pad — and unused keys go dark. Always returns
    /// exactly `Pad.AgentKeyIds.Count` entries, so a vanished agent actively
    /// clears its key rather than leaving it stale.
    public static List<Oai.Thread> ThreadsFor(List<HerdrAgent> agents, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        var threads = new List<Oai.Thread>();
        for (var slot = 0; slot < Pad.AgentKeyIds.Count; slot++)
        {
            var id = Pad.AgentKeyIds[slot];
            if (slot >= agents.Count)
            {
                threads.Add(new Oai.Thread(id) { Brightness = 0, Effect = Oai.Effect.Off });
                continue;
            }

            var state = agents[slot].AgentStatus;
            threads.Add(new Oai.Thread(id)
            {
                Color = cfg.ColorFor(state),
                Brightness = cfg.Brightness,
                Effect = cfg.EffectFor(state),
                Speed = cfg.Speed,
            });
        }

        return threads;
    }

    public static Oai.Thread StackThread(bool open, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        return new Oai.Thread(Pad.StackKeyId)
        {
            Color = cfg.StackColor,
            Brightness = open ? cfg.Brightness : cfg.Brightness * 0.3,
            Effect = open ? Oai.Effect.Breath : Oai.Effect.Solid,
            Speed = cfg.Speed,
        };
    }

    public static Oai.Thread TabCycleThread(BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        return new Oai.Thread(Pad.TabCycleKeyId)
        {
            Color = cfg.TabCycleColor,
            Brightness = cfg.Brightness * 0.3,
            Effect = Oai.Effect.Solid,
            Speed = cfg.Speed,
        };
    }

    public static Oai.Thread LandThread(bool open, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        return new Oai.Thread(Pad.LandKeyId)
        {
            Color = cfg.LandColor,
            Brightness = open ? cfg.Brightness : cfg.Brightness * 0.3,
            Effect = open ? Oai.Effect.Breath : Oai.Effect.Solid,
            Speed = cfg.Speed,
        };
    }

    public static Oai.Thread VoiceThread(int id, bool active, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        return new Oai.Thread(id)
        {
            Color = active ? cfg.VoiceActiveColor : cfg.VoiceColor,
            Brightness = active ? cfg.Brightness : cfg.Brightness * 0.25,
            Effect = active ? Oai.Effect.Breath : Oai.Effect.Solid,
            Speed = cfg.Speed,
        };
    }

    public static Oai.Thread MacroThread(int id, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        return new Oai.Thread(id)
        {
            Color = cfg.MacroColor,
            Brightness = cfg.Brightness * 0.25,
            Effect = Oai.Effect.Solid,
            Speed = cfg.Speed,
        };
    }

    public static Oai.Zone? ZoneFor(string? state, BridgeConfig? cfg = null)
    {
        cfg ??= new BridgeConfig();
        if (state is null) return null;
        return new Oai.Zone(cfg.EffectFor(state), cfg.Brightness, cfg.Speed, 1, cfg.ColorFor(state));
    }
}
