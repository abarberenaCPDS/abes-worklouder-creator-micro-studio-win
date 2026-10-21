namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

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

    /// What a turn of the dial does. The dial is blind — the pad cannot show
    /// which mode it is in — so this is a config choice rather than a gesture.
    public enum DialMode
    {
        /// Scroll the focused pane: turned right it moves down towards the newest
        /// output, turned left up through the older output. Five lines a detent —
        /// a nudge past the three a wheel notch covers — unless
        /// `ScrollSettings.Lines` says otherwise.
        Scroll,

        /// Step the focused agent's reasoning effort.
        Effort,
    }

    /// How the dial scrolls. There are two mechanisms because a pane's output
    /// is either Herdr's to move or the application's own:
    ///
    ///   * Herdr keeps scrollback for anything drawing to the normal screen,
    ///     and `Lines` is how far through it a detent moves.
    ///   * A full-screen application keeps its output to itself and leaves no
    ///     scrollback at all, so all that is left is a key it already answers.
    ///     Herdr can send no wheel event and no page key — `pageup` and every
    ///     spelling of it is rejected — and the arrows mean "previous prompt"
    ///     in every agent, so `Up` / `Down` are empty unless the application's
    ///     own line-scroll action has been bound to a key.
    public sealed record ScrollSettings(int Lines, string Up, string Down)
    {
        /// Five lines a detent: a wheel notch covers three, and a dial turned
        /// between the fingers is a bigger gesture than a wheel flicked by one,
        /// so it moves a little further. No
        /// fallback keys: there is no key the pad can send that every agent
        /// already understands, and sending an arbitrary one is worse than
        /// sending nothing.
        public static ScrollSettings Default { get; } = new(5, "", "");
    }

    /// Where a detent lands the viewport.
    ///
    /// `detent` is the dial's own direction: +1 clockwise — turned right — and
    /// -1 counter-clockwise. Turning it **right scrolls down**, towards the
    /// newest output, the way rolling a wheel towards you does; left scrolls up
    /// into the older output. Herdr counts `offset_from_bottom` from the live
    /// bottom, so scrolling down is a *smaller* offset — the two run opposite
    /// each other, which is why this subtracts where `Climb` adds.
    ///
    /// Clamped rather than wrapping, the same shape as `Climb`: a detent pins at
    /// either end, and never goes negative — `offset_from_bottom` is unsigned and
    /// the server rejects a negative.
    public static int ScrollTo(int offsetFromBottom, int maxOffsetFromBottom, int detent, int lines) =>
        Math.Clamp(offsetFromBottom - detent * lines, 0, Math.Max(0, maxOffsetFromBottom));

    /// The key a detent sends to a pane that scrolls itself, or nil when none is
    /// configured — which is the default, because no such key exists.
    ///
    /// The rightward detent moves the viewport down, so it sends `down`.
    public static string? ScrollKey(int detent, string up, string down)
    {
        var key = detent > 0 ? down : up;
        return string.IsNullOrWhiteSpace(key) ? null : key;
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
