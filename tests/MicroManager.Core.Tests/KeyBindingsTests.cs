namespace MicroManager.Core.Tests;

using MicroManager.Core.Config;

public class KeyBindingsTests
{
    [Fact]
    public void MissingFileMeansDefaults()
    {
        var bindings = KeyBindings.Load();
        Assert.Equal("Open PRs for all active GitButler branches", bindings.TextFor(9));
        Assert.Equal("Run but pull", bindings.TextFor(12));
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
            "12": "Run but pull"
          }
        }
        """;
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal("custom nine", bindings.TextFor(9));
        Assert.Equal("Run but pull", bindings.TextFor(12));
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
        Assert.Equal("Run but pull", bindings.TextFor(12));
    }

    [Fact]
    public void MalformedFileFallsBackToDefaults()
    {
        var bindings = KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes("not json {"));
        Assert.Equal("Open PRs for all active GitButler branches", bindings.TextFor(9));
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
}
