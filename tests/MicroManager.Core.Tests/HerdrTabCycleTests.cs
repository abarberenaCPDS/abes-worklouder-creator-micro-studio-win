namespace MicroManager.Core.Tests;

using System.Text.Json;
using MicroManager.Core.Herdr;

public class HerdrTabCycleTests
{
    [Fact]
    public void AdvancesToTheNextTabByNumber()
    {
        var tabs = new List<HerdrTab>
        {
            new("t1", "ws", 1, true),
            new("t2", "ws", 2, false),
            new("t3", "ws", 3, false),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Equal("t2", next?.TabId);
    }

    [Fact]
    public void WrapsFromTheLastTabToTheFirst()
    {
        var tabs = new List<HerdrTab>
        {
            new("t1", "ws", 1, false),
            new("t2", "ws", 2, true),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Equal("t1", next?.TabId);
    }

    [Fact]
    public void CyclesInNumberOrderNotListOrder()
    {
        var tabs = new List<HerdrTab>
        {
            new("t3", "ws", 3, true),
            new("t1", "ws", 1, false),
            new("t2", "ws", 2, false),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Equal("t1", next?.TabId);
    }

    [Fact]
    public void StaysInsideTheFocusedWorkspace()
    {
        var tabs = new List<HerdrTab>
        {
            new("t1", "ws1", 1, true),
            new("t2", "ws2", 2, false),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Null(next);
    }

    [Fact]
    public void SingleTabHasNowhereToGo()
    {
        var tabs = new List<HerdrTab>
        {
            new("t1", "ws", 1, true),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Null(next);
    }

    [Fact]
    public void NoFocusedTabDoesNothing()
    {
        var tabs = new List<HerdrTab>
        {
            new("t1", "ws", 1, false),
        };
        var next = HerdrClient.NextTab(tabs);
        Assert.Null(next);
    }
}
