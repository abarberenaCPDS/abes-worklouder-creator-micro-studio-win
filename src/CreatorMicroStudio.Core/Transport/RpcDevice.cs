namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

using System.Text.Json;
using System.Text.Json.Nodes;

/// JSON-RPC client layered on top of a raw-HID transport. Handles call ids
/// (kept below 1000 per firmware), the pending-call table, timeouts, and the
/// abbreviated notification envelopes (`{"m": ..., "p": ...}`) the device
/// pushes, plus full-envelope responses (`{"id", "result", "error"}`).
public class RpcDevice : IWlDevice, IAsyncDisposable
{
    private readonly IWlTransport _transport;
    private readonly WireProtocol.Reassembler _reassembler = new();
    private readonly Dictionary<int, PendingCall> _pending = new();
    private int _nextId = 1;

    /// One message can be several reports, so two callers sending at once would
    /// interleave their chunks into two unparseable frames. Only the send is
    /// gated, never the wait for a reply, so a slow call cannot stall a fast one.
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private sealed class PendingCall
    {
        public TaskCompletionSource<JsonRpcResponse> Tcs { get; }
        public CancellationTokenSource Cts { get; }
        public PendingCall(TaskCompletionSource<JsonRpcResponse> tcs, CancellationTokenSource cts)
        {
            Tcs = tcs;
            Cts = cts;
        }
    }

    public string? Product => _transport.Product;
    public int? ProductId => _transport.ProductId;
    public string? Transport => _transport.Transport;
    public string? Serial => _transport.Serial;
    public bool IsConnected => _transport.IsConnected;

    public event EventHandler<(string Method, object? Parameters, int Id)>? OnTx;
    public event EventHandler<JsonRpcResponse>? OnResponse;
    public event EventHandler<(string Method, object? Parameters)>? OnNotification;
    public event EventHandler<string>? OnDeviceLog;
    public event EventHandler<string>? OnWriteError;
    public event EventHandler<string>? OnDisconnect;

    public RpcDevice(IWlTransport transport)
    {
        _transport = transport;
        _transport.OnReportReceived += (_, report) => OnReportReceived(report);
        _transport.OnDeviceLog += (_, log) => OnDeviceLog?.Invoke(this, log);
        _transport.OnDisconnect += (_, reason) => OnDisconnect?.Invoke(this, reason);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _transport.ConnectAsync(cancellationToken);

    public void Disconnect(string? reason = null)
    {
        _transport.Disconnect();
        lock (_pending)
        {
            foreach (var pending in _pending.Values)
            {
                pending.Cts.Cancel();
                pending.Tcs.TrySetException(new DeviceDisconnectedException(reason ?? "disconnected"));
            }
            _pending.Clear();
        }
        if (reason is not null) OnDisconnect?.Invoke(this, reason);
    }

    public async Task<JsonRpcResponse> RequestAsync(string method, object? parameters,
        CancellationToken cancellationToken = default)
    {
        if (!_transport.IsConnected)
            throw new DeviceNotConnectedException();

        var id = NextId();
        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new PendingCall(tcs, linked);
        lock (_pending) _pending[id] = pending;

        try
        {
            OnTx?.Invoke(this, (Method: method, Parameters: parameters, Id: id));
            var message = new Dictionary<string, object?> { ["method"] = method, ["id"] = id };
            message["params"] = parameters ?? new Dictionary<string, object?>();
            await SendReportsAsync(
                JsonSerializer.Serialize(message), linked.Token).ConfigureAwait(false);

            using var registration = linked.Token.Register(() =>
                tcs.TrySetCanceled(linked.Token));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_pending) _pending.Remove(id);
            pending.Cts.Dispose();
        }
    }

    public Task NotifyAsync(string method, object? parameters,
        CancellationToken cancellationToken = default)
    {
        if (!_transport.IsConnected)
            throw new DeviceNotConnectedException();

        var id = NextId();
        OnTx?.Invoke(this, (method, parameters, id));
        var message = new Dictionary<string, object?> { ["method"] = method, ["id"] = id };
        message["params"] = parameters ?? new Dictionary<string, object?>();
        var json = JsonSerializer.Serialize(message);
        return SendReportsAsync(json, cancellationToken);
    }

    private async Task SendReportsAsync(string json, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var report in WireProtocol.EncodeReports(json))
                await _transport.SendReportAsync(report, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private int NextId()
    {
        lock (_pending)
        {
            var id = _nextId;
            _nextId = _nextId % 998 + 1;
            return id;
        }
    }

    private void OnReportReceived(byte[] report)
    {
        foreach (var message in _reassembler.CompleteMessages(report))
        {
            try
            {
                ProcessMessage(message);
            }
            catch
            {
                // Malformed frames are dropped rather than fatal — the
                // firmware can emit noise between valid messages.
            }
        }
    }

    private void ProcessMessage(string json)
    {
        JsonElement document;
        JsonElement methodProp2 = default;
        try { document = JsonSerializer.Deserialize<JsonElement>(json)!; }
        catch { return; }

        if (document.ValueKind != JsonValueKind.Object) return;

        if (document.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
        {
            var id = idProp.GetInt32();
            string? error = null;
            object? result = null;
            if (document.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                error = errorProp.GetProperty("message").GetString() ?? "api error";
            // The Creator Micro firmware returns `{method, params, id}` while
            // JSON-RPC libraries commonly use `{result, id}`. Accept both:
            // `params` is the firmware's return payload, not a notification
            // when the response carries an id.
            if (document.TryGetProperty("result", out var resultProp))
                result = resultProp.ValueKind == JsonValueKind.Null ? null : (object)resultProp;
            else if (document.TryGetProperty("params", out var responseParams) &&
                     responseParams.ValueKind != JsonValueKind.Null)
                result = responseParams;

            JsonRpcResponse response;
            if (result is JsonElement je) response = new JsonRpcResponse(id, je.GetRawText(), error);
            else response = new JsonRpcResponse(id, result, error);

            OnResponse?.Invoke(this, new JsonRpcResponse(id, response.Result, response.Error));

            lock (_pending)
            {
                if (_pending.TryGetValue(id, out var pending))
                {
                    _pending.Remove(id);
                    if (error is not null) pending.Tcs.TrySetException(new HerdrApiException(error));
                    else pending.Tcs.TrySetResult(response);
                }
            }
        }
        else if (document.TryGetProperty("m", out var methodProp) ||
            document.TryGetProperty("method", out methodProp2))
        {
            var method = (methodProp.ValueKind == JsonValueKind.String ? methodProp : methodProp2).GetString()!;
            object? parameters = null;
            if (document.TryGetProperty("p", out var pProp) && pProp.ValueKind != JsonValueKind.Null)
                parameters = pProp;
            else if (document.TryGetProperty("params", out var paramsProp) && paramsProp.ValueKind != JsonValueKind.Null)
                parameters = paramsProp;

            (string Method, object? Parameters) notification = (method, (object?)parameters);
            OnNotification?.Invoke(this, notification);
        }
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }
}

public sealed class DeviceNotConnectedException : Exception { }
public sealed class DeviceDisconnectedException(string message) : Exception(message);
public sealed class HerdrApiException(string message) : Exception(message);
