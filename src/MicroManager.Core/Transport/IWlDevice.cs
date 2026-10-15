using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace MicroManager.Core.Transport;

/// Raw-HID JSON-RPC transport for Work Louder devices (vendor collection,
/// usage page 0xFF00 / usage 1).
///
/// Framing, on 64-byte reports carried by the vendor collection:
///
///     byte 0      report id, always 0x06
///     byte 1      channel: 1 = firmware debug log, 2 = JSON-RPC
///     byte 2      payload length in this report, max 61
///     bytes 3..   UTF-8 fragment of the JSON message
///
/// Messages are split across as many reports as needed and reassembled by
/// scanning for balanced top-level braces.
public interface IWlDevice
{
    string? Product { get; }
    int? ProductId { get; }
    string? Transport { get; }
    string? Serial { get; }
    bool IsConnected { get; }

    event EventHandler<(string Method, object? Parameters, int Id)>? OnTx;
    event EventHandler<JsonRpcResponse>? OnResponse;
    event EventHandler<(string Method, object? Parameters)>? OnNotification;
    event EventHandler<string>? OnDeviceLog;
    event EventHandler<string>? OnWriteError;
    event EventHandler<string>? OnDisconnect;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect(string? reason = null);

    /// Sends a JSON-RPC request and awaits its response. Firmware only accepts
    /// call ids below 1000.
    Task<JsonRpcResponse> RequestAsync(string method, object? parameters,
        CancellationToken cancellationToken = default);

    /// Sends a JSON-RPC request without awaiting a response.
    Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken = default);
}

public readonly record struct JsonRpcResponse(int Id, object? Result, string? Error);

public static class WireProtocol
{
    public const byte ReportId = 0x06;
    public const byte ChannelRpc = 2;
    public const byte ChannelDebug = 1;
    public const int MaxChunk = 61;
    public const int ReportSize = 64;

    /// Splits a JSON payload into 64-byte output reports on the RPC channel.
    public static byte[][] EncodeReports(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var reports = new List<byte[]>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var chunk = Math.Min(MaxChunk, payload.Length - offset);
            var report = new byte[ReportSize];
            report[0] = ReportId;
            report[1] = ChannelRpc;
            report[2] = (byte)chunk;
            Array.Copy(payload, offset, report, 3, chunk);
            reports.Add(report);
            offset += chunk;
        }

        return reports.Count > 0 ? reports.ToArray() : new[] { EmptyReport(ChannelRpc) };
    }

    private static byte[] EmptyReport(byte channel) => new byte[] { ReportId, channel, 0 };

    /// Reassembles fragmented JSON-RPC messages from input reports. Call once
    /// per input report; returns any complete top-level JSON objects found.
    public sealed class Reassembler
    {
        private readonly StringBuilder _buffer = new();

        public IEnumerable<string> CompleteMessages(byte[] report)
        {
            // The report id may be delivered out-of-band by the stack, in
            // which case the channel byte is at index 0; tolerate both.
            var start = report.Length > 2 && report[0] == ReportId ? 1 : 0;
            if (report.Length <= start + 2) return Array.Empty<string>();

            var channel = report[start];
            var length = report[start + 1];
            if (channel != ChannelRpc && channel != ChannelDebug) return Array.Empty<string>();
            if (length == 0 || length > MaxChunk || report.Length < start + 2 + length)
                return Array.Empty<string>();

            var text = Encoding.UTF8.GetString(report, start + 2, (int)length);

            if (channel == ChannelDebug)
            {
                _buffer.Append(text);
                return FlushLines();
            }

            _buffer.Append(text);
            return ExtractJsonObjects();
        }

        private List<string> FlushLines()
        {
            var messages = new List<string>();
            while (true)
            {
                var nl = _buffer.ToString().IndexOfAny(new[] { '\n', '\r' });
                if (nl < 0) break;
                var line = _buffer.ToString(0, nl).Trim();
                _buffer.Remove(0, nl + 1);
                if (line.Length > 0) messages.Add(line);
                if (_buffer.Length > 4096) _buffer.Clear();
            }

            return messages;
        }

        private List<string> ExtractJsonObjects()
        {
            var messages = new List<string>();
            var text = _buffer.ToString();
            var depth = 0;
            var start = -1;
            var inString = false;
            var escaped = false;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                }
                else if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (ch == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && start >= 0)
                        {
                            messages.Add(text[start..(i + 1)]);
                        }
                    }
                }
            }

            if (depth == 0) _buffer.Clear();
            else if (start >= 0) _buffer.Remove(0, start);

            return messages;
        }
    }
}
