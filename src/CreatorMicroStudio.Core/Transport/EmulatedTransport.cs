namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

using System.Text;
using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Emulator;

/// Emulated raw-HID transport: decodes client request reports, asks the
/// `PadEmulator`, and raises the response as input reports. The bridge's RPC
/// layer cannot tell the difference from real hardware.
public sealed class EmulatedTransport : IWlTransport
{
    private readonly PadEmulator _emulator;

    public string? Product => _emulator.Product;
    public int? ProductId => _emulator.ProductId;
    public string? Transport => _emulator.Transport;
    public string? Serial => _emulator.Serial;
    public bool IsConnected => _emulator.IsConnected;

    public event EventHandler<byte[]>? OnReportReceived;
    public event EventHandler<string>? OnDeviceLog;
    public event EventHandler<string>? OnDisconnect;

    public EmulatedTransport(PadEmulator emulator)
    {
        _emulator = emulator;
        // Key notifications are pushed as input reports by the emulator itself;
        // forward them so a wrapped pad reaches the RPC layer like a directly
        // wired one does.
        _emulator.OnReportReceived += (_, report) => OnReportReceived?.Invoke(this, report);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _emulator.ConnectAsync(cancellationToken);

    public void Disconnect() => _emulator.Disconnect();

    public async Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        var text = DecodeReport(report);
        if (text is null) return;

        JsonElement request;
        try { request = JsonSerializer.Deserialize<JsonElement>(text)!; }
        catch { return; }

        var method = request.GetProperty("method").GetString()!;
        var id = request.GetProperty("id").GetInt32();
        var parameters = request.TryGetProperty("params", out var p) ? p : default;

        // A real device is never synchronous; answer on the next turn so
        // callers that assume synchrony would deadlock on hardware.
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return;

        var (resultJson, error) = _emulator.Handle(method, parameters);
        var response = error is null
            ? $"{{\"id\":{id},\"result\":{resultJson}}}"
            : $"{{\"id\":{id},\"error\":{{\"message\":{JsonSerializer.Serialize(error)}}}}}";

        foreach (var outReport in WireProtocol.EncodeReports(response))
            OnReportReceived?.Invoke(this, outReport);
    }

    private static string? DecodeReport(byte[] report)
    {
        var start = report.Length > 2 && report[0] == WireProtocol.ReportId ? 1 : 0;
        if (report.Length <= start + 2) return null;
        var channel = report[start];
        var length = report[start + 1];
        if (channel != WireProtocol.ChannelRpc || length == 0 || length > WireProtocol.MaxChunk ||
            report.Length < start + 2 + length)
            return null;
        return Encoding.UTF8.GetString(report, start + 2, (int)length);
    }
}
