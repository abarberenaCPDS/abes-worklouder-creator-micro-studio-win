namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;

using System.Text.Json;

/// Client for the Herdr socket API: newline-delimited JSON over the local
/// socket (Unix domain socket on Unix, named pipe on Windows). The server
/// handles exactly one request per connection and then closes, with one
/// exception: `events.subscribe` takes over the stream and pushes events
/// until the client disconnects.
public static class HerdrClient
{
    public static string SocketPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("HERDR_SOCKET_PATH");
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

        var baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(baseDir, "herdr", "herdr.sock");
    }

    /// The distro the WSL bridge runs `nc` in. Unset uses WSL's own default.
    public static string? WslDistro()
    {
        var distro = Environment.GetEnvironmentVariable(StudioEnvironment.WslDistro);
        return string.IsNullOrEmpty(distro) ? null : distro;
    }

    /// An absolute Unix path in `HERDR_SOCKET_PATH` on Windows can only mean a
    /// path *inside* a distro — no Windows Herdr is ever reached that way — so
    /// it selects the WSL bridge instead of a named pipe.
    public static bool IsWslSocketPath(string path) => path.StartsWith('/');

    /// Windows reaches Herdr through a named pipe, or through `wsl.exe` when
    /// pointed at a socket inside WSL. Everywhere else it is a Unix socket.
    public static IHerdrConnection CreateConnection(string path)
    {
        if (!OperatingSystem.IsWindows()) return new UnixHerdrConnection(path);

        return IsWslSocketPath(path)
            ? new StdioHerdrConnection(path, WslDistro())
            : new NamedPipeHerdrConnection(path);
    }

    private static readonly JsonSerializerOptions Snake = HerdrJson.Snake;

    /// Agents in the server's own order — workspace, then tab, then pane —
    /// which is exactly how Herdr's agent panel lists them in grouped mode.
    /// Slot N is element N, so the pad reads like the sidebar. Do not re-sort:
    /// an earlier version ordered by ID strings here, and IDs do not sort the
    /// way the sidebar displays.
    public static async Task<List<HerdrAgent>> ListAgentsAsync(CancellationToken ct = default)
    {
        var result = await RequestAsync<AgentListResult>("agent.list", null, ct);
        return result.Agents;
    }

    /// The agent whose pane has focus in Herdr, if any. Fetched fresh rather
    /// than read off the bridge's poll, since focus is exactly the thing that
    /// changes between polls.
    public static async Task<HerdrAgent?> FocusedAgentAsync(CancellationToken ct = default) =>
        (await ListAgentsAsync(ct)).FirstOrDefault(a => a.Focused);

    public static async Task FocusAgentAsync(string target, CancellationToken ct = default) =>
        await RequestAsync("agent.focus", new { target }, ct);

    private static async Task RequestAsync(string method, object? parameters,
        CancellationToken ct, TimeSpan? timeout = null) =>
        await RequestAsync<JsonElement>(method, parameters, ct, timeout);

    public static async Task<List<HerdrTab>> ListTabsAsync(string? workspaceId = null,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>();
        if (workspaceId is not null) parameters["workspace_id"] = workspaceId;
        var result = await RequestAsync<TabListResult>("tab.list", parameters, ct);
        return result.Tabs;
    }

    public static async Task<List<HerdrWorkspace>> ListWorkspacesAsync(CancellationToken ct = default) =>
        (await RequestAsync<WorkspaceListResult>("workspace.list", null, ct)).Workspaces;

    public static async Task FocusTabAsync(string tabId, CancellationToken ct = default) =>
        await RequestAsync("tab.focus", new { tab_id = tabId }, ct);

    /// Injects key chords into a pane, crossterm-style names ("ctrl+alt+v",
    /// "f13", "enter"). The pane's terminal encodes them as if typed.
    public static async Task SendKeysAsync(string paneId, IList<string> keys, CancellationToken ct = default) =>
        await RequestAsync("pane.send_keys",
            new { pane_id = paneId, keys }, ct);

    /// Types a string into a pane — bracketed-pasted when the pane supports
    /// it, so multi-word text lands as one block and nothing auto-submits.
    public static async Task SendTextAsync(string paneId, string text, CancellationToken ct = default) =>
        await RequestAsync("pane.send_text",
            new { pane_id = paneId, text }, ct);

    /// The pane's viewport, or null when Herdr keeps no scrollback for it.
    ///
    /// Alternate-screen output never reaches the host's scrollback, so a
    /// full-screen TUI reports `max_offset_from_bottom: 0` — there is nothing
    /// here to move, and the app has to be asked to move itself instead.
    public static async Task<PaneScrollInfo?> PaneScrollAsync(string paneId, CancellationToken ct = default)
    {
        var result = await RequestAsync<PaneGetResult>("pane.get", new { pane_id = paneId }, ct);
        return result.Pane?.Scroll;
    }

    /// Moves a pane's viewport, in lines counted from the live bottom: 0 is the
    /// newest output. Exact, and indifferent to what is running in the pane —
    /// which is the point, since it needs no key the application understands.
    public static async Task ScrollPaneAsync(string paneId, int offsetFromBottom,
        CancellationToken ct = default) =>
        await RequestAsync("pane.scroll",
            new { pane_id = paneId, offset_from_bottom = offsetFromBottom }, ct);

    /// Focuses the tab after the focused one in its workspace, wrapping at the
    /// end. Tabs in other workspaces are left alone: cycling is a
    /// within-window gesture, not a window switcher.
    public static async Task CycleTabsAsync(CancellationToken ct = default, int offset = 1)
    {
        var next = AdjacentTab(await ListTabsAsync(ct: ct), offset);
        if (next is not null) await FocusTabAsync(next.TabId, ct);
    }

    /// The tab `CycleTabsAsync` would focus, or nil when there is nothing to
    /// do — no focused tab, or a workspace with a single tab.
    public static HerdrTab? NextTab(List<HerdrTab> tabs)
        => AdjacentTab(tabs, 1);

    public static HerdrTab? AdjacentTab(List<HerdrTab> tabs, int offset)
    {
        var focused = tabs.FirstOrDefault(t => t.Focused);
        if (focused is null) return null;
        var siblings = tabs
            .Where(t => t.WorkspaceId == focused.WorkspaceId)
            .OrderBy(t => t.Number)
            .ToList();
        if (siblings.Count <= 1) return null;
        var index = siblings.IndexOf(focused);
        if (index < 0) return null;
        return siblings[((index + offset) % siblings.Count + siblings.Count) % siblings.Count];
    }

    public static async Task CycleWorkspacesAsync(int offset, CancellationToken ct = default)
    {
        var workspaces = (await ListWorkspacesAsync(ct)).OrderBy(workspace => workspace.Number).ToList();
        if (workspaces.Count <= 1) return;
        var current = workspaces.FindIndex(workspace => workspace.Focused);
        if (current < 0) return;
        var index = ((current + offset) % workspaces.Count + workspaces.Count) % workspaces.Count;
        await RequestAsync("workspace.focus", new { workspace_id = workspaces[index].WorkspaceId }, ct);
    }

    public static async Task FocusPaneAsync(string direction, CancellationToken ct = default) =>
        await RequestAsync("pane.focus_direction", new { direction }, ct);

    public static async Task ZoomPaneAsync(CancellationToken ct = default) =>
        await RequestAsync("pane.zoom", new { mode = "toggle" }, ct);

    public static async Task PromptAgentAsync(string target, string text, CancellationToken ct = default) =>
        await RequestAsync("agent.prompt", new { target, text }, ct);

    public static async Task<string?> CurrentPaneIdAsync(CancellationToken ct = default)
    {
        var result = await RequestAsync<JsonElement>("pane.current", null, ct);
        if (result.ValueKind != JsonValueKind.Object) return null;
        if (result.TryGetProperty("pane_id", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();
        if (result.TryGetProperty("pane", out var pane) && pane.ValueKind == JsonValueKind.Object &&
            pane.TryGetProperty("pane_id", out var nested) && nested.ValueKind == JsonValueKind.String)
            return nested.GetString();
        return null;
    }

    private static int _nextId;
    private static string NextId() => $"wl_{System.Threading.Interlocked.Increment(ref _nextId)}";

    /// What came back was not JSON. A transport tool complaining looks exactly
    /// like this: `wsl.exe` writes its errors to stdout, and writes them as
    /// UTF-16, so every character arrives with a NUL after it.
    private static string TransportGrumble(string line)
    {
        var cleaned = new string(line.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 0 ? cleaned : "the connection produced no JSON";
    }

    private static async Task<TResult> RequestAsync<TResult>(string method, object? parameters,
        CancellationToken ct, TimeSpan? timeout = null)
    {
        var path = SocketPath();
        var connection = CreateConnection(path);

        await using var _ = connection.ConfigureAwait(false);
        var cts = timeout is { } t
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, new CancellationTokenSource(t).Token)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await connection.ConnectAsync(cts.Token).ConfigureAwait(false);
            var envelope = new Dictionary<string, object?>
            {
                ["id"] = NextId(),
                ["method"] = method,
                ["params"] = parameters ?? new Dictionary<string, object?>(),
            };
            await connection.WriteLineAsync(
                JsonSerializer.Serialize(envelope), cts.Token).ConfigureAwait(false);

            await foreach (var line in connection.ReadLinesAsync(cts.Token).ConfigureAwait(false))
            {
                JsonElement document;
                try
                {
                    document = JsonSerializer.Deserialize<JsonElement>(line, Snake);
                }
                catch (JsonException)
                {
                    throw new HerdrException(HerdrError.BadResponse, TransportGrumble(line), path);
                }

                if (document.ValueKind != JsonValueKind.Object) continue;
                if (document.TryGetProperty("error", out var error) &&
                    error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message))
                    throw new HerdrException(HerdrError.Api, message.GetString() ?? "api error", path);
                if (document.TryGetProperty("result", out var result))
                    return JsonSerializer.Deserialize<TResult>(result.GetRawText(), Snake)!;
                return default!;
            }

            throw new HerdrException(HerdrError.Closed,
                $"connection closed before {method} responded", path);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new HerdrException(HerdrError.Timeout, $"timed out waiting for {method}", path);
        }
    }

    /// One subscription, on its own connection. The first line is the
    /// acknowledgement; everything after it is a pushed event.
    public sealed class EventStream : IAsyncDisposable
    {
        public Action? OnReady { get; set; }
        public Action<JsonElement>? OnEvent { get; set; }
        public Action<Exception?>? OnClosed { get; set; }

        private readonly List<Dictionary<string, object?>> _subscriptions;
        private readonly IHerdrConnection _connection;
        private bool _ready;
        private bool _stopped;

        public EventStream(List<Dictionary<string, object?>> subscriptions)
        {
            _subscriptions = subscriptions;
            _connection = CreateConnection(SocketPath());
        }

        public async Task<EventStream> StartAsync(CancellationToken ct = default)
        {
            await _connection.ConnectAsync(ct).ConfigureAwait(false);
            var envelope = new Dictionary<string, object?>
            {
                ["id"] = "wl_sub",
                ["method"] = "events.subscribe",
                ["params"] = new { subscriptions = _subscriptions },
            };
            await _connection.WriteLineAsync(
                JsonSerializer.Serialize(envelope), ct).ConfigureAwait(false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var line in _connection.ReadLinesAsync(ct).ConfigureAwait(false))
                    {
                        if (ct.IsCancellationRequested) break;
                        var document = JsonSerializer.Deserialize<JsonElement>(line, Snake);
                        if (!_ready)
                        {
                            _ready = true;
                            OnReady?.Invoke();
                            continue;
                        }

                        if (document.ValueKind == JsonValueKind.Object)
                            OnEvent?.Invoke(document);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    OnClosed?.Invoke(ex);
                }
                finally
                {
                    if (!_stopped) OnClosed?.Invoke(null);
                }
            }, ct);
            return this;
        }

        public void Stop()
        {
            _stopped = true;
            try { _connection.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { }
        }

        public ValueTask DisposeAsync()
        {
            Stop();
            return ValueTask.CompletedTask;
        }
    }
}
