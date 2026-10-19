namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using System.IO.Pipes;
using System.Text;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

/// The Windows transport is chosen from the *shape* of the socket path, so
/// these pin the rule down. The live test needs a Herdr server in WSL and
/// skips itself without one.
public class HerdrWslTests
{
    [Fact]
    public void AbsoluteUnixPathSelectsTheWslBridge()
    {
        Assert.True(HerdrClient.IsWslSocketPath("/home/abe/.config/herdr/herdr.sock"));
    }

    [Fact]
    public void NamedPipeAndWindowsPathsDoNot()
    {
        Assert.False(HerdrClient.IsWslSocketPath(@"\\.\pipe\herdr"));
        Assert.False(HerdrClient.IsWslSocketPath(@"C:\Users\abe\.config\herdr\herdr.sock"));
        Assert.False(HerdrClient.IsWslSocketPath("herdr"));
    }

    [Fact]
    public void TheBridgeRefusesAPathThatNeedsAShell()
    {
        // `nc -U` gets the path as an argument, so nothing would expand `~`.
        var error = Assert.Throws<ArgumentException>(
            () => new StdioHerdrConnection("~/.config/herdr/herdr.sock"));
        Assert.Contains("absolute", error.Message);
    }

    [Fact]
    public void TheBridgeTakesAnAbsolutePathAndAnOptionalDistro()
    {
        var connection = new StdioHerdrConnection("/home/abe/.config/herdr/herdr.sock");
        Assert.NotNull(connection);
        Assert.NotNull(new StdioHerdrConnection("/tmp/herdr.sock", "AlmaLinux-9"));
    }

    [Fact]
    public void TheBridgeNeverOpensAConsoleWindow()
    {
        var startInfo = StdioHerdrConnection.StartInfoFor(
            "/home/abe/.config/herdr/herdr.sock", "AlmaLinux-9");

        // The app is a GUI process with no console, so a console child that is
        // allowed one gets its own window — a flash of black per request, which
        // on the key path means a flash per key press.
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);

        Assert.Equal("wsl.exe", startInfo.FileName);
        Assert.Equal(
            new[] { "-d", "AlmaLinux-9", "--", "nc", "-U", "/home/abe/.config/herdr/herdr.sock" },
            startInfo.ArgumentList);
    }

    [Fact]
    public void AnUnsetDistroLeavesItToWsl()
    {
        var startInfo = StdioHerdrConnection.StartInfoFor("/tmp/herdr.sock", null);
        Assert.Equal(new[] { "--", "nc", "-U", "/tmp/herdr.sock" }, startInfo.ArgumentList);
    }

    /// Live WSL path lookup, but does not call or mutate the Herdr server.
    [Fact]
    public async Task HerdrCliTildePathResolvesInsideTheSelectedDistro()
    {
        if (!OperatingSystem.IsWindows()) return;
        var socketPath = Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH");
        if (string.IsNullOrEmpty(socketPath) || !HerdrClient.IsWslSocketPath(socketPath)) return;

        var path = await HerdrCommandRunner.ResolveWslBinaryPathAsync("~/.local/bin/herdr", HerdrClient.WslDistro());
        Assert.EndsWith("/.local/bin/herdr", path, StringComparison.Ordinal);
    }

    /// Live: skips unless Windows is pointed at a socket inside WSL. Run with
    /// HERDR_SOCKET_PATH=/home/<you>/.config/herdr/herdr.sock
    /// (and MICROMANAGER_WSL_DISTRO=<name> if that is not the default distro).
    [Fact]
    public async Task LiveAgentListOverTheWslBridge()
    {
        if (!OperatingSystem.IsWindows()) return;

        var path = Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH");
        if (string.IsNullOrEmpty(path) || !HerdrClient.IsWslSocketPath(path)) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var agents = await HerdrClient.ListAgentsAsync(cts.Token);

        Assert.NotEmpty(agents);
        // The fields the bridge and the pad both depend on.
        Assert.All(agents, agent =>
        {
            Assert.False(string.IsNullOrEmpty(agent.Agent));
            Assert.False(string.IsNullOrEmpty(agent.AgentStatus));
            Assert.False(string.IsNullOrEmpty(agent.FocusTarget));
        });
        Assert.Contains(agents, agent => agent.Focused);
    }

    /// Live, and the failure path: a socket that is not there must say why
    /// rather than timing out.
    [Fact]
    public async Task LiveMissingSocketReportsCannotConnect()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH"))) return;

        var connection = new StdioHerdrConnection("/tmp/definitely-not-here/herdr.sock",
            HerdrClient.WslDistro());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await connection.ConnectAsync(cts.Token);
        await using var _ = connection;

        var error = await Assert.ThrowsAsync<HerdrException>(async () =>
        {
            await foreach (var _ in connection.ReadLinesAsync(cts.Token)) { }
        });

        Assert.Equal(HerdrError.CannotConnect, error.Error);
    }

    /// A transport that complains instead of speaking the protocol must reach
    /// the user as its own words. This reproduces `wsl.exe` exactly: its errors
    /// go to *stdout*, encoded UTF-16, so the text arrives with a NUL after
    /// every character. No WSL needed — a named pipe stands in.
    [Fact]
    public async Task ATransportThatComplainsBecomesReadableText()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pipeName = $"micromanager-junk-{Guid.NewGuid():N}";
        var previous = Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH");
        Environment.SetEnvironmentVariable("HERDR_SOCKET_PATH", $@"\\.\pipe\{pipeName}");

        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var accepted = server.WaitForConnectionAsync(cts.Token);
            var pending = HerdrClient.ListAgentsAsync(cts.Token);
            await accepted;

            // Drain the request first: a pipe Flush waits for the peer to read,
            // so a server that only writes deadlocks against the client.
            using var requests = new StreamReader(server, Encoding.UTF8);
            var request = await requests.ReadLineAsync(cts.Token);
            Assert.Contains("agent.list", request);

            // No Flush here: on a pipe server it waits for the client to drain
            // every byte, and the trailing NUL of the UTF-16 newline never is.
            await server.WriteAsync(Encoding.Unicode.GetBytes(
                "There is no distribution with the supplied name.\n"), cts.Token);

            var error = await Assert.ThrowsAsync<HerdrException>(() => pending);
            Assert.Equal(HerdrError.BadResponse, error.Error);
            Assert.Contains("distribution", error.Message);
            Assert.DoesNotContain('\0', error.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HERDR_SOCKET_PATH", previous);
            await server.DisposeAsync();
        }
    }
}
