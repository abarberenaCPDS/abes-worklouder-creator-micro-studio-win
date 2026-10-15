using System.Runtime.CompilerServices;
using System.Net.Sockets;

namespace MicroManager.Core.Herdr;

/// Unix domain socket connection (macOS / Linux).
public sealed class UnixHerdrConnection : IHerdrConnection
{
    private readonly string _path;
    private Socket? _socket;
    private Stream? _stream;
    private StreamReader? _reader;

    public UnixHerdrConnection(string path) => _path = path;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var endPoint = new UnixDomainSocketEndPoint(_path);
        await _socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        _stream = new NetworkStream(_socket, ownsSocket: true);
        _reader = new StreamReader(_stream);
    }

    public Task WriteLineAsync(string json, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("not connected");
        _writer.WriteLine(json);
        return _writer.FlushAsync(cancellationToken);
    }

    private StreamWriter? _writer;

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_reader is null) throw new InvalidOperationException("not connected");
        string? line;
        while ((line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { } line2)
            yield return line2;
    }

    public ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        _stream?.Dispose();
        _socket?.Dispose();
        return ValueTask.CompletedTask;
    }
}
