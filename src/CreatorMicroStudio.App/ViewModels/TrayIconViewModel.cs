using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.Maui.ApplicationModel;
using AbesWorkLouderCreatorMicroStudio.Windows.Core;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;

namespace AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

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
        _bridge.OnKeyStateChanged += UpdateKeyState;
        _bridge.OnActivity += OnActivity;

        ToggleEnabledCommand = new Command(ToggleEnabled);
        ShowHideWindowCommand = new Command(ToggleWindowVisibility);
        ExitApplicationCommand = new Command(ExitApplication);
        NavigatePageCommand = new Command<string>(NavigatePage);

        Lights = new LightingViewModel(bridge)
        {
            // The console's one-line reports join the pad's own, since the pad
            // has no screen and this is the only feedback there is.
            OnActivity = message => MainThread.BeginInvokeOnMainThread(() => LastAction = message),
        };
    }

    /// <summary>The manual lighting console — the Inspector's colour half.</summary>
    public LightingViewModel Lights { get; }

    public ICommand ToggleEnabledCommand { get; }
    public ICommand ShowHideWindowCommand { get; }
    public ICommand ExitApplicationCommand { get; }
    public ICommand NavigatePageCommand { get; }

    private string _selectedPage = "Studio";
    public string SelectedPage
    {
        get => _selectedPage;
        private set
        {
            if (_selectedPage == value) return;
            var previous = _selectedPage;
            _selectedPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStudioPage));
            OnPropertyChanged(nameof(IsControlsPage));
            OnPropertyChanged(nameof(IsLightingPage));
            OnPropertyChanged(nameof(IsHerdrPage));
            OnPropertyChanged(nameof(IsInspectorPage));
            if (previous == "Inspector" && value != "Inspector" && Lights.IsManual)
                Lights.IsManual = false; // Inspector paint is transient; Studio takes back the pad.
        }
    }

    public bool IsStudioPage => SelectedPage == "Studio";
    public bool IsControlsPage => SelectedPage == "Controls";
    public bool IsLightingPage => SelectedPage == "Lighting";
    public bool IsHerdrPage => SelectedPage == "Herdr";
    public bool IsInspectorPage => SelectedPage == "Inspector";

    private void NavigatePage(string? page)
    {
        if (page is "Studio" or "Controls" or "Lighting" or "Herdr" or "Inspector")
            SelectedPage = page;
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

    /// <summary>The port's SemVer version, from windows/Directory.Build.props.</summary>
    public string VersionText { get; } = $"v{StudioInfo.Version}";

    private string _lastAction = string.Empty;

    /// <summary>What the pad was last used for — it has no screen of its own.</summary>
    public string LastAction
    {
        get => _lastAction;
        set { _lastAction = value; OnPropertyChanged(); }
    }

    /// <summary>One swatch per physical key, so the pad's lighting is visible here too.</summary>
    public ObservableCollection<KeyLightViewModel> KeyLights { get; } =
        new(Enumerable.Range(0, Pad.KeyCount).Select(id => new KeyLightViewModel(id)));

    /// <summary>The shared state-to-colour table, so this list and the pad agree.</summary>
    private static readonly BridgeConfig Colors = new();

    public void UpdateKeyState(IReadOnlyDictionary<int, int> lit)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var light in KeyLights)
            {
                light.Color = lit.TryGetValue(light.KeyId, out var packed)
                    ? Color.FromArgb(PackedRgb.HexString(packed))
                    : KeyLightViewModel.Dark;
            }
        });
    }

    private void OnActivity(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => LastAction = message);
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
                    existing.DirectoryName = agent.DirectoryName;
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
                        DirectoryName = agent.DirectoryName,
                        Focused = agent.Focused,
                        FocusCommand = new Command(() => FocusAgent(agent))
                    });
                }
            }
        });
    }

    private void OnDeviceConnected()
    {
        IsConnected = true;
        StatusText = _bridge.UsingEmulator
            ? "Connected — pad emulator (Creator Micro 2 not found)"
            : "Connected";
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

    private bool _windowVisible = true;

    /// <summary>Hide the window to the tray, or bring it back (tray click / menu).</summary>
    public void ToggleWindowVisibility()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null) return;

        if (_windowVisible)
        {
            window.Hide();
        }
        else
        {
            window.Show();
            window.Activate();
        }

        _windowVisible = !_windowVisible;
    }

    public void ExitApplication()
    {
        Application.Current?.Quit();
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

    /// <summary>Jumps to this agent, the same thing its key on the pad does.</summary>
    public ICommand FocusCommand { get; set; } = new Command(() => { });

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); }
    }

    /// <summary>Same colour the agent's key gets on the pad.</summary>
    public Color StatusColor => Color.FromArgb(PackedRgb.HexString(Colors.ColorFor(Status)));

    private static readonly BridgeConfig Colors = new();

    private string _shortName = string.Empty;
    public string ShortName
    {
        get => _shortName;
        set { _shortName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The checkout the agent works in. Several agents in one repo share it, so
    /// it is shown under the name rather than used as the name.
    /// </summary>
    private string _directoryName = string.Empty;
    public string DirectoryName
    {
        get => _directoryName;
        set { _directoryName = value; OnPropertyChanged(); }
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

/// <summary>A single key on the on-screen mirror of the pad.</summary>
public class KeyLightViewModel : INotifyPropertyChanged
{
    /// <summary>An unlit key — dark enough to read as off, light enough to see.</summary>
    public static readonly Color Dark = Color.FromArgb("#1E1E1E");

    public KeyLightViewModel(int keyId) => KeyId = keyId;

    /// <summary>The firmware's key index, which is also what `Pad` addresses.</summary>
    public int KeyId { get; }

    private Color _color = Dark;
    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
