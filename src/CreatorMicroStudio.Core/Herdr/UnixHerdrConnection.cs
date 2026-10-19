using System.Runtime.CompilerServices;
using System.Net.Sockets;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

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
        _writer = new StreamWriter(_stream) { AutoFlush = true, NewLine = "\n" };
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
        // Disposing the reader closes the socket underneath, so the writer's
        // parting flush throws; that must not replace the error being unwound.
        try { _writer?.Dispose(); } catch (Exception) { }
        try { _reader?.Dispose(); } catch (Exception) { }
        try { _stream?.Dispose(); } catch (Exception) { }
        try { _socket?.Dispose(); } catch (Exception) { }
        return ValueTask.CompletedTask;
    }
}
