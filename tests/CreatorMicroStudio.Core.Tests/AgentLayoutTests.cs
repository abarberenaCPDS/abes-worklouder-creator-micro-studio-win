using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using DevicePad = AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad.Pad;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

/// Host-side agent layout: which keycaps answer for which agent slot, in
/// physical reading order. None of this touches the firmware keymap — it is
/// `agentKeys` plus the Focus Agent overrides that keep a newly assigned key
/// quiet until an agent arrives.
public sealed class AgentLayoutTests
{
    [Fact]
    public void StockLayoutIsSixKeysInReadingOrder()
    {
        var bindings = new KeyBindings();
        Assert.Equal(new[] { 1, 0, 2, 3, 4, 5 }, bindings.AgentKeyIds);
        Assert.Equal(bindings.AgentKeyIds,
            KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("{}")).AgentKeyIds);
    }

    /// Slots are positions in the layout, not key ids: slot 0 is the top-right
    /// key (1) because that is where reading starts on the reversed top row.
    [Fact]
    public void AgentSlotForCountsPositionsInReadingOrder()
    {
        var bindings = new KeyBindings();
        Assert.Equal(0, bindings.AgentSlotFor(1));
        Assert.Equal(1, bindings.AgentSlotFor(0));
        Assert.Equal(2, bindings.AgentSlotFor(2));
        Assert.Equal(3, bindings.AgentSlotFor(3));
        Assert.Equal(4, bindings.AgentSlotFor(4));
        Assert.Equal(5, bindings.AgentSlotFor(5));
        Assert.Null(bindings.AgentSlotFor(6));
        Assert.Null(bindings.AgentSlotFor(DevicePad.DialClockwiseId));
    }

    /// The file may list the layout in any order; it is canonicalised to
    /// reading order and duplicates collapse.
    [Theory]
    [InlineData("""{ "agentKeys": [5, 4, 3, 2, 1, 0] }""", new[] { 1, 0, 2, 3, 4, 5 })]
    [InlineData("""{ "agentKeys": [0, 1, 0, 2, 2] }""", new[] { 1, 0, 2 })]
    [InlineData("""{ "agentKeys": [4, 0, 12] }""", new[] { 0, 4, 12 })]
    [InlineData("""{ "agentKeys": [] }""", new int[0])]
    public void AgentKeysParseIntoReadingOrderWithoutDuplicates(string json, int[] expected) =>
        Assert.Equal(expected, KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json)).AgentKeyIds);

    [Fact]
    public void AllThirteenKeysCanBeAgentSlots()
    {
        var json = """{ "agentKeys": [12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0] }""";
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(new[] { 1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, bindings.AgentKeyIds);
    }

    /// A hand-written layout must work for every keycap, including ones whose
    /// Windows stock default differs (key 7 cycles tabs) or that inherit a
    /// legacy text binding (key 9). Layout membership means Focus Agent unless
    /// an explicit action says otherwise; outside the layout the stock
    /// defaults are untouched.
    [Fact]
    public void AHandWrittenLayoutAssignsFocusAgentToEveryListedKey()
    {
        var json = """
        {
          "agentKeys": [1, 0, 7, 9],
          "keys": { "7": "legacy tab text", "9": "legacy macro text" }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Equal(new InputAction(InputActionKind.FocusAgent), bindings.ActionFor(7));
        Assert.Equal(new InputAction(InputActionKind.FocusAgent), bindings.ActionFor(9));
        Assert.False(bindings.IsActionCustomized(7));
        Assert.False(bindings.IsActionCustomized(9));

        var stock = new KeyBindings();
        Assert.Equal(new InputAction(InputActionKind.NextTab), stock.ActionFor(7));
        Assert.Equal(InputActionKind.InsertText, stock.ActionFor(9).Kind);

        var explicitAction = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(
            """{ "agentKeys": [7], "actions": { "7": "next-tab" } }"""));
        Assert.Equal(new InputAction(InputActionKind.NextTab), explicitAction.ActionFor(7));
        Assert.True(explicitAction.IsActionCustomized(7));
    }

    /// A layout we cannot read is not worth a dead pad, so the stock six keys
    /// stay. An empty array is different: it is a deliberate "no agents here".
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "agentKeys": 5 }""")]
    [InlineData("""{ "agentKeys": "1,0" }""")]
    [InlineData("""{ "agentKeys": true }""")]
    [InlineData("""{ "agentKeys": null }""")]
    [InlineData("""{ "agentKeys": [0, 1.5] }""")]
    [InlineData("""{ "agentKeys": [-1, 2] }""")]
    [InlineData("""{ "agentKeys": [13] }""")]
    [InlineData("""{ "agentKeys": [0, "1"] }""")]
    [InlineData("""{ "agentKeys": [0, null] }""")]
    public void MalformedAgentKeysKeepTheStockLayout(string json) =>
        Assert.Equal(DevicePad.AgentKeyIds, KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json)).AgentKeyIds);

    [Fact]
    public void LoadingACustomLayoutDoesNotReplaceExistingActions()
    {
        WithStore(store =>
        {
            store.Update(json =>
            {
                json["agentKeys"] = new JsonArray(3, 0);
                json["actions"] = new JsonObject
                {
                    ["0"] = new JsonObject { ["type"] = "submit-prompt", ["text"] = "keep me" },
                    ["3"] = "zoom-pane",
                    ["9"] = new JsonObject { ["type"] = "insert-text", ["text"] = "elsewhere" },
                };
            });

            var bindings = KeyBindings.Load(store);
            Assert.Equal(new[] { 0, 3 }, bindings.AgentKeyIds);
            Assert.Equal(new InputAction(InputActionKind.SubmitPrompt, "keep me"), bindings.ActionFor(0));
            Assert.Equal(new InputAction(InputActionKind.ZoomPane), bindings.ActionFor(3));
            Assert.Equal(new InputAction(InputActionKind.InsertText, "elsewhere"), bindings.ActionFor(9));

            // Reading is not a write.
            Assert.NotNull(store.Read()["actions"]?["0"]);
        });
    }

    [Fact]
    public void SetAgentKeysNormalisesTheLayoutAndOnlyResetsNewlyAddedKeys()
    {
        WithStore(store =>
        {
            store.Update(json =>
            {
                json["agentKeys"] = new JsonArray(1, 0, 2, 3);
                json["actions"] = new JsonObject
                {
                    // Existing layout key that stays: its action survives.
                    ["1"] = new JsonObject { ["type"] = "submit-prompt", ["text"] = "keep reviewing" },
                    // Removed from the layout: its action survives too.
                    ["2"] = new JsonObject { ["type"] = "insert-text", ["text"] = "removed key keeps this" },
                    // Newly added: reset to Focus Agent.
                    ["4"] = "voice",
                    // Unrelated key and aliases are left alone.
                    ["6"] = "zoom-pane",
                    ["10+11"] = "voice",
                    ["dial-right"] = "effort-up",
                };
                json["keymap"] = new JsonObject { ["profile"] = "stock" };
                json["future"] = new JsonObject { ["keep"] = true };
                json["lighting"] = new JsonObject { ["brightness"] = 0.4 };
            });

            KeyBindings.SetAgentKeys(new[] { 5, 4, 1, 0 }, store: store);

            var saved = store.Read();
            Assert.Equal(new[] { 1, 0, 4, 5 },
                saved["agentKeys"]!.AsArray().Select(node => node!.GetValue<int>()));
            var actions = saved["actions"]!;
            Assert.Equal("keep reviewing", actions["1"]?["text"]?.GetValue<string>());
            Assert.Equal("removed key keeps this", actions["2"]?["text"]?.GetValue<string>());
            Assert.Equal("focus-agent", actions["4"]?.GetValue<string>());
            Assert.Equal("focus-agent", actions["5"]?.GetValue<string>());
            Assert.Equal("zoom-pane", actions["6"]?.GetValue<string>());
            Assert.Equal("voice", actions["10+11"]?.GetValue<string>());
            Assert.Equal("effort-up", actions["dial-right"]?.GetValue<string>());

            // Host-side config only: the device keymap and other sections stay put.
            Assert.Equal("stock", saved["keymap"]?["profile"]?.GetValue<string>());
            Assert.True(saved["future"]?["keep"]?.GetValue<bool>());
            Assert.Equal(0.4, saved["lighting"]?["brightness"]?.GetValue<double>());

            var reloaded = KeyBindings.Load(store);
            Assert.Equal(new[] { 1, 0, 4, 5 }, reloaded.AgentKeyIds);
            Assert.Equal(new InputAction(InputActionKind.SubmitPrompt, "keep reviewing"), reloaded.ActionFor(1));
            Assert.Equal(new InputAction(InputActionKind.InsertText, "removed key keeps this"), reloaded.ActionFor(2));
            Assert.Equal(new InputAction(InputActionKind.FocusAgent), reloaded.ActionFor(4));
            Assert.Equal(new InputAction(InputActionKind.FocusAgent), reloaded.ActionFor(5));
            Assert.Equal(new InputAction(InputActionKind.ZoomPane), reloaded.ActionFor(6));
            Assert.Equal(new InputAction(InputActionKind.EffortUp), reloaded.ActionFor(DevicePad.DialClockwiseId));
        });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DevicePad.KeyCount)]
    public void SetAgentKeysRefusesAnInvalidKeycapWithoutWriting(int invalid)
    {
        WithStore(store =>
        {
            store.Update(json => json["future"] = new JsonObject { ["keep"] = true });
            var before = File.ReadAllText(store.Path);

            var error = Assert.Throws<AgentLayoutEditException>(() =>
                KeyBindings.SetAgentKeys(new[] { 0, invalid }, store: store));

            Assert.False(error.ConfigurationChanged);
            Assert.Equal(before, File.ReadAllText(store.Path));
            Assert.Null(store.Read()["agentKeys"]);
        });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DevicePad.KeyCount)]
    public void SetAgentKeysRefusesAnInvalidKeycapWithoutCreatingAConfig(int invalid)
    {
        WithStore(store =>
        {
            Assert.Throws<AgentLayoutEditException>(() => KeyBindings.SetAgentKeys(new[] { invalid }, store: store));
            Assert.False(File.Exists(store.Path));
        });
    }

    [Fact]
    public void SetAgentKeysRefusesAStaleLayoutReview()
    {
        WithStore(store =>
        {
            KeyBindings.SetAgentKeys(new[] { 1, 0 }, store: store);
            var reviewed = KeyBindings.Load(store);

            // Another window saved a different layout after this review.
            KeyBindings.SetAgentKeys(new[] { 1, 0, 2 }, store: store);

            var error = Assert.Throws<AgentLayoutEditException>(() =>
                KeyBindings.SetAgentKeys(new[] { 1, 0, 3 }, reviewed, store));

            Assert.True(error.ConfigurationChanged);
            Assert.Equal(new[] { 1, 0, 2 }, KeyBindings.Load(store).AgentKeyIds);
        });
    }

    [Fact]
    public void SetAgentKeysRefusesWhenAKeyActionChangedSinceReview()
    {
        WithStore(store =>
        {
            KeyBindings.SetAgentKeys(new[] { 1, 0 }, store: store);
            var reviewed = KeyBindings.Load(store);

            KeyBindings.SetAction(2, new InputAction(InputActionKind.SubmitPrompt, "changed elsewhere"), store);

            var error = Assert.Throws<AgentLayoutEditException>(() =>
                KeyBindings.SetAgentKeys(new[] { 1, 0, 3 }, reviewed, store));

            Assert.True(error.ConfigurationChanged);
            var unchanged = KeyBindings.Load(store);
            Assert.Equal(new[] { 1, 0 }, unchanged.AgentKeyIds);
            Assert.Equal("changed elsewhere", unchanged.ActionFor(2).Value);
        });
    }

    [Fact]
    public void SetAgentKeysAcceptsAnUnchangedReview()
    {
        WithStore(store =>
        {
            KeyBindings.SetAgentKeys(new[] { 1, 0 }, store: store);
            var reviewed = KeyBindings.Load(store);

            KeyBindings.SetAgentKeys(new[] { 1, 0, 6 }, reviewed, store);

            Assert.Equal(new[] { 1, 0, 6 }, KeyBindings.Load(store).AgentKeyIds);
        });
    }

    /// Lighting and unrelated sections are not part of the layout review, so
    /// they must not block an otherwise unchanged save.
    [Fact]
    public void SetAgentKeysIgnoresLightingAndUnrelatedChangesSinceReview()
    {
        WithStore(store =>
        {
            KeyBindings.SetAgentKeys(new[] { 1, 0 }, store: store);
            var reviewed = KeyBindings.Load(store);

            store.Update(json =>
            {
                json["lighting"] = new JsonObject { ["brightness"] = 0.2 };
                json["future"] = new JsonObject { ["kept"] = true };
                json["claude"] = new JsonObject { ["models"] = new JsonArray("fable") };
            });

            KeyBindings.SetAgentKeys(new[] { 1, 0, 4 }, reviewed, store);

            Assert.Equal(new[] { 1, 0, 4 }, KeyBindings.Load(store).AgentKeyIds);
        });
    }

    [Fact]
    public void FactoryResetRestoresTheStockLayout()
    {
        WithStore(store =>
        {
            KeyBindings.SetAgentKeys(new[] { 12, 11, 10 }, store: store);
            KeyBindings.SetAction(4, new InputAction(InputActionKind.SubmitPrompt, "not an agent"), store);
            KeyBindings.SetAction(5, new InputAction(InputActionKind.None), store);

            KeyBindings.SetAgentKeys(DevicePad.AgentKeyIds, store: store);

            var reset = KeyBindings.Load(store);
            Assert.Equal(DevicePad.AgentKeyIds, reset.AgentKeyIds);
            foreach (var key in DevicePad.AgentKeyIds)
                Assert.Equal(new InputAction(InputActionKind.FocusAgent), reset.ActionFor(key));

            // Keys outside the stock layout keep their own bindings.
            Assert.Equal(new InputAction(InputActionKind.InsertText, "Open a pull request for this branch"),
                reset.ActionFor(9));
        });
    }

    private static void WithStore(Action<StudioConfigStore> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "agent-layout-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            test(new StudioConfigStore(Path.Combine(directory, "config.json")));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
