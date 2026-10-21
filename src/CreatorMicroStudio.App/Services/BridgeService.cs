using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Platforms.Windows;
using AbesWorkLouderCreatorMicroStudio.Windows.Core;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Emulator;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Keymap;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;
using AbesWorkLouderCreatorMicroStudio.Windows.Transport.Winrt;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Services;

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
    event Action<KeyBindings>? OnBindingsChanged;
    event Action<IReadOnlySet<int>>? OnInputCapabilitiesChanged;
    event Action<LightingPreferences>? OnLightingChanged;
    Func<string, string, Task<string?>>? AskUser { get; set; }

    /// A one-line description of what the pad was just used for: the agent a key
    /// focused, the macro it typed, where the dial landed. The pad has no screen,
    /// so this is the only feedback a press gets.
    event Action<string>? OnActivity;

    bool IsConnected { get; }

    /// <summary>
    /// True when no pad could be opened and the app is driving the in-process
    /// emulator instead. Without this the status line reads "Connected" either
    /// way, which hides a missing — or sleeping, over Bluetooth — pad.
    /// </summary>
    bool UsingEmulator { get; }

    bool IsEnabled { get; set; }
    KeyBindings Bindings { get; }
    IReadOnlySet<int> AvailableInputIds { get; }
    LightingPreferences Lighting { get; }
    bool IsPreviewingLighting { get; }
    void SetActionOverride(int input, InputAction? action);
    void SetActionOverrides(IEnumerable<int> inputs, InputAction? action);

    /// <summary>
    /// Persists a host-side agent layout. Newly added keys are reset to Focus
    /// Agent; removed keys keep their actions. The device keymap is never touched.
    /// </summary>
    void SetAgentKeys(IEnumerable<int> keys, KeyBindings? expectedBindings = null);
    Task TestActionAsync(int input);
    Task TestActionAsync(int input, InputAction action);
    void PreviewLightingPreferences(LightingPreferences preferences);
    void SaveLightingPreferences(LightingPreferences preferences);
    void RevertLightingPreferences();
    void ReloadConfiguration();

    /// <summary>
    /// While this is on, the pad is being driven by hand: Herdr is still tracked
    /// and the keys still work, but the bridge stops painting, so a chosen
    /// colour is not wiped by the next poll. Turning it off hands the pad back
    /// and the next tick repaints it from the agents.
    /// </summary>
    bool ManualLighting { get; set; }

    /// <summary>
    /// Sends a hand-built picture — the lighting console's apply buttons — and
    /// mirrors it into the window's swatches.
    /// </summary>
    Task ApplyLightingAsync(
        List<Oai.Thread>? threads = null,
        Oai.Zone? keys = null,
        Oai.Zone? ambient = null);
}

public class BridgeService : IBridgeService, IDisposable, IAsyncDisposable
{
    private readonly BridgeConfig _config;
    private readonly bool _useEmulator;
    private readonly CancellationTokenSource _cts = new();
    private readonly TuneController _tune;
    private Task? _pollTask;
    private Task? _supervisorTask;

    /// Swapped by the supervisor when the pad comes back, so it is read from
    /// the read thread, the poll loop and the window all at once.
    private volatile RpcDevice? _rpcDevice;

    /// Released when the current device goes away. The supervisor waits on it
    /// rather than polling, so a healthy pad costs nothing.
    private readonly SemaphoreSlim _deviceLost = new(0);

    /// Before reopening, then doubling to the cap. A pad that is asleep or out
    /// of range comes back on its own schedule, so this never gives up.
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

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
    private LightingPreferences _lighting = LightingPreferences.Load();
    private LightingPreferences _savedLighting = LightingPreferences.Load();

    private volatile bool _isEnabled = true;
    private volatile bool _manualLighting;
    private bool _warnedNoTerminal;
    private bool _disposed;
    private bool _isPreviewingLighting;

    /// <summary>
    /// What the lighting console last put on the keys, so the window's swatches
    /// follow it. Rebuilt from the agents as soon as the bridge paints again.
    /// </summary>
    private readonly Dictionary<int, int> _manualLit = new();
    private HashSet<int> _availableInputIds = Enumerable.Range(0, Pad.KeyCount)
        .Concat(new[] { Pad.DialClockwiseId, Pad.DialCounterClockwiseId }).ToHashSet();

