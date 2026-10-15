using System.Runtime.CompilerServices;
using System.IO.Pipes;

namespace MicroManager.Core.Herdr;

/// Named pipe connection (Windows). Herdr serves its API on a named pipe
/// rather than a Unix domain socket.
public sealed class NamedPipeHerdrConnection : IHerdrConnection
{
    private readonly string _serverName;
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public NamedPipeHerdrConnection(string path)
    {
        var prefix = @"\\.\pipe\";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            _pipeName = path[prefix.Length..];
            _serverName = ".";
        }
        else
        {
            _pipeName = path;
            _serverName = ".";
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _pipe = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        return _pipe.ConnectAsync(cancellationToken).ContinueWith(_ =>
        {
            _reader = new StreamReader(_pipe!);
            _writer = new StreamWriter(_pipe!) { AutoFlush = true };
        }, cancellationToken);
    }

    public Task WriteLineAsync(string json, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("not connected");
        _writer.WriteLine(json);
        return _writer.FlushAsync(cancellationToken);
    }

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
        _writer?.Dispose();
        _pipe?.Dispose();
        return ValueTask.CompletedTask;
    }
}
