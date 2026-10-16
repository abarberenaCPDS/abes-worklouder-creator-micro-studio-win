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

    bool IsConnected { get; }
    bool IsEnabled { get; set; }
}

public class BridgeService : IBridgeService, IDisposable, IAsyncDisposable
{
    private readonly BridgeConfig _config;
    private readonly bool _useEmulator;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    private RpcDevice? _rpcDevice;
    private bool _disposed;

    public bool IsConnected => _rpcDevice?.IsConnected ?? false;
    public bool IsEnabled { get; set; } = true;

    public event Action<List<HerdrAgent>>? OnAgentStateChanged;
    public event Action? OnDeviceConnected;
    public event Action? OnDeviceDisconnected;
    public event Action<string>? OnError;

    public BridgeService() : this(useEmulator: IsEmulatorForced()) { }

    public BridgeService(bool useEmulator)
    {
        _useEmulator = useEmulator;
        _config = new BridgeConfig();
    }

    /// <summary>
    /// Set MICROMANAGER_EMULATOR=1 to run against the in-process pad emulator
    /// instead of looking for a real Creator Micro 2 (useful without hardware).
    /// </summary>
    private static bool IsEmulatorForced() =>
        Environment.GetEnvironmentVariable("MICROMANAGER_EMULATOR") is "1" or "true" or "True" or "TRUE";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
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

                    await Task.Delay(_config.PollInterval, _cts.Token);
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
        device.OnDisconnect += (_, _) => OnDeviceDisconnected?.Invoke();
        return device;
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask; } catch { }
        }

        try { _rpcDevice?.Disconnect(); } catch { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (_rpcDevice is null) return;

        var agents = await HerdrClient.ListAgentsAsync(ct);

        var threads = new List<Oai.Thread>();

        if (IsEnabled && agents.Count > 0)
        {
            threads.AddRange(StatusMapper.ThreadsFor(agents, _config));
            threads.Add(StatusMapper.StackThread(false, _config));
            threads.Add(StatusMapper.TabCycleThread(_config));
            threads.Add(StatusMapper.LandThread(false, _config));
            threads.Add(StatusMapper.VoiceThread(Pad.VoiceKeyIds[0], false, _config));
            threads.Add(StatusMapper.VoiceThread(Pad.VoiceKeyIds[1], false, _config));
            threads.Add(StatusMapper.MacroThread(9, _config));
            threads.Add(StatusMapper.MacroThread(12, _config));

            var aggregate = StatusMapper.Aggregate(agents, _config);
            var zone = StatusMapper.ZoneFor(aggregate, _config);
            if (zone is { } zoneValue)
            {
                await _rpcDevice.RequestAsync("v.oai.rgbcfg",
                    Oai.RgbConfigParams(zoneValue, Oai.Zone.Dark), ct);
            }
        }
        else
        {
            for (var id = 0; id <= Pad.MaxThreadId; id++)
            {
                threads.Add(new Oai.Thread(id) { Brightness = 0, Effect = Oai.Effect.Off });
            }
            await _rpcDevice.RequestAsync("v.oai.rgbcfg",
                Oai.RgbConfigParams(Oai.Zone.Dark, Oai.Zone.Dark), ct);
        }

        if (threads.Count > 0)
        {
            await _rpcDevice.RequestAsync("v.oai.thstatus",
                Oai.ThreadsParams(threads), ct);
        }

        OnAgentStateChanged?.Invoke(agents);
    }

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
