using System.Text.Json;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Keymap;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Transport;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

public sealed class KeymapReadResponseTests
{
    private sealed class FirmwareEnvelopeTransport(string keymapJson) : IWlTransport
    {
        private readonly WireProtocol.Reassembler _requests = new();
        public string? Product => "fixture";
        public int? ProductId => null;
        public string? Transport => "fixture";
        public string? Serial => null;
        public bool IsConnected { get; private set; }
        public event EventHandler<byte[]>? OnReportReceived;
        public event EventHandler<string>? OnDeviceLog;
        public event EventHandler<string>? OnDisconnect;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default)
        {
            foreach (var message in _requests.CompleteMessages(report))
            {
                using var request = JsonDocument.Parse(message);
                var id = request.RootElement.GetProperty("id").GetInt32();
                // Firmware response envelope: method + params + id. The fs.read
                // data value is itself the JSON string stored on the pad.
                var parameters = JsonSerializer.Serialize(new { data = keymapJson });
                var response = $"{{\"method\":\"fs.read\",\"params\":{parameters},\"id\":{id}}}";
                foreach (var input in WireProtocol.EncodeReports(response))
                    OnReportReceived?.Invoke(this, input);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FsReadAcceptsTheFirmwareMethodParamsIdResponseEnvelope()
    {
        const string keymap = "{\"activeProfileId\":12,\"profiles\":[{\"id\":12,\"layers\":[]}] }";
        await using var device = new RpcDevice(new FirmwareEnvelopeTransport(keymap));
        await device.ConnectAsync();

        var parsed = await KeymapManager.ReadAsync(device);

        Assert.Equal(12, parsed["activeProfileId"]!.GetValue<int>());
        Assert.Equal(12, parsed["profiles"]![0]!["id"]!.GetValue<int>());
    }
}