    public bool IsConnected => _rpcDevice?.IsConnected ?? false;
    public KeyBindings Bindings => _keyBindings;
    public IReadOnlySet<int> AvailableInputIds => _availableInputIds;
    public LightingPreferences Lighting => CloneLighting(_lighting);
    public bool IsPreviewingLighting => _isPreviewingLighting;

    /// <summary>
    /// Set when no pad could be opened and the emulator stood in for it. The
    /// window shows this, so a missing — or asleep, over Bluetooth — pad is
    /// never mistaken for a working one.
    /// </summary>
    public bool UsingEmulator => _usingEmulator;

    private volatile bool _usingEmulator;

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

    public bool ManualLighting
    {
        get => _manualLighting;
        set
        {
            if (_manualLighting == value) return;
            _manualLighting = value;
            _fingerprint = null;   // handed back: the next tick repaints
        }
    }

    public event Action<List<HerdrAgent>>? OnAgentStateChanged;
    public event Action? OnDeviceConnected;
    public event Action? OnDeviceDisconnected;
    public event Action<string>? OnError;
    public event Action<IReadOnlyDictionary<int, int>>? OnKeyStateChanged;
    public event Action<KeyBindings>? OnBindingsChanged;
    public event Action<IReadOnlySet<int>>? OnInputCapabilitiesChanged;
    public event Action<LightingPreferences>? OnLightingChanged;
    public Func<string, string, Task<string?>>? AskUser { get; set; }
    public event Action<string>? OnActivity;

    public BridgeService() : this(useEmulator: IsEmulatorForced()) { }

    public BridgeService(bool useEmulator)
    {
        _useEmulator = useEmulator;
        _config = new BridgeConfig();
        _lighting = LightingPreferences.Load();
        _savedLighting = CloneLighting(_lighting);
        _tune = new TuneController
        {
            Bindings = () => _keyBindings,
            OnError = message => OnError?.Invoke(message),
            OnTune = message => OnActivity?.Invoke(message),
        };
    }

