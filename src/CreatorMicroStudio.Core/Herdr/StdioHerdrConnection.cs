using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

/// A Herdr server inside WSL, reached over `wsl.exe`'s stdio.
///
/// The port's Windows transport is a named pipe, but a Herdr server running in
/// WSL listens on a *Linux* Unix domain socket, which no Windows process can
/// open: the socket lives inside the WSL VM, and `HERDR_SOCKET_PATH` only
/// overrides the path string, not the transport. The one thing that does cross
/// that boundary is `wsl.exe`'s stdio, so this runs `nc -U <socket>` inside the
/// distro and speaks the protocol over the child's pipes. Nothing has to be
/// left running on the Linux side and no port is opened.
///
/// This costs a process spawn per request. That is acceptable because Herdr
/// serves exactly one request per connection — the server closes the socket
/// after replying, so there is no connection to keep for a second call.
///
/// Requires an `nc` with `-U` (nmap-ncat `nc` provides it) in the distro.
public sealed class StdioHerdrConnection : IHerdrConnection
{
    private readonly string _socketPath;
    private readonly string? _distro;
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;

    /// The path is resolved inside the distro, so it must be absolute there.
    /// `~` is expanded by a shell, and this runs no shell.
    public StdioHerdrConnection(string socketPath, string? distro = null)
    {
        if (!socketPath.StartsWith('/'))
            throw new ArgumentException(
                $"the WSL socket path must be absolute inside the distro, not '{socketPath}'",
                nameof(socketPath));

        _socketPath = socketPath;
        _distro = distro;
    }

    /// The command that opens the socket. Split out so a test can hold the flags
    /// still — `CreateNoWindow` above all. The app is a GUI process with no
    /// console, so a console child left to allocate one gets a window of its own,
    /// which is a flash of black on **every** request: every key press, and the
    /// poll behind it.
    public static ProcessStartInfo StartInfoFor(string socketPath, string? distro)
    {
        var startInfo = new ProcessStartInfo("wsl.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // The child is a Linux process passing UTF-8 through a byte pipe.
            // Left to itself this would follow the Windows console code page
            // and mangle anything that is not ASCII.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        if (!string.IsNullOrEmpty(distro))
        {
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(distro);
        }

        // No shell, so nothing here needs quoting.
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("nc");
        startInfo.ArgumentList.Add("-U");
        startInfo.ArgumentList.Add(socketPath);

        return startInfo;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _process = Process.Start(StartInfoFor(_socketPath, _distro));
        }
        catch (Exception ex)
        {
            throw new HerdrException(HerdrError.CannotConnect,
                $"could not run wsl.exe: {ex.Message}", _socketPath);
        }

        if (_process is null)
            throw new HerdrException(HerdrError.CannotConnect, "could not run wsl.exe", _socketPath);

        _reader = _process.StandardOutput;
        _writer = _process.StandardInput;
        // Herdr's framing is newline-delimited; the default on Windows is CRLF.
        _writer.NewLine = "\n";
        _writer.AutoFlush = true;
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(string json, CancellationToken cancellationToken = default)
    {
        if (_writer is null) throw new InvalidOperationException("not connected");
        return _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_reader is null) throw new InvalidOperationException("not connected");

        string? line;
        while ((line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is { } line2)
            yield return line2;

        // stdout closed without a usable reply: the socket is usually missing
        // because Herdr is not running, and "connection closed" would hide that.
        if (await FailureAsync(cancellationToken).ConfigureAwait(false) is { } failure)
            throw new HerdrException(HerdrError.CannotConnect, failure, _socketPath);
    }

    /// What `nc` complained about, if it exited non-zero. Bounded, because the
    /// process is expected to be gone already and the caller's budget is not.
    private async Task<string?> FailureAsync(CancellationToken cancellationToken)
    {
        if (_process is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (_process.ExitCode == 0) return null;

        string stderr;
        try
        {
            stderr = await _process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            stderr = string.Empty;
        }

        // wsl.exe has historically written UTF-16 to stderr, which arrives here
        // as NUL-riddled text; drop them rather than showing escape soup.
        stderr = stderr.Replace("\0", string.Empty).Trim();
        return stderr.Length > 0 ? stderr : $"nc exited with code {_process.ExitCode}";
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            // The Linux process goes on the far side of the distro boundary, so
            // this is best-effort: `nc` also exits on its own once the server
            // closes the socket.
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone, or not ours to kill.
        }

        // Same reason as the pipe: closing stdin under a writer that flushes on
        // the way out throws, and that would mask the real error.
        try { _writer?.Dispose(); } catch (Exception) { }
        try { _reader?.Dispose(); } catch (Exception) { }
        _process?.Dispose();
        return ValueTask.CompletedTask;
    }
}
