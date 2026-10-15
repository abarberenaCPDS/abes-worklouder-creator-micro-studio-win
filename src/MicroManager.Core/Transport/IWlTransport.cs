namespace MicroManager.Core.Transport;

/// Low-level raw-HID transport: connect/disconnect and send/receive reports.
public interface IWlTransport
{
    string? Product { get; }
    int? ProductId { get; }
    string? Transport { get; }
    string? Serial { get; }
    bool IsConnected { get; }

    event EventHandler<byte[]>? OnReportReceived;
    event EventHandler<string>? OnDeviceLog;
    event EventHandler<string>? OnDisconnect;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect();
    Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default);
}
