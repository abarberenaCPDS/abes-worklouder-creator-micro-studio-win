using System.Text.Json;
using MicroManager.App.Platforms.Windows;
using MicroManager.Core.Config;
using MicroManager.Core.Emulator;
using MicroManager.Core.Herdr;
using MicroManager.Core.Keymap;
using MicroManager.Core.Pad;
using MicroManager.Core.Transport;
using MicroManager.Transport.Winrt;

namespace MicroManager.App.Services;

public interface IBridgeService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    event Action<List<HerdrAgent>>? OnAgentStateChanged;
    event Action? OnDeviceConnected;
    event Action? OnDeviceDisconnected;
    event Action<string>? OnError;

    /// The lit keys, as packed 0xRRGGBB by key id. Keys that are dark are
    /// absent, so an empty map means the pad is showing nothing.
    event Action<IReadOnlyDictionary<int, int>>? OnKeyStateChanged;

    /// A one-line description of what the pad was just used for: the agent a key
    /// focused, the macro it typed, where the dial landed. The pad has no screen,
    /// so this is the only feedback a press gets.
    event Action<string>? OnActivity;

    bool IsConnected { get; }
    bool IsEnabled { get; set; }
}

public class BridgeService : IBridgeService, IDisposable, IAsyncDisposable
{
    private readonly BridgeConfig _config;
    private readonly bool _useEmulator;
    private readonly CancellationTokenSource _cts = new();
    private readonly TuneController _tune;
    private Task? _pollTask;
    private RpcDevice? _rpcDevice;

    /// The agents from the last poll. Read on the device's read thread when a
    /// key is pressed, so it is only ever replaced wholesale, never mutated.
    private List<HerdrAgent> _agents = new();

    /// The last picture sent to the pad. Every tick recomputes it and stops
    /// there if nothing moved, so a quiet Herdr costs nothing on the wire — and
    /// while a fingerprint only covers what is sent, it covers every thread, so
    /// one agent changing still repaints when the worst state has not.
    private string? _fingerprint;

    /// Reloaded on every start, so a config edit only needs an off/on toggle.
    private KeyBindings _keyBindings = KeyBindings.Load();

    private volatile bool _isEnabled = true;
    private bool _warnedNoTerminal;
    private bool _disposed;

    public bool IsConnected => _rpcDevice?.IsConnected ?? false;

