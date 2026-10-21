using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Services;

/// The dial and joystick: scrolling, effort and model for whichever agent has
/// focus.
///
/// The dial **scrolls** the focused pane by default — five lines per detent,
/// moving the pane's own viewport wherever Herdr holds the output, and saying so
/// where it does not — and goes back to stepping effort with `"dial": "effort"`
/// in the config file.
///
/// Claude Code takes orders directly — `/effort <level>` and `/model <name>`
/// are commands, so in effort mode the dial climbs an effort ladder and the
/// joystick cycles a model list, both configurable. Codex has bindable effort
/// commands but only a picker for models, so its dial sends the chords bound in
/// `[tui.keymap]` and its joystick drives the `/model` picker: north opens it,
/// north/south move, east confirms, west cancels.
///
/// Either way the joystick is working a list you cannot see from the pad, so
/// every deflection also reports what it landed on — the macOS app puts a panel
/// on screen, the port reports it through `OnTune`.
///
/// A port of `Sources/WLMicroManager/TuneController.swift`.
public sealed class TuneController
{
    /// The chords `~/.codex/config.toml` binds to
    /// `chat.increase_reasoning_effort` / `chat.decrease_reasoning_effort`.
    public const string CodexEffortUpChord = "ctrl+alt+u";
    public const string CodexEffortDownChord = "ctrl+alt+d";

    /// A Codex picker steered by deflections goes stale if it was dealt with by
    /// hand; after this long a deflection opens a fresh one instead.
    private static readonly TimeSpan PickerLifetime = TimeSpan.FromSeconds(30);

    /// Where each Claude pane sits on the effort ladder and the model list.
    /// Blind state: it starts at the defaults and follows our own commands, so a
    /// change made by hand inside the session drifts it until the next nudge.
    private readonly Dictionary<string, int> _claudeEffortIndex = new();
    private readonly Dictionary<string, int> _claudeModelIndex = new();

    /// The Codex pane whose `/model` picker we opened, if any.
    private string? _codexPickerPane;
    private DateTimeOffset _codexPickerOpened;

    public Action<string>? OnError { get; set; }

    /// What the last detent or deflection landed on, for the status line.
    public Action<string>? OnTune { get; set; }

    /// The bridge owns the loaded config, which is reloaded on every start; this
    /// reads through to it so a config edit only needs an off/on toggle.
    public Func<KeyBindings> Bindings { get; set; } = static () => new KeyBindings();

    // MARK: - Dial: scroll, or effort

    /// A dial detent: +1 clockwise, -1 counter-clockwise.
    public async Task DialAsync(int step, CancellationToken ct = default)
    {
        var focused = await FocusedPaneAsync(ct);
        if (focused is not { } target) return;
        var (agent, pane) = target;

        if (Bindings().Dial == TuneLadder.DialMode.Scroll)
        {
            await ScrollAsync(step, pane, agent, ct);
            return;
        }

        switch (TuneLadder.KindFor(agent.Agent))
        {
            case TuneLadder.Kind.Claude:
            {
                var ladder = Bindings().ClaudeEfforts;
                if (ladder.Count == 0)
                {
                    OnError?.Invoke("No Claude effort ladder is configured.");
                    return;
                }

                var index = TuneLadder.Climb(
                    _claudeEffortIndex.TryGetValue(pane, out var current) ? current : ladder.Count / 2,
                    step,
                    ladder.Count);
                _claudeEffortIndex[pane] = index;

                await SendCommandAsync(pane, $"/effort {ladder[index]}", ct);
                OnTune?.Invoke($"{agent.ShortName} · effort {ladder[index]}");
                break;
            }

            case TuneLadder.Kind.Codex:
                await HerdrClient.SendKeysAsync(
                    pane,
                    new[] { step > 0 ? CodexEffortUpChord : CodexEffortDownChord },
                    ct);
                OnTune?.Invoke($"{agent.ShortName} · effort {(step > 0 ? "up" : "down")}");
                break;

            default:
                OnError?.Invoke($"No effort control for {agent.Agent}.");
                break;
        }
    }

