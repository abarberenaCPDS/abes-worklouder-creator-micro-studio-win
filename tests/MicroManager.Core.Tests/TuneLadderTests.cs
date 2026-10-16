namespace MicroManager.Core.Tests;

using MicroManager.Core.Config;
using MicroManager.Core.Pad;

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
}