    /// <summary>
    /// Set WL_EMULATE=1 to run against the in-process pad emulator instead of
    /// looking for a real Creator Micro 2 (useful without hardware).
    /// </summary>
    private static bool IsEmulatorForced() => StudioEnvironment.IsEmulated();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _keyBindings = KeyBindings.Load();
        _lighting = LightingPreferences.Load();
        _savedLighting = CloneLighting(_lighting);
        _isPreviewingLighting = false;
        OnLightingChanged?.Invoke(CloneLighting(_lighting));

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
            await RefreshInputCapabilitiesAsync(_rpcDevice).ConfigureAwait(false);

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
        finally
        {
            // Started whether the first attempt worked or not, so a pad that
            // turns up later — plugged in, or woken from Bluetooth sleep — is
            // picked up without a restart. Not when the emulator was asked for
            // explicitly: then a real pad is not wanted.
            if (!_useEmulator)
            {
                _supervisorTask = Task.Run(SuperviseAsync);
            }
        }
    }

    public void SetActionOverride(int input, InputAction? action)
    {
        SetActionOverrides(new[] { input }, action);
    }

    public void SetActionOverrides(IEnumerable<int> inputs, InputAction? action)
    {
        KeyBindings.SetActions(inputs, action);
        _keyBindings = KeyBindings.Load();
        _fingerprint = null;
        OnBindingsChanged?.Invoke(_keyBindings);
    }

    public void SetAgentKeys(IEnumerable<int> keys, KeyBindings? expectedBindings = null)
    {
        KeyBindings.SetAgentKeys(keys, expectedBindings);
        _keyBindings = KeyBindings.Load();
        _fingerprint = null;
        OnBindingsChanged?.Invoke(_keyBindings);
    }

    public Task TestActionAsync(int input) => HandleKeyPressAsync(input);
    public Task TestActionAsync(int input, InputAction action) => DispatchActionAsync(input, action);

    public void PreviewLightingPreferences(LightingPreferences preferences)
    {
        _lighting = CloneLighting(preferences);
        _isPreviewingLighting = true;
        _fingerprint = null;
        OnLightingChanged?.Invoke(CloneLighting(_lighting));
    }

    public void SaveLightingPreferences(LightingPreferences preferences)
    {
        LightingPreferences.Save(preferences);
        _savedLighting = CloneLighting(preferences);
        _lighting = CloneLighting(preferences);
        _isPreviewingLighting = false;
        _fingerprint = null;
        OnLightingChanged?.Invoke(CloneLighting(_lighting));
    }

    public void RevertLightingPreferences()
    {
        _lighting = CloneLighting(_savedLighting);
        _isPreviewingLighting = false;
        _fingerprint = null;
        OnLightingChanged?.Invoke(CloneLighting(_lighting));
    }

    public void ReloadConfiguration()
    {
        _keyBindings = KeyBindings.Load();
        _lighting = LightingPreferences.Load();
        _savedLighting = CloneLighting(_lighting);
        _isPreviewingLighting = false;
        _fingerprint = null;
        OnBindingsChanged?.Invoke(_keyBindings);
        OnLightingChanged?.Invoke(CloneLighting(_lighting));
    }

    private static LightingPreferences CloneLighting(LightingPreferences value) =>
        LightingPreferences.Parse(value.ToJsonString());

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

                // Not announced here: this runs before the connected event, and
                // the status it would set is overwritten the moment the app
                // reports itself connected. The window reads this flag instead.
                _usingEmulator = true;
            }
        }

        var emulator = CreateDevice(new PadEmulator());
        await emulator.ConnectAsync(cancellationToken).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine("Connected via pad emulator");
        return emulator;
    }

    private async Task RefreshInputCapabilitiesAsync(RpcDevice device)
    {
        try
        {
            var config = await KeymapManager.ReadAsync(device).ConfigureAwait(false);
            _availableInputIds = KeymapManager.AvailableInputIds(config).ToHashSet();
            OnInputCapabilitiesChanged?.Invoke(_availableInputIds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read pad input capabilities: {ex.Message}");
        }
    }

    private RpcDevice CreateDevice(IWlTransport transport)
    {
        var device = new RpcDevice(transport);
        device.OnDisconnect += (_, _) =>
        {
            // A device that has already been replaced must not signal: closing
            // the old one would otherwise send the supervisor looking for a pad
            // it is already holding.
            if (!ReferenceEquals(_rpcDevice, device)) return;

            // Repaint from scratch once the pad is back: what it was showing
            // before it went away is not what it is showing now.
            _fingerprint = null;
            OnDeviceDisconnected?.Invoke();
            _deviceLost.Release();
        };
        device.OnNotification += (_, notification) => OnNotification(notification);
        return device;
    }

    // MARK: - Reconnecting

    /// Waits for the pad to go away, then gets it back. A cable being pulled and
    /// a Bluetooth link dropping look the same from here — the device node
    /// disappears either way — so one path covers both.
    private async Task SuperviseAsync()
    {
        var ct = _cts.Token;

        // Launched with no pad, with the emulator standing in: look straight
        // away, so plugging one in or waking a sleeping one just starts working.
        if (_usingEmulator) _deviceLost.Release();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _deviceLost.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            await ReconnectAsync(ct).ConfigureAwait(false);
        }
    }

    /// Reopens the pad, retrying until it is back. The emulator is deliberately
    /// not a fallback here: quietly putting a phantom pad on the lights is worse
    /// than the window saying the pad is gone.
    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = FirstRetryDelay;
        var announced = false;

        while (!ct.IsCancellationRequested)
        {
            // Already back, or never actually lost.
            if (_rpcDevice is { IsConnected: true }) return;

            if (!announced)
            {
                announced = true;
                Activity("Pad went away — looking for it");
            }

            try
            {
                var device = CreateDevice(new WinrtTransport());
                await device.ConnectAsync(ct).ConfigureAwait(false);

                var previous = _rpcDevice;
                _rpcDevice = device;
                _usingEmulator = false;
                _fingerprint = null;

                // The replacement is in place first, so the old device's
                // disconnect is ignored rather than starting this over.
                if (previous is not null)
                {
                    try { previous.Disconnect(); } catch { }
                }

                OnDeviceConnected?.Invoke();

                try
                {
                    await KeymapManager.ApplyAsync(device).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"Keymap binding failed: {ex.Message}");
                }
                await RefreshInputCapabilitiesAsync(device).ConfigureAwait(false);

                Activity("Pad reconnected");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (DeviceNotConnectedException)
            {
                // Nothing on the bus yet. That is the ordinary state while a pad
                // is asleep or unplugged, so it earns no error.
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Reconnect failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            delay = delay * 2 > MaxRetryDelay ? MaxRetryDelay : delay * 2;
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask; } catch { }
        }
        if (_supervisorTask is not null)
        {
            try { await _supervisorTask; } catch { }
        }

        await AllLightsOffAsync();

        try { _rpcDevice?.Disconnect(); } catch { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        // Herdr is still reachable with no pad attached, so the window's agent
        // list keeps updating while the supervisor looks for the pad.
        var agents = await HerdrClient.ListAgentsAsync(ct);
        _agents = agents;

        // The agent list is what the window shows, so it goes out every tick.
        OnAgentStateChanged?.Invoke(agents);

        // Nothing to paint on. Writing here would only earn an error a tick,
        // and the supervisor is already reopening the pad.
        if (_rpcDevice is not { IsConnected: true } device) return;

        var enabled = IsEnabled;
        var hasAgents = enabled && agents.Count > 0;
        var aggregate = hasAgents ? StatusMapper.Aggregate(agents, _config) : null;
        var threads = enabled ? BuildThreads(agents, hasAgents) : LightingEditor.AllOffThreads();

        // Hand-driven lights: Herdr keeps being tracked, but the picture is left
        // exactly as the console left it.
        if (_manualLighting) return;

        var ambient = enabled ? UnderglowZone(aggregate) : Oai.Zone.Dark;
        var keys = enabled ? BacklightZone() : Oai.Zone.Dark;
        var fingerprint = Fingerprint(threads, aggregate, keys, ambient);
        if (fingerprint == _fingerprint) return;
        _fingerprint = fingerprint;

        PublishKeyState(threads);

        await device.RequestAsync(Oai.MethodThreads, Oai.ThreadsParams(threads), ct);
        await device.RequestAsync(Oai.MethodRgbConfig, Oai.RgbConfigParams(keys, ambient), ct);
    }

    private List<Oai.Thread> BuildThreads(List<HerdrAgent> agents, bool hasAgents)
    {
        var layout = _keyBindings.AgentKeyIds;
        var threads = new Dictionary<int, Oai.Thread>();

        // Agent status for the configured keys. A custom action on one of those
        // keys takes precedence below, so it is painted as its action instead.
        foreach (var thread in StatusMapper.ThreadsFor(
                     hasAgents ? agents : new List<HerdrAgent>(), _config, layout))
        {
            threads[thread.Id] = thread;
        }

        // Every keycap gets a thread. A key removed from the layout must be
        // repainted, or it would keep showing the status of an agent it no
        // longer answers for.
        for (var id = 0; id < Pad.KeyCount; id++)
        {
            var action = _keyBindings.ActionFor(id);
            var slot = _keyBindings.AgentSlotFor(id);
            var showsAgent = hasAgents && slot is { } agentSlot && agentSlot < agents.Count &&
                action.Kind == InputActionKind.FocusAgent;

            if (showsAgent)
            {
                threads[id] = _lighting.LookForStatus(agents[slot!.Value].AgentStatus)
                    .Thread(id, _lighting.Brightness, _lighting.Speed);
            }
            else if (_lighting.Keys.TryGetValue(id, out var fixedLook))
            {
                threads[id] = fixedLook.Thread(id, _lighting.Brightness, _lighting.Speed);
            }
            else
            {
                threads[id] = StatusMapper.ActionThread(action, id, _config, layout)
                    ?? new Oai.Thread(id) { Brightness = 0, Effect = Oai.Effect.Off };
            }
        }

        return threads.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
    }

    private Oai.Zone UnderglowZone(string? aggregate) => _lighting.Underglow.Mode switch
    {
        LightingPreferences.UnderglowMode.Off => Oai.Zone.Dark,
        LightingPreferences.UnderglowMode.Custom => _lighting.Underglow.Look.Zone(_lighting.Brightness, _lighting.Speed),
        LightingPreferences.UnderglowMode.Status when aggregate is not null =>
            _lighting.LookForStatus(aggregate).Zone(_lighting.Brightness, _lighting.Speed),
        _ => Oai.Zone.Dark,
    };

    private Oai.Zone BacklightZone() => _lighting.Backlight.Mode switch
    {
        LightingPreferences.BacklightMode.Custom => _lighting.Backlight.Look.Zone(_lighting.Brightness, _lighting.Speed),
        _ => Oai.Zone.Dark,
    };

    /// Thread state paints over zone state, so clearing the zones alone leaves
    /// the pad lit. Both have to go.
    private async Task AllLightsOffAsync()
    {
        if (_rpcDevice is not { IsConnected: true } device) return;

        try
        {
            await device.RequestAsync(Oai.MethodThreads, Oai.ThreadsParams(LightingEditor.AllOffThreads()));
            await device.RequestAsync(
                Oai.MethodRgbConfig, Oai.RgbConfigParams(Oai.Zone.Dark, Oai.Zone.Dark));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not clear the pad: {ex.Message}");
        }
    }

    /// <summary>
    /// The lighting console's way onto the pad. Only what it was handed is sent,
    /// so applying to a selection leaves every other key exactly as it was.
    /// </summary>
    public async Task ApplyLightingAsync(
        List<Oai.Thread>? threads = null,
        Oai.Zone? keys = null,
        Oai.Zone? ambient = null)
    {
        if (_rpcDevice is not { IsConnected: true } device)
        {
            OnError?.Invoke("The pad is not connected, so nothing was sent to it.");
            return;
        }

        try
        {
            if (threads is { Count: > 0 })
            {
                await device.RequestAsync(Oai.MethodThreads, Oai.ThreadsParams(threads));
            }

            if (keys is not null || ambient is not null)
            {
                await device.RequestAsync(
                    Oai.MethodRgbConfig,
                    Oai.RgbConfigParams(keys ?? Oai.Zone.Dark, ambient ?? Oai.Zone.Dark));
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Could not light the pad: {ex.Message}");
            return;
        }

        if (threads is { Count: > 0 })
        {
            foreach (var thread in threads)
            {
                if (thread.Color is { } color && (thread.Brightness ?? 0) > 0 &&
                    thread.Effect is { } effect && effect != Oai.Effect.Off)
                {
                    _manualLit[thread.Id] = color;
                }
                else
                {
                    _manualLit.Remove(thread.Id);
                }
            }
        }

        // A zone lights every key under it, so the swatches follow the colour.
        if (keys is { } zone)
        {
            for (var id = 0; id < Pad.KeyCount; id++)
            {
                if (zone.Effect == Oai.Effect.Off || zone.Brightness <= 0) _manualLit.Remove(id);
                else _manualLit[id] = zone.Color;
            }
        }

        PublishKeyState(_manualLit);
    }

    /// Fingerprints the whole rendered picture, not just the aggregate, so one
    /// agent changing still repaints when the worst state has not.
    private static string Fingerprint(List<Oai.Thread> threads, string? aggregate, Oai.Zone keys, Oai.Zone ambient) =>
        string.Join('|', threads.Select(t =>
            $"{t.Id}:{t.Color ?? -1}:{(int?)t.Effect ?? -1}:{t.Brightness ?? -1}")) +
        $"|agg:{aggregate ?? "-"}|keys:{ZoneFingerprint(keys)}|ambient:{ZoneFingerprint(ambient)}";

    private static string ZoneFingerprint(Oai.Zone zone) =>
        $"{zone.Color}:{(int)zone.Effect}:{zone.Brightness}:{zone.Speed}";

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

    /// The lighting console keeps its own map, since it knows what it sent.
    private void PublishKeyState(IReadOnlyDictionary<int, int> lit) => OnKeyStateChanged?.Invoke(lit);

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
            await DispatchActionAsync(index, _keyBindings.ActionFor(index));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex.Message);
        }
    }

    /// <summary>One host-side action dispatcher for keys, dial and joystick inputs.</summary>
    private async Task DispatchActionAsync(int input, InputAction action)
    {
        switch (action.Kind)
        {
            case InputActionKind.None:
                Activity($"{Pad.Input(input)?.Name ?? $"Input {input}"}: no action");
                return;
            case InputActionKind.FocusAgent:
                if (_keyBindings.AgentSlotFor(input) is { } slot) await FocusSlotAsync(slot);
                else await FocusCurrentAgentAsync();
                return;
            case InputActionKind.FocusSlot:
                await FocusSlotAsync(Math.Clamp(action.Slot ?? 1, 1, Pad.KeyCount) - 1);
                return;
            case InputActionKind.NextTab:
                await HerdrClient.CycleTabsAsync(_cts.Token, 1);
                Activity("Next Herdr tab");
                return;
            case InputActionKind.PreviousTab:
                await HerdrClient.CycleTabsAsync(_cts.Token, -1);
                Activity("Previous Herdr tab");
                return;
            case InputActionKind.NextWorkspace:
                await HerdrClient.CycleWorkspacesAsync(1, _cts.Token);
                Activity("Next Herdr workspace");
                return;
            case InputActionKind.PreviousWorkspace:
                await HerdrClient.CycleWorkspacesAsync(-1, _cts.Token);
                Activity("Previous Herdr workspace");
                return;
            case InputActionKind.FocusPane:
                await HerdrClient.FocusPaneAsync((action.Direction ?? InputDirection.Right).ToString().ToLowerInvariant(), _cts.Token);
                Activity($"Focused pane {action.Direction?.ToString().ToLowerInvariant() ?? "right"}");
                return;
            case InputActionKind.ZoomPane:
                await HerdrClient.ZoomPaneAsync(_cts.Token);
                Activity("Toggled pane zoom");
                return;
            case InputActionKind.SendKeys:
                await SendKeysToAgentAsync(action.Value ?? "");
                return;
            case InputActionKind.HerdrCommand:
                await RunHerdrCommandAsync(action.Value ?? "");
                return;
            case InputActionKind.InsertText:
                if (!string.IsNullOrEmpty(action.Value)) await InjectPromptAsync(input, action.Value);
                else Activity("Insert text is not configured");
                return;
            case InputActionKind.SubmitPrompt:
                await SubmitPromptAsync(action.Value ?? "");
                return;
            case InputActionKind.EffortUp:
                await _tune.EffortAsync(1, _cts.Token);
                return;
            case InputActionKind.EffortDown:
                await _tune.EffortAsync(-1, _cts.Token);
                return;
            case InputActionKind.ModelNext:
                await _tune.JoystickAsync(Pad.JoystickDirection.South, _cts.Token);
                return;
            case InputActionKind.ModelPrevious:
                await _tune.JoystickAsync(Pad.JoystickDirection.North, _cts.Token);
                return;
            case InputActionKind.ModelPickerConfirm:
                await _tune.JoystickAsync(Pad.JoystickDirection.East, _cts.Token);
                return;
            case InputActionKind.ModelPickerCancel:
                await _tune.JoystickAsync(Pad.JoystickDirection.West, _cts.Token);
                return;
            case InputActionKind.Scroll:
                SystemActions.Scroll(action.Direction ?? InputDirection.Down, action.Amount ?? 3);
                Activity(action.Summary);
                return;
            case InputActionKind.KeyboardShortcut:
                SystemActions.Shortcut(action.Value ?? "");
                Activity(action.Summary);
                return;
            case InputActionKind.MediaKey:
                SystemActions.Media(action.Media ?? MediaKey.PlayPause);
                Activity(action.Summary);
                return;
            case InputActionKind.Voice:
                SystemActions.VoiceTyping();
                Activity("Windows Voice Typing (Win+H)");
                return;
            case InputActionKind.TypeText:
                SystemActions.TypeText(action.Value ?? "");
                Activity(action.Summary);
                return;
            case InputActionKind.OpenUrl:
                SystemActions.Open(action.Value ?? "");
                Activity(action.Summary);
                return;
            case InputActionKind.ShellCommand:
                await SystemActions.RunShellAsync(action.Value ?? "", _cts.Token);
                Activity("PowerShell command completed");
                return;
            default:
                throw new NotSupportedException($"The action {action.Kind} is not supported.");
        }
    }

    private async Task FocusCurrentAgentAsync()
    {
        var agent = await HerdrClient.FocusedAgentAsync(_cts.Token);
        if (agent?.FocusTarget is not { } target)
        {
            OnError?.Invoke("Nothing has focus in Herdr right now.");
            return;
        }
        await HerdrClient.FocusAgentAsync(target, _cts.Token);
        Activity($"Focused {agent.ShortName}");
        RaiseTerminalIfPossible();
    }

    private async Task SendKeysToAgentAsync(string keys)
    {
        var agent = await HerdrClient.FocusedAgentAsync(_cts.Token);
        if (agent?.PaneId is not { } pane)
        {
            OnError?.Invoke("Nothing has focus in Herdr right now.");
            return;
        }
        var sequence = keys.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (sequence.Length == 0) throw new InvalidOperationException("Enter one or more Herdr key names.");
        await HerdrClient.SendKeysAsync(pane, sequence, _cts.Token);
        Activity($"Sent keys to {agent.ShortName}: {keys}");
    }

    private async Task SubmitPromptAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException("Set prompt text first.");
        var agent = await HerdrClient.FocusedAgentAsync(_cts.Token);
        if (agent?.FocusTarget is not { } target)
        {
            OnError?.Invoke("Nothing has focus in Herdr right now.");
            return;
        }
        await HerdrClient.PromptAgentAsync(target, text, _cts.Token);
        Activity($"Submitted prompt to {agent.ShortName}");
    }

    private async Task RunHerdrCommandAsync(string command)
    {
        var parsed = HerdrCommand.Parse(command);
        var context = parsed.Placeholders.Count == 0
            ? new Dictionary<string, string?>()
            : await HerdrCommandContextAsync(_cts.Token);
        var ask = AskUser is null ? null : new Func<string, string, Task<string?>>((prompt, text) => AskUser(prompt, text));
        var arguments = await parsed.ResolveAsync(context, ask, _cts.Token);
        if (arguments is null)
        {
            Activity("Herdr command cancelled");
            return;
        }
        await HerdrCommandRunner.RunAsync(arguments, _cts.Token);
        Activity($"Ran herdr {string.Join(' ', arguments)}");
    }

    private static async Task<Dictionary<string, string?>> HerdrCommandContextAsync(CancellationToken ct)
    {
        var workspaces = await HerdrClient.ListWorkspacesAsync(ct);
        var tabs = await HerdrClient.ListTabsAsync(ct: ct);
        var pane = await HerdrClient.CurrentPaneIdAsync(ct);
        var agent = await HerdrClient.FocusedAgentAsync(ct);
        var workspace = workspaces.FirstOrDefault(item => item.Focused);
        var tab = workspace is null
            ? tabs.FirstOrDefault(item => item.Focused)
            : tabs.FirstOrDefault(item => item.WorkspaceId == workspace.WorkspaceId && item.Focused)
              ?? (workspace.ActiveTabId is { } active ? tabs.FirstOrDefault(item => item.TabId == active) : null);
        var workspaceId = workspace?.WorkspaceId ?? tab?.WorkspaceId;
        var workspaceLabel = !string.IsNullOrWhiteSpace(workspace?.Label)
            ? workspace.Label
            : workspace is null ? null : $"Workspace {workspace.Number}";
        if (workspaceLabel is null && workspaceId is not null)
        {
            var tabWorkspace = workspaces.FirstOrDefault(item => item.WorkspaceId == workspaceId);
            workspaceLabel = tabWorkspace is null ? null :
                string.IsNullOrWhiteSpace(tabWorkspace.Label) ? $"Workspace {tabWorkspace.Number}" : tabWorkspace.Label;
        }
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["pane"] = pane ?? agent?.PaneId,
            ["tab"] = tab?.TabId,
            ["tab_label"] = tab is null ? null : string.IsNullOrWhiteSpace(tab.Label) ? $"Tab {tab.Number}" : tab.Label,
            ["workspace"] = workspaceId,
            ["workspace_label"] = workspaceLabel,
            ["agent"] = agent?.ShortName ?? agent?.PaneId,
        };
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

    /// Slot N is the Nth key in the configured reading-order layout
    /// (`Bindings.AgentKeyIds[N]`) — the same mapping the lighting uses, which
    /// is what makes the key you look at the key you press.
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

        RaiseTerminalIfPossible();
    }

    private void RaiseTerminalIfPossible()
    {
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
            if (_supervisorTask is not null)
            {
                try { await _supervisorTask; } catch { }
            }
            if (_rpcDevice is not null)
            {
                await _rpcDevice.DisposeAsync();
            }
            _disposed = true;
        }
    }
}