    /// Switching the bridge off clears the lights but deliberately leaves the
    /// keymap alone: rebinding is a flash write, and the keys light instantly on
    /// the way back in if the bindings are still there.
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            _fingerprint = null;   // the next tick repaints, or clears, the pad
        }
    }

    public event Action<List<HerdrAgent>>? OnAgentStateChanged;
    public event Action? OnDeviceConnected;
    public event Action? OnDeviceDisconnected;
    public event Action<string>? OnError;
    public event Action<IReadOnlyDictionary<int, int>>? OnKeyStateChanged;
    public event Action<string>? OnActivity;

    public BridgeService() : this(useEmulator: IsEmulatorForced()) { }

    public BridgeService(bool useEmulator)
    {
        _useEmulator = useEmulator;
        _config = new BridgeConfig();
        _tune = new TuneController
        {
            Bindings = () => _keyBindings,
            OnError = message => OnError?.Invoke(message),
            OnTune = message => OnActivity?.Invoke(message),
        };
    }

    /// <summary>
    /// Set MICROMANAGER_EMULATOR=1 to run against the in-process pad emulator
    /// instead of looking for a real Creator Micro 2 (useful without hardware).
    /// </summary>
    private static bool IsEmulatorForced() =>
        Environment.GetEnvironmentVariable("MICROMANAGER_EMULATOR") is "1" or "true" or "True" or "TRUE";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _keyBindings = KeyBindings.Load();

        try
        {
            _rpcDevice = await OpenDeviceAsync(cancellationToken).ConfigureAwait(false);
            OnDeviceConnected?.Invoke();

            try
            {
                var changed = await KeymapManager.ApplyAsync(_rpcDevice);
                System.Diagnostics.Debug.WriteLine($"Keymap applied: {changed}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Keymap binding failed: {ex.Message}");
            }

            _pollTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await PollOnceAsync(_cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        OnError?.Invoke($"Poll error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(_config.PollInterval, _cts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }, _cts.Token);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Connection failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Prefer the real Creator Micro 2 over raw HID; fall back to the in-process
    /// emulator when no device is attached so the app still runs in development.
    /// </summary>
    private async Task<RpcDevice> OpenDeviceAsync(CancellationToken cancellationToken)
    {
        if (!_useEmulator)
        {
            var device = CreateDevice(new WinrtTransport());
            try
            {
                await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"Connected via {device.Transport}");
                return device;
            }
            catch (DeviceNotConnectedException)
            {
                device.Disconnect();
                OnError?.Invoke("Creator Micro 2 not found; using the pad emulator.");
            }
        }

        var emulator = CreateDevice(new PadEmulator());
        await emulator.ConnectAsync(cancellationToken).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine("Connected via pad emulator");
        return emulator;
    }

    private RpcDevice CreateDevice(IWlTransport transport)
    {
        var device = new RpcDevice(transport);
        device.OnDisconnect += (_, _) =>
        {
            // Repaint from scratch once the pad is back: what it was showing
            // before it went away is not what it is showing now.
            _fingerprint = null;
            OnDeviceDisconnected?.Invoke();
        };
        device.OnNotification += (_, notification) => OnNotification(notification);
        return device;
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask; } catch { }
        }

        await AllLightsOffAsync();

        try { _rpcDevice?.Disconnect(); } catch { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (_rpcDevice is not { } device) return;

        var agents = await HerdrClient.ListAgentsAsync(ct);
        _agents = agents;

        var live = IsEnabled && agents.Count > 0;
        var aggregate = live ? StatusMapper.Aggregate(agents, _config) : null;
        var threads = live ? BuildThreads(agents) : OffThreads();

        // The agent list is what the window shows, so it goes out every tick.
        OnAgentStateChanged?.Invoke(agents);

        var fingerprint = Fingerprint(threads, aggregate);
        if (fingerprint == _fingerprint) return;
        _fingerprint = fingerprint;

        PublishKeyState(threads);

        var zone = live ? StatusMapper.ZoneFor(aggregate, _config) : null;
        // The key-backlight zone is left alone by default: the keys are painted
        // per-agent instead, and a zone colour would only compete with them.
        var keys = _config.DriveBacklight && zone is not null ? zone : Oai.Zone.Dark;

        await device.RequestAsync(Oai.MethodThreads, Oai.ThreadsParams(threads), ct);
        await device.RequestAsync(Oai.MethodRgbConfig, Oai.RgbConfigParams(keys, zone ?? Oai.Zone.Dark), ct);
    }

    private List<Oai.Thread> BuildThreads(List<HerdrAgent> agents)
    {
        var threads = StatusMapper.ThreadsFor(agents, _config);
        threads.Add(StatusMapper.StackThread(false, _config));
        threads.Add(StatusMapper.TabCycleThread(_config));
        // Both keys light their windows, and neither window exists yet: the
        // stack and land panels are Phase 3.
        threads.Add(StatusMapper.LandThread(false, _config));
        threads.AddRange(FlexThreads());
        return threads;
    }

    /// Macro and voice keys share ids, so the binding decides each key's light:
    /// configured text wins, the wide key falls back to voice.
    private IEnumerable<Oai.Thread> FlexThreads()
    {
        foreach (var key in Pad.MacroKeyIds.Concat(Pad.VoiceKeyIds))
        {
            if (_keyBindings.TextFor(key) is not null)
            {
                yield return StatusMapper.MacroThread(key, _config);
            }
            else if (Pad.VoiceKeyIds.Contains(key))
            {
                yield return StatusMapper.VoiceThread(key, active: false, _config);
            }
            else
            {
                yield return new Oai.Thread(key) { Brightness = 0, Effect = Oai.Effect.Off };
            }
        }
    }

    /// Thread state paints over zone state, so clearing the zones alone leaves
    /// the pad lit. Both have to go.
    private static List<Oai.Thread> OffThreads() =>
        Enumerable.Range(0, Pad.MaxThreadId + 1)
            .Select(id => new Oai.Thread(id)
            {
                Brightness = 0,
                Effect = Oai.Effect.Off,
                SyncKeys = false,
                SyncAmbient = false,
            })
            .ToList();

    private async Task AllLightsOffAsync()
    {
        if (_rpcDevice is not { IsConnected: true } device) return;

        try
        {
            await device.RequestAsync(Oai.MethodThreads, Oai.ThreadsParams(OffThreads()));
            await device.RequestAsync(
                Oai.MethodRgbConfig, Oai.RgbConfigParams(Oai.Zone.Dark, Oai.Zone.Dark));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not clear the pad: {ex.Message}");
        }
    }

    /// Fingerprints the whole rendered picture, not just the aggregate, so one
    /// agent changing still repaints when the worst state has not.
    private static string Fingerprint(List<Oai.Thread> threads, string? aggregate) =>
        string.Join('|', threads.Select(t =>
            $"{t.Id}:{t.Color ?? -1}:{(int?)t.Effect ?? -1}:{t.Brightness ?? -1}")) +
        $"|agg:{aggregate ?? "-"}";

    private void PublishKeyState(List<Oai.Thread> threads)
    {
        var lit = new Dictionary<int, int>();
        foreach (var thread in threads)
        {
            if (thread.Color is not { } color) continue;
            if ((thread.Brightness ?? 0) <= 0) continue;
            if (thread.Effect is not { } effect || effect == Oai.Effect.Off) continue;
            lit[thread.Id] = color;
        }

        OnKeyStateChanged?.Invoke(lit);
    }

    // MARK: - Key presses

    /// Key presses arrive as `v.oai.hid` notifications: `{"m": "v.oai.hid",
    /// "p": {"k": "AG07", "act": 1}}`. Matching only on `method` — the shape
    /// responses use — drops every one of them without an error, so this looks
    /// only at the abbreviated envelope the device actually pushes.
    private void OnNotification((string Method, object? Parameters) notification)
    {
        if (notification.Method != Oai.NotifyHID) return;
        if (notification.Parameters is not JsonElement payload ||
            payload.ValueKind != JsonValueKind.Object) return;

        // `act` is 1 on press and 0 on release; only presses do anything.
        if (!payload.TryGetProperty("act", out var act) ||
            act.ValueKind != JsonValueKind.Number || act.GetInt32() != 1) return;

        if (!payload.TryGetProperty("k", out var key) || key.ValueKind != JsonValueKind.String) return;
        if (Oai.AgIndex(key.GetString()) is not { } index) return;

        // Handled off the read loop: the handler talks to Herdr, and the read
        // loop has to stay free to deliver the pad's replies.
        _ = Task.Run(() => HandleKeyPressAsync(index));
    }

    /// Every bound key arrives here. Which key does what is the one place that
    /// has to agree with `Pad`, so keep the dispatch in a single list.
    private async Task HandleKeyPressAsync(int index)
    {
        try
        {
            if (index == Pad.TabCycleKeyId)
            {
                await HerdrClient.CycleTabsAsync(_cts.Token);
                Activity("Cycled tabs");
            }
            else if (Pad.MacroKeyIds.Contains(index) || Pad.VoiceKeyIds.Contains(index))
            {
                // A configured text macro wins over the key's built-in role,
                // which is how the config file may repurpose the wide voice key.
                if (_keyBindings.TextFor(index) is { } text)
                {
                    await InjectPromptAsync(index, text);
                }
                else if (Pad.VoiceKeyIds.Contains(index))
                {
                    Activity("Voice key — no speech engine on Windows yet");
                }
            }
            else if (index == Pad.DialUpId || index == Pad.DialDownId)
            {
                await _tune.DialAsync(index == Pad.DialUpId ? 1 : -1, _cts.Token);
            }
            else if (Pad.JoystickDirectionForKeyId(index) is { } direction)
            {
                await _tune.JoystickAsync(direction, _cts.Token);
            }
            else if (Pad.AgentSlotFor(index) is { } slot)
            {
                await FocusSlotAsync(slot);
            }

            // The stack (6) and land (8) keys open windows that are Phase 3, so
            // they are inert rather than silently pointing at nothing.
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex.Message);
        }
    }

    /// Types a macro string into the focused agent's prompt, unsubmitted — the
    /// human still reads it and presses enter.
    private async Task InjectPromptAsync(int index, string text)
    {
        var agent = await HerdrClient.FocusedAgentAsync(_cts.Token);
        if (agent?.PaneId is not { } pane)
        {
            OnError?.Invoke("Nothing has focus in Herdr right now.");
            return;
        }

        await HerdrClient.SendTextAsync(pane, text, _cts.Token);
        Activity($"Typed key {index}: {text}");
    }

    /// Slot N is the Nth key in reading order (`Pad.AgentKeyIds[N]`) — the same
    /// mapping the lighting uses, which is what makes the key you look at the
    /// key you press.
    ///
    /// Herdr selects the pane but leaves the terminal wherever it was in the
    /// window order, so an agent key pressed from a browser would move a cursor
    /// you could not see. The terminal comes forward with it.
    private async Task FocusSlotAsync(int slot)
    {
        if (slot < 0 || slot >= _agents.Count) return;
        if (_agents[slot].FocusTarget is not { } target) return;

        await HerdrClient.FocusAgentAsync(target, _cts.Token);
        var name = _agents[slot].ShortName;
        Activity($"Focused {name}");

        if (TerminalFocus.TryRaiseTerminal())
        {
            _warnedNoTerminal = false;
        }
        else if (!_warnedNoTerminal)
        {
            // Once, not on every press: a wrong terminal name would otherwise
            // bury every other message.
            _warnedNoTerminal = true;
            OnError?.Invoke(
                "Focused the agent, but found no terminal window to raise. " +
                "Set WL_TERMINAL_PROCESS to your terminal's executable name.");
        }
    }

    private void Activity(string message) => OnActivity?.Invoke(message);

    public void Dispose()
    {
        _cts.Cancel();
        _rpcDevice?.Disconnect();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            if (_pollTask is not null)
            {
                try { await _pollTask; } catch { }
            }
            if (_rpcDevice is not null)
            {
                await _rpcDevice.DisposeAsync();
            }
            _disposed = true;
        }
    }
}
