namespace MicroManager.Core.Herdr;

using System.Text.Json;

/// A Herdr agent, as reported by `agent.list`. The server's own order —
/// workspace, then tab, then pane — is how Herdr's agent panel lists them in
/// grouped mode, so slot N is element N. Do not re-sort.
public sealed record HerdrAgent(
    string? TerminalId,
    string? PaneId,
    string? TabId,
    string? WorkspaceId,
    string Agent,
    string AgentStatus,
    string? Cwd,
    string? ForegroundCwd,
    bool Focused)
{
    /// The target to hand to `agent.focus`, which resolves pane ids only.
    public string? FocusTarget => PaneId ?? TerminalId;

    /// Where the agent is actually working. `foreground_cwd` follows a `cd`
    /// inside the pane; `cwd` is only where the pane started.
    public string WorkingDirectory =>
        !string.IsNullOrEmpty(ForegroundCwd) ? ForegroundCwd : Cwd ?? string.Empty;

    public string ShortName =>
        string.IsNullOrEmpty(WorkingDirectory) ? Agent : Path.GetFileName(WorkingDirectory);
}

public sealed record HerdrTab(
    string TabId,
    string WorkspaceId,
    int Number,
    bool Focused);

public sealed class AgentListResult
{
    public List<HerdrAgent> Agents { get; set; } = new();
}

public sealed class AgentGetResult
{
    public HerdrAgent? Agent { get; set; }
}

public sealed class TabListResult
{
    public List<HerdrTab> Tabs { get; set; } = new();
}

public static class HerdrJson
{
    /// Herdr's wire names are snake_case.
    public static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

public enum HerdrError
{
    CannotConnect,
    Timeout,
    Api,
    Closed,
    BadResponse
}

public sealed class HerdrException : Exception
{
    public HerdrError Error { get; }
    public string? Path { get; }
    public HerdrException(HerdrError error, string message, string? path = null) : base(message)
    {
        Error = error;
        Path = path;
    }
}
