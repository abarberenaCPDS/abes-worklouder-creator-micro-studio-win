namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

/// <summary>One slot on the pad's physical surface, for on-screen mirrors.</summary>
public enum PadDisplayCellKind
{
    Key,
    Dial,
    Joystick,
    CaseMark,
}

/// <summary>
/// A slot in the maker-accurate physical layout. <see cref="InputId"/> is the
/// AG thread id for keys and -1 for the dial, joystick and non-key case mark;
/// those are addressed by their own <see cref="Pad"/> constants when hit.
/// </summary>
public readonly record struct PadDisplayCell(PadDisplayCellKind Kind, int InputId = -1)
{
    public static PadDisplayCell Key(int inputId) => new(PadDisplayCellKind.Key, inputId);
    public static PadDisplayCell Dial => new(PadDisplayCellKind.Dial);
    public static PadDisplayCell Joystick => new(PadDisplayCellKind.Joystick);
    public static PadDisplayCell CaseMark => new(PadDisplayCellKind.CaseMark);
    public bool IsKey => Kind == PadDisplayCellKind.Key;
}

public static partial class Pad
{
    /// <summary>
    /// The pad as you look at it, including the dial (row 0, left of the two
    /// top keys), the joystick (row 0, right), and the non-key circular case
    /// mark in the bottom-left slot. Mirrors `Pad.physicalDisplayRows` in the
    /// Swift reference, including the reversed top row (key 1 before key 0).
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<PadDisplayCell>> PhysicalDisplayRows = new[]
    {
        new[] { PadDisplayCell.Dial, PadDisplayCell.Key(1), PadDisplayCell.Key(0), PadDisplayCell.Joystick },
        new[] { PadDisplayCell.Key(2), PadDisplayCell.Key(3), PadDisplayCell.Key(4), PadDisplayCell.Key(5) },
        new[] { PadDisplayCell.Key(6), PadDisplayCell.Key(7), PadDisplayCell.Key(8), PadDisplayCell.Key(9) },
        new[] { PadDisplayCell.CaseMark, PadDisplayCell.Key(10), PadDisplayCell.Key(11), PadDisplayCell.Key(12) },
    };

    /// <summary>Every key id named by <see cref="PhysicalDisplayRows"/>, in reading order.</summary>
    public static IReadOnlyList<int> PhysicalDisplayKeyIds { get; } =
        PhysicalDisplayRows.SelectMany(row => row)
            .Where(cell => cell.IsKey)
            .Select(cell => cell.InputId)
            .ToArray();
}
