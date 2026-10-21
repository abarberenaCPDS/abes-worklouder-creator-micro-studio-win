namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Keymap;
using DevicePad = AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad.Pad;

public class KeymapManagerTests
{
    [Fact]
    public void ParsesTheDoubleEncodedEnvelope()
    {
        var result = KeymapManager.Parse("{\"data\":\"{\\\"profiles\\\":[{\\\"id\\\":0}]}\"}");
        Assert.True(result.ContainsKey("profiles"));
    }

    [Fact]
    public void StockKeymapIsNotAgentBound()
    {
        var config = StockKeymap();
        Assert.False(KeymapManager.IsAgentKeymapApplied(config));
    }

    [Fact]
    public void ApplyingBindsEveryKeyToItsOwnCode()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        Assert.True(KeymapManager.IsAgentKeymapApplied(config));
    }

    [Fact]
    public void AgentOnlyKeymapIsNotConsideredApplied()
    {
        // Only key grid bound, dial missing -> not applied
        var config = BoundKeysOnlyKeymap();
        Assert.False(KeymapManager.IsAgentKeymapApplied(config));
    }

    /// Only the dial's two rotations are ours. Its press — the encoder's third
    /// slot — is left exactly as it was found, which is the macOS app's
    /// behaviour: a stock pad ships it as the play/pause media key, and taking it
    /// would change what the click does to the host.
    [Fact]
    public void ApplyingLeavesTheDialPressAlone()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        var dial = layout["encoders"]!.AsArray()[0]!.AsArray();
        Assert.Equal(KeymapManager.DialCodes[0], dial[0]!.GetValue<string>());
        Assert.Equal(KeymapManager.DialCodes[1], dial[1]!.GetValue<string>());
        Assert.Equal("KC_MPLY", dial[2]!.GetValue<string>());
    }

    /// A two-slot encoder has no press at all and the host cannot invent one, so
    /// the keymap counts as applied rather than looping on a check it could never
    /// satisfy — the same reading the stick's sectors get.
    [Fact]
    public void ATwoSlotEncoderIsAccepted()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        layout["encoders"]!.AsArray()[0] = new JsonArray("KV_OAI_AG13", "KV_OAI_AG14");

        Assert.True(KeymapManager.IsAgentKeymapApplied(config));
    }

    /// An encoder whose press still carries the media key is bound as far as the
    /// port is concerned — the press is not ours to demand, so it is not a
    /// failure. The two rotations are what the dial's report comes from.
    [Fact]
    public void AStockPressIsNotAFailure()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        layout["encoders"]!.AsArray()[0] = new JsonArray("KV_OAI_AG13", "KV_OAI_AG14", "KC_MPLY");

        Assert.True(KeymapManager.IsAgentKeymapApplied(config));
    }

    /// An earlier build of this port bound the dial's press to `AG19` to use the
    /// click itself. Such a pad is not "already applied": the app puts the stock
    /// media key back, then leaves the slot alone for good.
    [Fact]
    public void APadStillCarryingThePortsOldPressIsRepaired()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        layout["encoders"]!.AsArray()[0] = new JsonArray(
            "KV_OAI_AG13", "KV_OAI_AG14", KeymapManager.FormerDialPressCode);

        // Not applied, so `ApplyAsync` writes — and what it writes is the stock key.
        Assert.False(KeymapManager.IsAgentKeymapApplied(config));
        KeymapManager.Apply(config);
        var dial = layout["encoders"]!.AsArray()[0]!.AsArray();
        Assert.Equal(KeymapManager.StockDialPressCode, dial[2]!.GetValue<string>());
        Assert.True(KeymapManager.IsAgentKeymapApplied(config));
    }

    [Fact]
    public void ApplyingBindsTheJoystickCardinals()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        var joystick = layout["joystick"]!.AsObject();
        var sectors = joystick["sectors"]!.AsArray();
        var codes = sectors.OfType<JsonObject>().Select(s => s["k"]!.GetValue<string>()).ToList();
        Assert.Contains("KV_OAI_AG15", codes);
        Assert.Contains("KV_OAI_AG16", codes);
        Assert.Contains("KV_OAI_AG17", codes);
        Assert.Contains("KV_OAI_AG18", codes);
    }

    [Fact]
    public void ApplyingIsIdempotent()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        KeymapManager.Apply(config);
    }

    [Fact]
    public void ActiveProfileIsSelectedByIdNotArrayPosition()
    {
        var config = StockKeymap();
        var profiles = config["profiles"]!.AsArray();
        var second = profiles[0]!.DeepClone().AsObject();
        second["id"] = 42;
        profiles.Add(second);
        config["activeProfileId"] = 42;

        KeymapManager.Apply(config);

        Assert.Equal("KC_F13", profiles[0]!["layers"]![0]!["layout"]!["keymap"]![0]![0]!.GetValue<string>());
        Assert.Equal("KV_OAI_AG00", profiles[1]!["layers"]![0]!["layout"]!["keymap"]![0]![0]!.GetValue<string>());
    }


    [Fact]
    public void EverythingElseSurvivesTheRewrite()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        var joystick = layout["joystick"]!.AsObject();
        var sectors = joystick["sectors"]!.AsArray();
        // Non-cardinal sectors keep their keycodes; only the four cardinal
        // centres (0.0, 0.25, 0.5, 0.75) get rebound to AG codes.
        var nonCardinalCodes = sectors.OfType<JsonObject>()
            .Select(s => s["k"]!.GetValue<string>())
            .Where(c => c != "KV_OAI_AG15" && c != "KV_OAI_AG16" &&
                        c != "KV_OAI_AG17" && c != "KV_OAI_AG18")
            .ToList();
        Assert.Contains("KC_P1", nonCardinalCodes);
        Assert.Contains("KC_P3", nonCardinalCodes);
        Assert.Contains("KC_P5", nonCardinalCodes);
        Assert.Contains("KC_P7", nonCardinalCodes);
    }

    [Fact]
    public void ATwoSectorStickIsAcceptedWhenItsCardinalsAreBound()
    {
        // Firmware 0.6.3 stores only the vertical cardinals (0.25/0.75) on the
        // radial stick. The host cannot invent the missing sectors, so once the
        // stick's own cardinals carry their AG codes the keymap counts as
        // applied rather than looping on a check it can never satisfy.
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        // The device's stick, exactly: two sectors, the second wrapping past
        // zero (a1 > a2), both already carrying their cardinal's AG code.
        layout["joystick"]!.AsObject()["sectors"] = new JsonArray(
            new JsonObject { ["k"] = "KV_OAI_AG15", ["a1"] = 0.1875, ["a2"] = 0.3125 },
            new JsonObject { ["k"] = "KV_OAI_AG17", ["a1"] = 0.3125, ["a2"] = 0.1875 });

        Assert.Equal(2, layout["joystick"]!.AsObject()["sectors"]!.AsArray().Count);
        Assert.True(KeymapManager.IsAgentKeymapApplied(config));
    }

    [Fact]
    public void AvailableInputIdsRespectTheActiveProfilesExposedJoystickSectors()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        layout["joystick"]!.AsObject()["sectors"] = new JsonArray(
            new JsonObject { ["k"] = "KV_OAI_AG15", ["a1"] = 0.1875, ["a2"] = 0.3125 },
            new JsonObject { ["k"] = "KV_OAI_AG17", ["a1"] = 0.3125, ["a2"] = 0.1875 });

        var available = KeymapManager.AvailableInputIds(config);
        Assert.Contains(DevicePad.JoyNorthId, available);
        Assert.Contains(DevicePad.JoySouthId, available);
        Assert.DoesNotContain(DevicePad.JoyWestId, available);
        Assert.DoesNotContain(DevicePad.JoyEastId, available);
        Assert.DoesNotContain(DevicePad.DialPushId, available);
    }

    [Fact]
    public void AnExposedCardinalWithTheWrongCodeIsStillRejected()
    {
        var config = StockKeymap();
        KeymapManager.Apply(config);
        var layout = KeymapManager.ActiveLayerLayout(config)!;
        // The 0.1875–0.3125 sector sits at 0.25; give it back a plain keycode.
        layout["joystick"]!.AsObject()["sectors"]!.AsArray()[0]!["k"] = "KC_P4";

        Assert.False(KeymapManager.IsAgentKeymapApplied(config));
    }

    [Fact]
    public void MalformedInputIsRejectedRatherThanSilentlyAccepted()
    {
        Assert.Throws<FailureException>(() =>
            KeymapManager.Parse(null!));
    }

    // MARK: helpers

    private static JsonObject StockKeymap() =>
        JsonNode.Parse("""
        {
          "version": 1,
          "activeProfileId": 0,
          "profiles": [{
            "id": 0,
            "name": "Default",
            "layers": [{
              "id": 0,
              "name": "Base",
              "layout": {
                "keymap": [
                  ["KC_F13", "KC_F14"],
                  ["KC_F15", "KC_F16", "KC_F17", "KC_F18"],
                  ["KC_F19", "KC_F20", "KC_F21", "KC_F22"],
                  ["KC_F23", "KC_NONE", "KC_F24"]
                ],
                "encoders": [["KC_VOLU", "KC_VOLD", "KC_MPLY"]],
                "joystick": {
                  "type": "RADIAL",
                  "sectors": [
                    {"k": "KI_X", "a1": 0.1875, "a2": 0.3125},
                    {"k": "KC_P1", "a1": 0.3125, "a2": 0.4375},
                    {"k": "KC_P2", "a1": 0.4375, "a2": 0.5625},
                    {"k": "KC_P3", "a1": 0.5625, "a2": 0.6875},
                    {"k": "KC_P4", "a1": 0.6875, "a2": 0.8125},
                    {"k": "KC_P5", "a1": 0.8125, "a2": 0.9375},
                    {"k": "KC_P6", "a1": 0.9375, "a2": 0.0625},
                    {"k": "KC_P7", "a1": 0.0625, "a2": 0.1875}
                  ]
                }
              }
            }]
          }]
        }
        """)!.AsObject();

    private static JsonObject BoundKeysOnlyKeymap()
    {
        var stock = StockKeymap();
        var layout = GetLayout(stock);
        // Remove encoders and joystick sections -> only key grid bound
        layout!.Remove("encoders");
        layout!.Remove("joystick");
        return stock;
    }

    private static JsonObject? GetLayout(JsonObject config)
    {
        var profiles = config["profiles"]?.AsArray();
        if (profiles is null) return null;
        var profile = profiles[0]!.AsObject();
        var layers = profile["layers"]?.AsArray();
        if (layers is null) return null;
        var layer = layers[0]!.AsObject();
        return layer["layout"]?.AsObject();
    }
}
