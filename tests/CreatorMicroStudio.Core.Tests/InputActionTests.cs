using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using DevicePad = AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad.Pad;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

public sealed class InputActionTests
{
    [Theory]
    [InlineData("focus_agent", InputActionKind.FocusAgent)]
    [InlineData("cycle-tabs", InputActionKind.NextTab)]
    [InlineData("zoom-pane", InputActionKind.ZoomPane)]
    [InlineData("shell-command", InputActionKind.ShellCommand)]
    [InlineData("media", InputActionKind.MediaKey)]
    public void ParsesActionNamesAndAliases(string name, InputActionKind expected)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(name));
        Assert.Equal(expected, InputAction.Parse(json.RootElement)?.Kind);
    }

    [Fact]
    public void ParameterizedActionsRoundTrip()
    {
        var actions = new[]
        {
            new InputAction(InputActionKind.FocusSlot, Slot: 4),
            new InputAction(InputActionKind.FocusPane, Direction: InputDirection.Left),
            new InputAction(InputActionKind.Scroll, Direction: InputDirection.Down, Amount: 12),
            new InputAction(InputActionKind.SendKeys, Value: "ctrl+c esc"),
            new InputAction(InputActionKind.HerdrCommand, Value: "tab rename {tab} {ask:Name}"),
            new InputAction(InputActionKind.SubmitPrompt, Value: "Run the tests"),
            new InputAction(InputActionKind.KeyboardShortcut, Value: "ctrl+shift+k"),
            new InputAction(InputActionKind.MediaKey, Media: MediaKey.VolumeDown),
            new InputAction(InputActionKind.OpenUrl, Value: "https://example.test"),
            new InputAction(InputActionKind.ShellCommand, Value: "Get-Location"),
        };

        foreach (var action in actions)
        {
            using var json = JsonDocument.Parse(action.ToJsonNode().ToJsonString());
            Assert.Equal(action, InputAction.Parse(json.RootElement));
        }
    }

    [Fact]
    public void ClampsParametersAndUsesLegacyMacroText()
    {
        using var slot = JsonDocument.Parse("{\"type\":\"focus-slot\",\"slot\":99}");
        using var scroll = JsonDocument.Parse("{\"type\":\"scroll\",\"amount\":0}");
        using var macro = JsonDocument.Parse("\"macro\"");

        Assert.Equal(6, InputAction.Parse(slot.RootElement)?.Slot);
        Assert.Equal(1, InputAction.Parse(scroll.RootElement)?.Amount);
        Assert.Equal(new InputAction(InputActionKind.InsertText, "old macro"),
            InputAction.Parse(macro.RootElement, "old macro"));
    }

    [Fact]
    public void DefaultsCoverTheWindowsPadWithoutClaimingDialPush()
    {
        Assert.Equal(InputActionKind.FocusAgent, InputAction.DefaultFor(0).Kind);
        Assert.Equal(InputActionKind.Scroll, InputAction.DefaultFor(DevicePad.DialClockwiseId).Kind);
        Assert.Equal(InputActionKind.ModelNext, InputAction.DefaultFor(DevicePad.JoySouthId).Kind);
        Assert.Equal(InputActionKind.None, InputAction.DefaultFor(DevicePad.DialPushId).Kind);
    }

    [Fact]
    public void EffectiveDefaultsRetainTuneConfigAndLegacyMacroText()
    {
        var defaults = new KeyBindings();
        Assert.Equal(InputActionKind.FocusAgent, defaults.ActionFor(0).Kind);
        Assert.Equal(InputActionKind.NextTab, defaults.ActionFor(7).Kind);
        Assert.Equal("Open a pull request for this branch", defaults.ActionFor(9).Value);
        Assert.Equal(InputActionKind.Voice, defaults.ActionFor(10).Kind);
        Assert.Equal(InputActionKind.Scroll, defaults.ActionFor(DevicePad.DialClockwiseId).Kind);

        var effortMode = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("{\"dial\":\"effort\"}"));
        Assert.Equal(InputActionKind.EffortUp, effortMode.ActionFor(DevicePad.DialClockwiseId).Kind);
    }

    [Fact]
    public void KeyBindingsReadActionsAndLegacyFieldsWithNumericPrecedence()
    {
        const string json = """
            {
              "keys": {"9": "legacy macro"},
              "actions": {
                "dial-right": "effort-up",
                "13": "effort-down",
                "9": "macro"
              },
              "dial": "effort",
              "claude": {"efforts": ["low", "high"]}
            }
            """;

        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(InputActionKind.EffortDown, bindings.ActionFor(DevicePad.DialClockwiseId).Kind);
        Assert.Equal(new InputAction(InputActionKind.InsertText, "legacy macro"), bindings.ActionFor(9));
        Assert.Equal(new[] { "low", "high" }, bindings.ClaudeEfforts);
    }
}
