namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

public class StatusMapperTests
{
    [Fact]
    public void NoAgentsMeansNoState()
    {
        Assert.Null(StatusMapper.Aggregate(new List<HerdrAgent>()));
    }

    [Fact]
    public void BlockedOutranksWorkingAndIdle()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "working", null, null, false),
            new(null, null, null, null, "b", "blocked", null, null, false),
            new(null, null, null, null, "c", "idle", null, null, false),
        };
        Assert.Equal("blocked", StatusMapper.Aggregate(agents));
    }

    [Fact]
    public void WorkingOutranksIdleAndDone()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "idle", null, null, false),
            new(null, null, null, null, "b", "working", null, null, false),
            new(null, null, null, null, "c", "done", null, null, false),
        };
        Assert.Equal("working", StatusMapper.Aggregate(agents));
    }

    [Fact]
    public void UnrecognisedStatusStillYieldsAState()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "flipping", null, null, false),
        };
        Assert.Equal("unknown", StatusMapper.Aggregate(agents));
    }

    [Fact]
    public void BlockedAgentBreathesSoItReadsDifferently()
    {
        var cfg = new BridgeConfig();
        var threads = StatusMapper.ThreadsFor(new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "blocked", null, null, false),
        }, cfg);

        var blocked = threads.First(t => t.Color == cfg.ColorFor("blocked"));
        Assert.Equal(Oai.Effect.Breath, blocked.Effect);
    }

    [Fact]
    public void EachAgentGetsItsOwnKeyInReadingOrder()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, "pane1", null, null, "a", "idle", null, null, false),
            new(null, "pane2", null, null, "b", "idle", null, null, false),
        };
        var threads = StatusMapper.ThreadsFor(agents);

        // agentKeyIDs = [1, 0, 2, 3, 4, 5]; slot 0 -> key 1, slot 1 -> key 0
        var byId = threads.ToDictionary(t => t.Id);
        Assert.Equal(6, threads.Count);
        Assert.True(byId[1].Brightness > 0);
        Assert.True(byId[0].Brightness > 0);
        Assert.Equal(byId[1].Color, byId[0].Color);
    }

    [Fact]
    public void KeysWithNoAgentAreSwitchedOffNotLeftStale()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "idle", null, null, false),
        };
        var threads = StatusMapper.ThreadsFor(agents);

        // Only slot 0 is used (key 1). Keys 0, 2, 3, 4, 5 go dark.
        Assert.All(threads.Where(t => t.Id != 1), t =>
        {
            Assert.Equal(0, t.Brightness);
            Assert.Equal(Oai.Effect.Off, t.Effect);
        });
        var used = threads.First(t => t.Id == 1);
        Assert.True(used.Brightness > 0);
    }

    [Fact]
    public void MoreAgentsThanKeysDoesNotOverflow()
    {
        var agents = Enumerable.Range(0, 20).Select(i =>
            new HerdrAgent(null, null, null, null, $"a{i}", "idle", null, null, false)).ToList();
        var threads = StatusMapper.ThreadsFor(agents);
        Assert.Equal(Pad.AgentKeyIds.Count, threads.Count);
    }

    [Fact]
    public void ConfigOverridesAreHonoured()
    {
        var cfg = new BridgeConfig { Colors = { ["blocked"] = 0x123456 } };
        var agents = new List<HerdrAgent>
        {
            new(null, null, null, null, "a", "blocked", null, null, false),
        };
        var threads = StatusMapper.ThreadsFor(agents, cfg);
        Assert.Equal(0x123456, threads.First(t => t.Id == 1).Color);
    }

    [Fact]
    public void ThreadWireUsesAbbreviatedKeysAndNumericEffect()
    {
        var thread = new Oai.Thread(1) { Color = 0xFF0000, Effect = Oai.Effect.Breath, Speed = 0.7 };
        var wire = thread.Wire();
        Assert.Equal(1, wire["id"]);
        Assert.Equal(0xFF0000, wire["c"]);
        Assert.Equal((int)Oai.Effect.Breath, wire["e"]);
        Assert.Equal(0.7, wire["s"]);
    }

    [Fact]
    public void UnsetThreadFieldsAreOmittedSoTheyStayUnchanged()
    {
        var thread = new Oai.Thread(5);
        var wire = thread.Wire();
        Assert.Single(wire); // only id
        Assert.Equal(5, wire["id"]);
    }

    [Fact]
    public void ColourRoundTrip()
    {
        var packed = 0x1A2B3C;
        Assert.Equal("#1A2B3C", PackedRgb.HexString(packed));
        Assert.Equal(packed, PackedRgb.FromHex("#1A2B3C"));
        Assert.Equal(0x112233, PackedRgb.FromHex("#123"));
    }

    [Fact]
    public void AgentSlotAndKeyAreInverses()
    {
        for (var slot = 0; slot < Pad.AgentKeyIds.Count; slot++)
            Assert.Equal(slot, Pad.AgentSlotFor(Pad.AgentKeyIds[slot]));
    }

    [Fact]
    public void AGNamesMapToKeyIndices()
    {
        Assert.Equal(0, Oai.AgIndex("AG00"));
        Assert.Equal(19, Oai.AgIndex("AG19"));
        Assert.Null(Oai.AgIndex("KC_F13"));
        Assert.Null(Oai.AgIndex(null));
    }

    [Fact]
    public void DoneAndIdleAreToldApartByColour()
    {
        var cfg = new BridgeConfig();
        Assert.NotEqual(cfg.ColorFor("done"), cfg.ColorFor("idle"));
        Assert.Equal(Oai.Effect.Solid, cfg.EffectFor("done"));
        Assert.Equal(Oai.Effect.Solid, cfg.EffectFor("idle"));
    }

    [Fact]
    public void EveryStatusColourIsPinned()
    {
        var cfg = new BridgeConfig();
        foreach (var state in new[] { "blocked", "working", "done", "idle", "unknown" })
        {
            Assert.NotEqual(0xFFFFFF, cfg.ColorFor(state));
        }
        Assert.Equal(Oai.Effect.Breath, cfg.EffectFor("blocked"));
    }

    [Fact]
    public void DoneAndIdleAgentsGetDifferentKeys()
    {
        var agents = new List<HerdrAgent>
        {
            new(null, "p1", null, null, "a", "done", null, null, false),
            new(null, "p2", null, null, "b", "idle", null, null, false),
        };
        var threads = StatusMapper.ThreadsFor(agents);
        var byId = threads.ToDictionary(t => t.Id);
        Assert.NotEqual(byId[1].Color, byId[0].Color);
    }

    [Fact]
    public void StackAndTabCycleThreadsAreAlwaysPresent()
    {
        var agents = new List<HerdrAgent>();
        var threads = StatusMapper.ThreadsFor(agents);
        foreach (var t in threads)
        {
            Assert.Equal(0, t.Brightness);
            Assert.Equal(Oai.Effect.Off, t.Effect);
        }

        var stack = StatusMapper.StackThread(false);
        Assert.Equal(Pad.StackKeyId, stack.Id);
        Assert.Equal(Oai.Effect.Solid, stack.Effect);

        var tab = StatusMapper.TabCycleThread();
        Assert.Equal(Pad.TabCycleKeyId, tab.Id);
        Assert.True(tab.Brightness > 0);
    }
}
