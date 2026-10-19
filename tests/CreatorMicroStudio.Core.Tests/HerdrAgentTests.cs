namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

public class HerdrAgentTests
{
    /// The case that prompted this: three panes, one checkout. The directory
    /// name is identical for all of them, so only the pane's own title tells
    /// them apart.
    [Fact]
    public void AgentsSharingACheckoutAreToldApartByTheirTitles()
    {
        var agents = new List<HerdrAgent>
        {
            InDirectory("/home/abe/wsl-src/arcOS.Web", "384209"),
            InDirectory("/home/abe/wsl-src/arcOS.Web", "arcOS.Web | trunk"),
            InDirectory("/home/abe/wsl-src/arcOS.Web", "bug-prefill"),
        };

        Assert.Equal(
            new[] { "384209", "arcOS.Web | trunk", "bug-prefill" },
            agents.Select(a => a.ShortName));
    }

    [Fact]
    public void WithoutATitleTheDirectoryNamesTheAgent()
    {
        Assert.Equal("arcOS.Web", InDirectory("/home/abe/wsl-src/arcOS.Web").ShortName);
    }

    [Fact]
    public void ABlankTitleIsNotAName()
    {
        Assert.Equal("arcOS.Web", InDirectory("/home/abe/wsl-src/arcOS.Web", "   ").ShortName);
    }

    /// Herdr pads its title with a leading activity glyph, so the title that
    /// reaches the status line should not carry stray whitespace either.
    [Fact]
    public void TheTitleIsTrimmed()
    {
        Assert.Equal("bug-prefill", InDirectory("/tmp/x", " bug-prefill ").ShortName);
    }

    /// Older Herdr builds report no directory and no title. The agent kind is a
    /// poor name, but it beats an empty one.
    [Fact]
    public void WithNothingElseTheAgentKindIsTheName()
    {
        var agent = new HerdrAgent(null, null, null, null, "claude", "idle", null, null, false);

        Assert.Equal("claude", agent.ShortName);
        Assert.Equal("claude", agent.DirectoryName);
    }

    /// Guards the snake_case policy: the titles are new fields, and a name that
    /// does not round-trip would leave every agent on its directory name.
    [Fact]
    public void TheTitlesComeOffTheWire()
    {
        const string json = """
        {"type":"agent_list","agents":[{"terminal_id":"term_65de3bdf018313","agent":"claude","terminal_title":"✳ bug-prefill","terminal_title_stripped":"bug-prefill","agent_status":"idle","pane_id":"w1:p7","focused":false,"cwd":"/home/abe/wsl-src/arcOS.Web","foreground_cwd":"/home/abe/wsl-src/arcOS.Web"}]}
        """;

        var result = JsonSerializer.Deserialize<AgentListResult>(json, HerdrJson.Snake)!;

        var agent = Assert.Single(result.Agents);
        Assert.Equal("✳ bug-prefill", agent.TerminalTitle);
        Assert.Equal("bug-prefill", agent.ShortName);
        Assert.Equal("arcOS.Web", agent.DirectoryName);
        Assert.Equal("w1:p7", agent.FocusTarget);
    }

    private static HerdrAgent InDirectory(string foregroundCwd, string? title = null) =>
        new(null, null, null, null, "claude", "idle", null, foregroundCwd, false,
            TerminalTitleStripped: title);
}
