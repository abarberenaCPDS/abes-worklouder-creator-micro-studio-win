using MicroManager.Core.Config;
using MicroManager.Core.Emulator;
using MicroManager.Core.Herdr;
using MicroManager.Core.Keymap;
using MicroManager.Core.Pad;
using MicroManager.Core.Transport;

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
    private readonly RpcDevice _rpcDevice;
    private readonly BridgeConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pollTask;
    private bool _disposed;

    public bool IsConnected => _rpcDevice.IsConnected;
    public bool IsEnabled { get; set; } = true;

    public event Action<List<HerdrAgent>>? OnAgentStateChanged;
    public event Action? OnDeviceConnected;
    public event Action? OnDeviceDisconnected;
    public event Action<string>? OnError;

    public BridgeService()
    {
        var emulator = new PadEmulator();
        var transport = (IWlTransport)emulator;
        _rpcDevice = new RpcDevice(transport);
        _config = new BridgeConfig();

        _rpcDevice.OnDisconnect += (_, _) =>
        {
            OnDeviceDisconnected?.Invoke();
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _rpcDevice.ConnectAsync(cancellationToken);
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

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollTask is not null)
        {
            try { await _pollTask; } catch { }
        }

        try { _rpcDevice.Disconnect(); } catch { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
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
        _rpcDevice.Disconnect();
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
            await _rpcDevice.DisposeAsync();
            _disposed = true;
        }
    }
}