    /// <summary>Runs an explicit effort action even when the dial default is scroll.</summary>
    public async Task EffortAsync(int step, CancellationToken ct = default)
    {
        var focused = await FocusedPaneAsync(ct);
        if (focused is not { } target) return;
        var (agent, pane) = target;

        switch (TuneLadder.KindFor(agent.Agent))
        {
            case TuneLadder.Kind.Claude:
            {
                var ladder = Bindings().ClaudeEfforts;
                if (ladder.Count == 0)
                {
                    OnError?.Invoke("No Claude effort ladder is configured.");
                    return;
                }
                var index = TuneLadder.Climb(
                    _claudeEffortIndex.TryGetValue(pane, out var current) ? current : ladder.Count / 2,
                    step, ladder.Count);
                _claudeEffortIndex[pane] = index;
                await SendCommandAsync(pane, $"/effort {ladder[index]}", ct);
                OnTune?.Invoke($"{agent.ShortName} · effort {ladder[index]}");
                break;
            }
            case TuneLadder.Kind.Codex:
                await HerdrClient.SendKeysAsync(pane,
                    new[] { step > 0 ? CodexEffortUpChord : CodexEffortDownChord }, ct);
                OnTune?.Invoke($"{agent.ShortName} · effort {(step > 0 ? "up" : "down")}");
                break;
            default:
                OnError?.Invoke($"No effort control for {agent.Agent}.");
                break;
        }
    }

    /// A detent's scroll, in whatever is running in the pane. Unlike effort this
    /// is not agent-specific and needs no ladder and no per-pane state — but it
    /// has two mechanisms, because a pane's output is either Herdr's to move or
    /// the application's own.
    ///
    /// Herdr holds the scrollback for anything drawing to the normal screen, and
    /// there a detent moves the viewport by an exact number of lines without
    /// needing anything of the application at all — five lines, a nudge past a
    /// wheel notch's three, with the rightward detent moving down and the
    /// leftward one up.
    /// A full-screen application draws to the alternate screen, and those rows
    /// never reach the host's scrollback, so there is nothing to move: the dial
    /// says so rather than sending a guess.
    private async Task ScrollAsync(int step, string pane, HerdrAgent agent, CancellationToken ct)
    {
        var settings = Bindings().Scroll;

        if (await HerdrClient.PaneScrollAsync(pane, ct) is { MaxOffsetFromBottom: > 0 } scroll)
        {
            var target = TuneLadder.ScrollTo(
                scroll.OffsetFromBottom, scroll.MaxOffsetFromBottom, step, settings.Lines);
            await HerdrClient.ScrollPaneAsync(pane, target, ct);
            OnTune?.Invoke($"{agent.ShortName} · {target}/{scroll.MaxOffsetFromBottom} lines back");
            return;
        }

        if (TuneLadder.ScrollKey(step, settings.Up, settings.Down) is not { } key)
        {
            // Nothing here can be scrolled, and nothing was configured to try.
            // The pad can send no wheel and no page key, so an application that
            // draws itself has to be told to stop drawing itself — see the dial
            // docs for the switch.
            OnTune?.Invoke($"{agent.ShortName} · nothing to scroll — a full-screen app keeps its own output");
            return;
        }

        // A configured key is read as a one-line scroll, so a detent lands the
        // same distance the pane's own scroll would.
        var keys = new List<string>(settings.Lines);
        for (var i = 0; i < settings.Lines; i++) keys.Add(key);
        await HerdrClient.SendKeysAsync(pane, keys, ct);
        OnTune?.Invoke($"{agent.ShortName} · scrolled {(step > 0 ? "down" : "up")} {settings.Lines} lines");
    }

    // MARK: - Joystick: model

