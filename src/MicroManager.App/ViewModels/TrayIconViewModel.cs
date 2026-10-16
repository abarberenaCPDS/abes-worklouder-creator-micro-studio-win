using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;
using MicroManager.Core.Herdr;
using MicroManager.App.Services;

namespace MicroManager.App.ViewModels;

public class TrayIconViewModel : INotifyPropertyChanged
{
    private readonly IBridgeService _bridge;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrayIconViewModel(IBridgeService bridge)
    {
        _bridge = bridge;
        _bridge.OnAgentStateChanged += agents => UpdateAgents(agents);
        _bridge.OnDeviceConnected += OnDeviceConnected;
        _bridge.OnDeviceDisconnected += OnDeviceDisconnected;
        _bridge.OnError += OnError;
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; _bridge.IsEnabled = value; OnPropertyChanged(); }
    }

    private string _statusText = "Disconnected";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<AgentViewModel> Agents { get; } = new();

    public void UpdateAgents(List<HerdrAgent> agents)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var currentIds = new HashSet<string?>(agents.Select(a => a.PaneId ?? a.TerminalId));
            var toRemove = Agents.Where(a => !currentIds.Contains(a.PaneId)).ToList();
            foreach (var a in toRemove) Agents.Remove(a);

            foreach (var agent in agents)
            {
                var paneId = agent.PaneId ?? agent.TerminalId ?? string.Empty;
                var existing = Agents.FirstOrDefault(a => a.PaneId == paneId);
                if (existing is not null)
                {
                    existing.Status = agent.AgentStatus;
                    existing.ShortName = agent.ShortName;
                    existing.Focused = agent.Focused;
                }
                else
                {
                    Agents.Add(new AgentViewModel
                    {
                        PaneId = paneId,
                        AgentName = agent.Agent,
                        Status = agent.AgentStatus,
                        ShortName = agent.ShortName,
                        Focused = agent.Focused,
                        FocusAction = () => FocusAgent(agent)
                    });
                }
            }
        });
    }

    private void OnDeviceConnected()
    {
        IsConnected = true;
        StatusText = "Connected";
    }

    private void OnDeviceDisconnected()
    {
        IsConnected = false;
        StatusText = "Disconnected";
        Agents.Clear();
    }

    private void OnError(string error)
    {
        StatusText = $"Error: {error}";
    }

    private async void FocusAgent(HerdrAgent agent)
    {
        var target = agent.PaneId ?? agent.TerminalId;
        if (target is not null)
        {
            try { await HerdrClient.FocusAgentAsync(target); }
            catch { }
        }
    }

    public void ToggleEnabled()
    {
        IsEnabled = !IsEnabled;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class AgentViewModel : INotifyPropertyChanged
{
    public string PaneId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public Action FocusAction { get; set; } = () => { };

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private string _shortName = string.Empty;
    public string ShortName
    {
        get => _shortName;
        set { _shortName = value; OnPropertyChanged(); }
    }

    private bool _focused;
    public bool Focused
    {
        get => _focused;
        set { _focused = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
