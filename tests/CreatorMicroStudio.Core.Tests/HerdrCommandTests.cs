using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

public sealed class HerdrCommandTests
{
    [Fact]
    public async Task ExpandsPlaceholdersAndAskIntoSeparateArguments()
    {
        var command = HerdrCommand.Parse("herdr tab rename {tab} {ask:New tab name={tab_label}}");
        var context = new Dictionary<string, string?>
        {
            ["tab"] = "w1:t2",
            ["tab_label"] = "worker tab",
        };
        var arguments = await command.ResolveAsync(context, (prompt, value) =>
        {
            Assert.Equal("New tab name", prompt);
            Assert.Equal("worker tab", value);
            return Task.FromResult<string?>("a name; not a shell command");
        });

        Assert.Equal(new[] { "tab", "rename", "w1:t2", "a name; not a shell command" }, arguments);
    }

    [Fact]
    public async Task SingleQuotesKeepPlaceholderTextLiteralAndCancelStopsExecution()
    {
        var literal = HerdrCommand.Parse("notification show '{tab}'");
        var args = await literal.ResolveAsync(new Dictionary<string, string?>(), null);
        Assert.Equal(new[] { "notification", "show", "{tab}" }, args);

        var asks = HerdrCommand.Parse("notification show {ask:Message}");
        var cancelled = await asks.ResolveAsync(new Dictionary<string, string?>(), (_, _) => Task.FromResult<string?>(null));
        Assert.Null(cancelled);
    }

    [Theory]
    [InlineData("tab rename {unknown}")]
    [InlineData("tab rename {tab")]
    [InlineData("tab rename 'not closed")]
    public void RejectsInvalidPlaceholdersAndQuotes(string text) =>
        Assert.Throws<FormatException>(() => HerdrCommand.Parse(text));

    [Fact]
    public void RejectsLegacyTuiKeybindingsAndOpaqueIds()
    {
        Assert.Contains("keybinding", Assert.Throws<FormatException>(() =>
            HerdrCommand.Parse("prefix+shift+t")).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("internal command IDs", Assert.Throws<FormatException>(() =>
            HerdrCommand.Parse("cmd_8f384")).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandsTildePathsForWslHerdrCli()
    {
        Assert.Equal("/home/abe/.local/bin/herdr",
            HerdrCommandRunner.ExpandWslPath("~/.local/bin/herdr", "/home/abe"));
        Assert.Equal("/home/abe", HerdrCommandRunner.ExpandWslPath("~", "/home/abe"));
        Assert.Equal("/usr/bin/herdr", HerdrCommandRunner.ExpandWslPath("/usr/bin/herdr", "/home/abe"));
    }
}
