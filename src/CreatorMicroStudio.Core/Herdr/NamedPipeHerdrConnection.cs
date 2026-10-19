using System.Runtime.CompilerServices;
using System.IO.Pipes;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

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
            // Herdr's framing is newline-delimited; the Windows default is CRLF.
            _writer = new StreamWriter(_pipe!) { AutoFlush = true, NewLine = "\n" };
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
        // The order matters, and so does the swallowing. Both text wrappers were
        // built over the pipe without leaveOpen, so disposing the reader closes
        // it; flushing the writer after that throws, and that exception would
        // replace whatever we were unwinding — an error from the server, say.
        try { _writer?.Dispose(); } catch (Exception) { }
        try { _reader?.Dispose(); } catch (Exception) { }
        try { _pipe?.Dispose(); } catch (Exception) { }
        return ValueTask.CompletedTask;
    }
}
