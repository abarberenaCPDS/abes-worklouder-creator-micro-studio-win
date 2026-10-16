namespace MicroManager.Core.Config;

using MicroManager.Core.Pad;

/// The dial and joystick are blind controls: the pad has no screen, so the
/// numbers that decide what a detent or a deflection means live here, apart
/// from the code that talks to Herdr. Kept in step with
/// `Sources/WLMicroManager/TuneController.swift`.
public static class TuneLadder
{
    /// Which list an agent's dial and joystick drive.
    public enum Kind
    {
        Claude,
        Codex,
        Unsupported,
    }

    public static Kind KindFor(string agent) =>
        agent.Contains("claude", StringComparison.OrdinalIgnoreCase) ? Kind.Claude
        : agent.Contains("codex", StringComparison.OrdinalIgnoreCase) ? Kind.Codex
        : Kind.Unsupported;

    /// Clamped, not wrapping: turning past the top pins at max rather than
    /// jumping to low — a dial has ends even when the hardware spins freely.
    public static int Climb(int index, int step, int count) =>
        count <= 0 ? 0 : Math.Max(0, Math.Min(count - 1, index + step));

    /// Models cycle with wraparound — unlike effort, a list of names has no
    /// natural top or bottom.
    public static int Cycle(int index, int step, int count) =>
        count <= 0 ? 0 : ((index + step) % count + count) % count;

    /// The vertical deflections move one place through a list; the horizontal
    /// ones are confirm and cancel, so they carry no step of their own.
    public static int? StepFor(Pad.JoystickDirection direction) => direction switch
    {
        Pad.JoystickDirection.North => -1,
        Pad.JoystickDirection.South => 1,
        _ => null,
    };
}
