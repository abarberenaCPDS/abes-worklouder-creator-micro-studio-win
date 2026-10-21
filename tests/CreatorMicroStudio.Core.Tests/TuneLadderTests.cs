namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

/// The dial climbs a ladder and the joystick cycles a list, and the two ends
/// of a dial and the two ends of a list do not behave the same way.
public class TuneLadderTests
{
    [Fact]
    public void ClimbPinsAtTheTopInsteadOfWrapping()
    {
        Assert.Equal(4, TuneLadder.Climb(4, 1, 5));
        Assert.Equal(4, TuneLadder.Climb(4, 9, 5));
    }

    [Fact]
    public void ClimbPinsAtTheBottom()
    {
        Assert.Equal(0, TuneLadder.Climb(0, -1, 5));
        Assert.Equal(0, TuneLadder.Climb(1, -9, 5));
    }

    [Fact]
    public void ClimbStepsFromTheMiddle()
    {
        Assert.Equal(3, TuneLadder.Climb(2, 1, 5));
        Assert.Equal(1, TuneLadder.Climb(2, -1, 5));
    }

    [Theory]
    [InlineData(0, 1, 4, 1)]
    [InlineData(1, 1, 4, 2)]
    [InlineData(3, 1, 4, 0)]
    [InlineData(0, -1, 4, 3)]
    [InlineData(1, -2, 4, 3)]
    public void ModelsCycleWithWraparound(int index, int step, int count, int expected) =>
        Assert.Equal(expected, TuneLadder.Cycle(index, step, count));

    [Fact]
    public void ALoneEntryListStaysPut()
    {
        Assert.Equal(0, TuneLadder.Climb(0, 1, 1));
        Assert.Equal(0, TuneLadder.Climb(0, -1, 1));
        Assert.Equal(0, TuneLadder.Cycle(0, 1, 1));
    }

    /// An empty list cannot index, and the config file is allowed to be wrong.
    [Fact]
    public void AnEmptyListDoesNotThrow()
    {
        Assert.Equal(0, TuneLadder.Climb(3, 1, 0));
        Assert.Equal(0, TuneLadder.Cycle(3, 1, 0));
    }

    [Theory]
    [InlineData("claude", TuneLadder.Kind.Claude)]
    [InlineData("Claude Code", TuneLadder.Kind.Claude)]
    [InlineData("codex", TuneLadder.Kind.Codex)]
    [InlineData("Codex CLI", TuneLadder.Kind.Codex)]
    [InlineData("amp", TuneLadder.Kind.Unsupported)]
    public void KindFollowsTheAgentName(string agent, TuneLadder.Kind expected) =>
        Assert.Equal(expected, TuneLadder.KindFor(agent));

    [Fact]
    public void OnlyTheVerticalDeflectionsCarryAStep()
    {
        Assert.Equal(-1, TuneLadder.StepFor(Pad.JoystickDirection.North));
        Assert.Equal(1, TuneLadder.StepFor(Pad.JoystickDirection.South));
        Assert.Null(TuneLadder.StepFor(Pad.JoystickDirection.East));
        Assert.Null(TuneLadder.StepFor(Pad.JoystickDirection.West));
    }

    /// Turning the dial right moves the viewport down, the way rolling a wheel
    /// towards you does, so that is the detent the down key has to land on.
    [Fact]
    public void ScrollKeysFollowTheDetent()
    {
        Assert.Equal("f14", TuneLadder.ScrollKey(1, "f13", "f14"));
        Assert.Equal("f13", TuneLadder.ScrollKey(-1, "f13", "f14"));
    }

    /// No key is configured by default, and the dial has to be able to tell
    /// "nothing to send" from "something to send" — there is no key the pad can
    /// send that a full-screen agent already understands.
    [Fact]
    public void NoConfiguredKeyMeansNothingIsSent()
    {
        Assert.Null(TuneLadder.ScrollKey(1, "", ""));
        Assert.Null(TuneLadder.ScrollKey(-1, "   ", ""));
    }

    /// Right scrolls down towards the newest output, which is a smaller offset
    /// from the bottom; left climbs back through the older output.
    [Fact]
    public void ScrollStepsByWholeLines()
    {
        Assert.Equal(3, TuneLadder.ScrollTo(6, 240, 1, 3));
        Assert.Equal(6, TuneLadder.ScrollTo(3, 240, -1, 3));
        Assert.Equal(0, TuneLadder.ScrollTo(1, 240, 1, 3));
    }

    /// A dial has ends: turning past either end of the scrollback pins rather
    /// than wrapping round to the other.
    [Fact]
    public void ScrollPinsAtBothEnds()
    {
        Assert.Equal(0, TuneLadder.ScrollTo(2, 240, 1, 3));
        Assert.Equal(240, TuneLadder.ScrollTo(239, 240, -1, 3));
        Assert.Equal(240, TuneLadder.ScrollTo(240, 240, -1, 3));
    }

    /// A pane with no scrollback reports a maximum of zero — an alternate-screen
    /// app — and there is no moving it: a detent must not go negative.
    [Fact]
    public void NoScrollbackStaysAtZero()
    {
        Assert.Equal(0, TuneLadder.ScrollTo(0, 0, 1, 3));
        Assert.Equal(0, TuneLadder.ScrollTo(0, 0, -1, 3));
    }
}