    public async Task JoystickAsync(Pad.JoystickDirection direction, CancellationToken ct = default)
    {
        var focused = await FocusedPaneAsync(ct);
        if (focused is not { } target) return;
        var (agent, pane) = target;

        switch (TuneLadder.KindFor(agent.Agent))
        {
            case TuneLadder.Kind.Claude:
                await ClaudeModelAsync(direction, pane, agent, ct);
                break;

            case TuneLadder.Kind.Codex:
                await CodexModelAsync(direction, pane, agent, ct);
                break;

            default:
                OnError?.Invoke($"No model control for {agent.Agent}.");
                break;
        }
    }

    private async Task ClaudeModelAsync(
        Pad.JoystickDirection direction, string pane, HerdrAgent agent, CancellationToken ct)
    {
        var models = Bindings().ClaudeModels;
        if (models.Count == 0)
        {
            OnError?.Invoke("No Claude model list is configured.");
            return;
        }

        // East and west confirm and cancel on Codex's picker; a plain list has
        // nothing for them to do.
        if (TuneLadder.StepFor(direction) is not { } step) return;

        var index = TuneLadder.Cycle(
            _claudeModelIndex.TryGetValue(pane, out var current) ? current : 0, step, models.Count);
        _claudeModelIndex[pane] = index;

        await SendCommandAsync(pane, $"/model {models[index]}", ct);
        OnTune?.Invoke($"{agent.ShortName} · model {models[index]}");
    }

    private async Task CodexModelAsync(
        Pad.JoystickDirection direction, string pane, HerdrAgent agent, CancellationToken ct)
    {
        var pickerOpen = _codexPickerPane == pane
            && DateTimeOffset.UtcNow - _codexPickerOpened < PickerLifetime;

        if (!pickerOpen)
        {
            // Any vertical deflection opens the picker; the rest is steering.
            if (TuneLadder.StepFor(direction) is null) return;

            await SendCommandAsync(pane, "/model", ct);
            _codexPickerPane = pane;
            _codexPickerOpened = DateTimeOffset.UtcNow;
            ShowCodexModels(agent);
            return;
        }

        switch (direction)
        {
            case Pad.JoystickDirection.North:
                await HerdrClient.SendKeysAsync(pane, new[] { "up" }, ct);
                ShowCodexModels(agent);
                break;

            case Pad.JoystickDirection.South:
                await HerdrClient.SendKeysAsync(pane, new[] { "down" }, ct);
                ShowCodexModels(agent);
                break;

            case Pad.JoystickDirection.East:
                await HerdrClient.SendKeysAsync(pane, new[] { "enter" }, ct);
                _codexPickerPane = null;
                OnTune?.Invoke($"{agent.ShortName} · model chosen");
                break;

            case Pad.JoystickDirection.West:
                await HerdrClient.SendKeysAsync(pane, new[] { "esc" }, ct);
                _codexPickerPane = null;
                OnTune?.Invoke($"{agent.ShortName} · model cancelled");
                break;
        }

        _codexPickerOpened = DateTimeOffset.UtcNow;
    }

    /// Codex's picker is its own; arrow keys are pushed at it without being told
    /// where the cursor sits, so this is a numbered reading of the list and
    /// nothing is marked as current.
    private void ShowCodexModels(HerdrAgent agent) =>
        OnTune?.Invoke(
            $"{agent.ShortName} · {string.Join(" / ", Bindings().CodexModels)}  " +
            "(north and south move · east confirms · west cancels)");

    // MARK: - Plumbing

    private async Task<(HerdrAgent Agent, string Pane)?> FocusedPaneAsync(CancellationToken ct)
    {
        var agent = await HerdrClient.FocusedAgentAsync(ct);
        if (agent?.PaneId is not { } pane)
        {
            OnError?.Invoke("Nothing has focus in Herdr right now.");
            return null;
        }

        return (agent, pane);
    }

    /// Slash commands go in as text plus enter: typed, then submitted.
    private static async Task SendCommandAsync(string pane, string command, CancellationToken ct)
    {
        await HerdrClient.SendTextAsync(pane, command, ct);
        await HerdrClient.SendKeysAsync(pane, new[] { "enter" }, ct);
    }
}
