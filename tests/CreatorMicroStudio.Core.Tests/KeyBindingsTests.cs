namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

public class KeyBindingsTests
{
    [Fact]
    public void MissingFileMeansDefaults()
    {
        var bindings = KeyBindings.Load();
        Assert.Equal("Open a pull request for this branch", bindings.TextFor(9));
        Assert.Equal("Run the test suite", bindings.TextFor(12));
        Assert.Null(bindings.TextFor(10));
    }

    [Fact]
    public void DefaultsSitOnTheMacroKeys()
    {
        var bindings = new KeyBindings();
        Assert.Equal(2, bindings.Texts.Count);
    }

    [Fact]
    public void FileOverridesAKeyAndKeepsTheOtherDefault()
    {
        var json = """
        {
          "keys": {
            "9": "custom nine",
            "12": "Run the test suite"
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal("custom nine", bindings.TextFor(9));
        Assert.Equal("Run the test suite", bindings.TextFor(12));
    }

    [Fact]
    public void WideKeyBindsBothHalvesTogether()
    {
        var json = """
        {
          "keys": {
            "10+11": "Summarise what you are working on"
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal("Summarise what you are working on", bindings.TextFor(10));
        Assert.Equal("Summarise what you are working on", bindings.TextFor(11));
    }

    [Fact]
    public void WideKeyHalvesCanDiffer()
    {
        var json = """
        {
          "keys": {
            "10": "left half",
            "11": "right half"
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal("left half", bindings.TextFor(10));
        Assert.Equal("right half", bindings.TextFor(11));
    }

    [Fact]
    public void EmptyStringUnbindsADefault()
    {
        var json = """
        {
          "keys": {
            "9": ""
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Null(bindings.TextFor(9));
        Assert.Equal("Run the test suite", bindings.TextFor(12));
    }

    [Fact]
    public void MalformedFileFallsBackToDefaults()
    {
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("not json {"));
        Assert.Equal("Open a pull request for this branch", bindings.TextFor(9));
    }

    [Fact]
    public void ClaudeModelAndEffortListsHaveDefaults()
    {
        var bindings = new KeyBindings();
        Assert.Equal(new[] { "fable", "opus", "sonnet", "haiku" }, bindings.ClaudeModels);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, bindings.ClaudeEfforts);
    }

    [Fact]
    public void ClaudeListsAreOverridable()
    {
        var json = """
        {
          "claude": {
            "models": ["fable", "opus"],
            "efforts": ["low", "high"]
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(new[] { "fable", "opus" }, bindings.ClaudeModels);
        Assert.Equal(new[] { "low", "high" }, bindings.ClaudeEfforts);
    }

    [Fact]
    public void EmptyClaudeListsFallBackToDefaults()
    {
        var json = """
        {
          "claude": {
            "models": [],
            "efforts": []
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(new[] { "fable", "opus", "sonnet", "haiku" }, bindings.ClaudeModels);
    }

    [Fact]
    public void TheDialScrollsByDefault()
    {
        Assert.Equal(TuneLadder.DialMode.Scroll, new KeyBindings().Dial);
        Assert.Equal(TuneLadder.DialMode.Scroll,
            KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("{}")).Dial);
    }

    [Fact]
    public void TheDialCanBePutBackOnEffort()
    {
        var json = """{ "dial": "effort" }""";
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(TuneLadder.DialMode.Effort, bindings.Dial);
    }

    /// Case and spacing are the file author's business, not ours.
    [Theory]
    [InlineData("scroll", TuneLadder.DialMode.Scroll)]
    [InlineData("Effort", TuneLadder.DialMode.Effort)]
    [InlineData("  effort  ", TuneLadder.DialMode.Effort)]
    [InlineData("wiggle", null)]
    [InlineData("", null)]
    public void DialModeWordsAreRead(string word, TuneLadder.DialMode? expected) =>
        Assert.Equal(expected, KeyBindings.DialModeNamed(word));

    /// A word we do not know is not worth a dead dial, so it keeps the default.
    [Fact]
    public void AnUnknownDialModeKeepsTheDefault()
    {
        var json = """{ "dial": "wiggle" }""";
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(TuneLadder.DialMode.Scroll, bindings.Dial);
    }

    [Fact]
    public void AMalformedFileStillHasAWorkingDial() =>
        Assert.Equal(TuneLadder.DialMode.Scroll,
            KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("not json {")).Dial);

    [Fact]
    public void TheDialScrollsFiveLinesAndSendsNothingByDefault()
    {
        var scroll = new KeyBindings().Scroll;
        Assert.Equal(5, scroll.Lines);
        // There is no fallback key to send: Herdr rejects `pageup` and every
        // spelling of it, and cannot synthesise a wheel event.
        Assert.Equal("", scroll.Up);
        Assert.Equal("", scroll.Down);
    }

    [Fact]
    public void ScrollSettingsComeFromTheFile()
    {
        var json = """{ "scroll": { "lines": 5, "up": "ctrl+u", "down": "ctrl+d" } }""";
        var scroll = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json)).Scroll;
        Assert.Equal(5, scroll.Lines);
        Assert.Equal("ctrl+u", scroll.Up);
        Assert.Equal("ctrl+d", scroll.Down);
    }

    /// Only what the file names changes, and a setting that makes no sense is
    /// not worth a dial that cannot move: a zero or negative step, and a blank
    /// key name, each keep their default.
    [Fact]
    public void ScrollSettingsFillInFromTheDefaults()
    {
        var json = """{ "scroll": { "lines": 0, "up": "  f13  " } }""";
        var scroll = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json)).Scroll;
        Assert.Equal(5, scroll.Lines);
        Assert.Equal("f13", scroll.Up);
        Assert.Equal("", scroll.Down);
    }
}
