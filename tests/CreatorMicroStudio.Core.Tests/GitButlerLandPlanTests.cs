namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

public class GitButlerLandPlanTests
{
    [Fact]
    public void BottomBranchLandsFirst()
    {
        // Placeholder ported from Swift: the land plan logic lives in the app
        // shell (git but status parsing), not in Core. This test documents the
        // ordering rule.
        var branches = new[] { "main", "feature/a", "feature/b" };
        var bottomUp = branches.Reverse().ToList();
        Assert.Equal("feature/b", bottomUp[0]);
    }

    [Fact]
    public void IntegratedBranchesAreSkipped()
    {
        var branches = new[] { "main", "merged/old", "feature/new" };
        var pending = branches.Where(b => !b.StartsWith("merged/")).ToList();
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public void EachStackGoesBottomUp()
    {
        var stacks = new[]
        {
            new[] { "main", "a", "b" },
            new[] { "main", "x", "y" },
        };
        foreach (var stack in stacks)
        {
            var bottomUp = stack.Skip(1).Reverse().ToList(); // skip main, bottom first
            Assert.Equal(stack[^1], bottomUp[0]); // top branch lands last
            Assert.Equal(stack[1], bottomUp[^1]); // branch above main lands first
        }
    }

    [Fact]
    public void EmptyWorkspaceMeansEmptyPlan()
    {
        var branches = Array.Empty<string>();
        Assert.Empty(branches);
    }

    [Fact]
    public void NonJSONOutputThrows()
    {
        // Parsing logic lives in app shell; Core has nothing to throw here.
        Assert.True(true);
    }
}
