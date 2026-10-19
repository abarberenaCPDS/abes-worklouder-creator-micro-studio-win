namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

public class PadGeometryTests
{
    [Fact]
    public void RowsMatchFirmwareMatrix()
    {
        var flat = Pad.Rows.SelectMany(r => r).ToList();
        Assert.Equal(Pad.KeyCount, flat.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, flat);
    }

    [Fact]
    public void ReadingOrderStartsTopRight()
    {
        // firmware top row is [0, 1] but key 0 is top-RIGHT, so reading order is 1, 0
        Assert.Equal(new[] { 1, 0, 2, 3, 4, 5 }, Pad.AgentKeyIds);
    }

    [Fact]
    public void AgentSlotForWorks()
    {
        Assert.Equal(0, Pad.AgentSlotFor(1));
        Assert.Equal(1, Pad.AgentSlotFor(0));
        Assert.Equal(2, Pad.AgentSlotFor(2));
        Assert.Null(Pad.AgentSlotFor(6));
    }

    [Fact]
    public void PositionOfMatchesFirmwareOrder()
    {
        Assert.Equal((0, 0), Pad.PositionOf(0));
        Assert.Equal((0, 1), Pad.PositionOf(1));
        Assert.Equal((1, 0), Pad.PositionOf(2));
    }

    [Fact]
    public void BoundKeyIdsCoverTheWholePad()
    {
        // agent keys + stack, tab-cycle, land, voice (2 halves), macro (2)
        Assert.Equal(13, Pad.BoundKeyIds.Count);
        Assert.Contains(Pad.StackKeyId, Pad.BoundKeyIds);
        Assert.Contains(Pad.TabCycleKeyId, Pad.BoundKeyIds);
        Assert.Contains(Pad.LandKeyId, Pad.BoundKeyIds);
        Assert.Contains(10, Pad.BoundKeyIds);
        Assert.Contains(11, Pad.BoundKeyIds);
        Assert.Contains(9, Pad.BoundKeyIds);
        Assert.Contains(12, Pad.BoundKeyIds);
    }

    [Fact]
    public void InputCatalogCoversKeysAndControlsWithoutClaimingFirmwareAvailability()
    {
        Assert.Equal(20, Pad.Inputs.Count);
        Assert.Equal("Key 0", Pad.Input(0)?.Name);
        Assert.Equal("Dial push", Pad.Input(Pad.DialPushId)?.Name);
        Assert.True(Pad.Input(Pad.DialPushId)?.CapabilityDependent);
        Assert.False(Pad.Input(0)?.CapabilityDependent);
        Assert.Equal(Enumerable.Range(0, 20), Pad.ConfigurableInputIds);
    }

    [Fact]
    public void PhysicalRowsPlaceControlsAndCaseMark()
    {
        Assert.Equal(4, Pad.PhysicalDisplayRows.Count);

        // The dial and joystick flank the reversed top row: key 1 is left of key 0.
        Assert.Equal(
            new[] { PadDisplayCellKind.Dial, PadDisplayCellKind.Key, PadDisplayCellKind.Key, PadDisplayCellKind.Joystick },
            Pad.PhysicalDisplayRows[0].Select(cell => cell.Kind));
        Assert.Equal(new[] { 1, 0 }, Pad.PhysicalDisplayRows[0].Where(cell => cell.IsKey).Select(cell => cell.InputId));

        // The bottom-left slot is the non-key case mark, not a key.
        Assert.Equal(PadDisplayCellKind.CaseMark, Pad.PhysicalDisplayRows[3][0].Kind);

        Assert.Equal(13, Pad.PhysicalDisplayKeyIds.Count);
        Assert.Equal(Enumerable.Range(0, 13), Pad.PhysicalDisplayKeyIds.OrderBy(id => id));
    }
}
