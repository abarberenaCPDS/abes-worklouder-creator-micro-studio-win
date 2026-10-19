using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Herdr;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

/// <summary>Read-only view of the current Herdr agents and host-side input mappings.</summary>
public sealed class HerdrViewModel : INotifyPropertyChanged
{
    private readonly IBridgeService _bridge;
    private string _agentStatus = "Waiting for the first Herdr agent snapshot.";
    private string _inputStatus = "";
    private string _focusStatus = "Select an agent to focus its pane in Herdr.";

    public HerdrViewModel(IBridgeService bridge)
    {
        _bridge = bridge;

        var socketPath = HerdrClient.SocketPath();
        SocketPath = socketPath;
        TransportContext = DescribeTransport(socketPath);
        MetadataStatus = "Herdr server version and protocol metadata are not exposed by the current client API.";

        FocusAgentCommand = new Command<HerdrAgentViewModel>(async agent => await FocusAgentAsync(agent));
        RefreshInputMappings(_bridge.AvailableInputIds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HerdrAgentViewModel> Agents { get; } = new();
    public ObservableCollection<HerdrInputViewModel> Inputs { get; } = new();

    public string SocketPath { get; }
    public string TransportContext { get; }
    public string MetadataStatus { get; }
    public ICommand FocusAgentCommand { get; }

    public string AgentStatus
    {
        get => _agentStatus;
        private set => SetProperty(ref _agentStatus, value);
    }

    public string InputStatus
    {
        get => _inputStatus;
        private set => SetProperty(ref _inputStatus, value);
    }

    public string FocusStatus
    {
        get => _focusStatus;
        private set => SetProperty(ref _focusStatus, value);
    }

    public void Attach()
    {
        _bridge.OnAgentStateChanged += AgentsChanged;
        _bridge.OnBindingsChanged += BindingsChanged;
        _bridge.OnInputCapabilitiesChanged += InputCapabilitiesChanged;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshInputMappings(_bridge.AvailableInputIds);
            InputStatus = AvailabilitySummary(_bridge.AvailableInputIds);
        });
    }

    public void Detach()
    {
        _bridge.OnAgentStateChanged -= AgentsChanged;
        _bridge.OnBindingsChanged -= BindingsChanged;
        _bridge.OnInputCapabilitiesChanged -= InputCapabilitiesChanged;
    }

    private void AgentsChanged(List<HerdrAgent> agents)
    {
        // Preserve the server's ordering: agent N is the pad's slot N.
        var snapshot = agents.ToArray();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Agents.Clear();
            for (var index = 0; index < snapshot.Length; index++)
                Agents.Add(new HerdrAgentViewModel(snapshot[index], index, _bridge.Bindings,
                    _bridge.AvailableInputIds, FocusAgentCommand));

            AgentStatus = snapshot.Length == 0
                ? "Herdr is reachable, but its latest snapshot contains no agents."
                : $"{snapshot.Length} agent(s), in Herdr server order. The first six correspond to the pad's six agent slots.";
        });
    }

    private void BindingsChanged(KeyBindings _)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshInputMappings(_bridge.AvailableInputIds);
            RebuildAgentRows();
        });
    }

    private void InputCapabilitiesChanged(IReadOnlySet<int> availableInputIds)
    {
        var snapshot = availableInputIds.ToHashSet();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshInputMappings(snapshot);
            RebuildAgentRows();
        });
    }

    private void RebuildAgentRows()
    {
        if (Agents.Count == 0) return;
        var snapshot = Agents.Select(agent => agent.Agent).ToArray();
        Agents.Clear();
        for (var index = 0; index < snapshot.Length; index++)
            Agents.Add(new HerdrAgentViewModel(snapshot[index], index, _bridge.Bindings,
                _bridge.AvailableInputIds, FocusAgentCommand));
    }

    private void RefreshInputMappings(IReadOnlySet<int> availableInputIds)
    {
        Inputs.Clear();
        foreach (var input in Pad.Inputs)
        {
            var action = _bridge.Bindings.ActionFor(input.Id);
            Inputs.Add(new HerdrInputViewModel(input, action.Title,
                _bridge.Bindings.IsActionCustomized(input.Id), availableInputIds.Contains(input.Id)));
        }

        InputStatus = AvailabilitySummary(availableInputIds);
    }

    private static string AvailabilitySummary(IReadOnlySet<int> availableInputIds)
    {
        var unavailable = Pad.Inputs.Count(input => !availableInputIds.Contains(input.Id));
        return unavailable == 0
            ? "All listed inputs are available on the current active profile."
            : $"{unavailable} listed input(s) are unavailable on the current active profile.";
    }

    private async Task FocusAgentAsync(HerdrAgentViewModel? agent)
    {
        if (agent?.Agent.FocusTarget is not { Length: > 0 } target) return;

        try
        {
            await HerdrClient.FocusAgentAsync(target);
            MainThread.BeginInvokeOnMainThread(() => FocusStatus = $"Focus requested for {agent.Agent.ShortName}.");
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() => FocusStatus = $"Could not focus {agent.Agent.ShortName}: {ex.Message}");
        }
    }

    private static string DescribeTransport(string socketPath)
    {
        if (!OperatingSystem.IsWindows()) return "Unix domain socket";
        if (!HerdrClient.IsWslSocketPath(socketPath)) return "Windows named pipe";

        var distro = HerdrClient.WslDistro();
        return string.IsNullOrWhiteSpace(distro)
            ? "WSL socket via the WSL bridge (default distro)"
            : $"WSL socket via the WSL bridge (distro: {distro})";
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class HerdrAgentViewModel
{
    public HerdrAgentViewModel(HerdrAgent agent, int serverIndex, KeyBindings bindings,
        IReadOnlySet<int> availableInputIds, ICommand focusCommand)
    {
        Agent = agent;
        FocusCommand = focusCommand;

        if (serverIndex < Pad.AgentKeyIds.Count)
        {
            var inputId = Pad.AgentKeyIds[serverIndex];
            var input = Pad.Input(inputId)!;
            SlotSummary = $"Slot {serverIndex + 1} · AG{input.Id} · {input.Name}";
            SlotAction = $"Input action: {bindings.ActionFor(input.Id).Title}" +
                (bindings.IsActionCustomized(input.Id) ? " (custom)" : " (default)");
            InputAvailability = availableInputIds.Contains(input.Id)
                ? "Available on current profile"
                : "Unavailable on current active profile";
            IsInputUnavailable = !availableInputIds.Contains(input.Id);
        }
        else
        {
            SlotSummary = "No pad slot · the pad has six agent slots";
            SlotAction = "";
            InputAvailability = "";
            IsInputUnavailable = false;
        }
    }

    public HerdrAgent Agent { get; }
    public ICommand FocusCommand { get; }
    public string Name => Agent.ShortName;
    public string State => $"{Agent.Agent} · {Agent.AgentStatus}";
    public string Location => string.IsNullOrWhiteSpace(Agent.WorkingDirectory) ? "Working directory unavailable" : Agent.WorkingDirectory;
    public string FocusIndicator => Agent.Focused ? "Focused in Herdr" : "";
    public string SlotSummary { get; }
    public string SlotAction { get; }
    public string InputAvailability { get; }
    public bool IsInputUnavailable { get; }
    public bool HasSlot => SlotAction.Length > 0;

    /// <summary>The agent's state colour, taken from the same table the pad uses.</summary>
    public Color StatusColor => Color.FromArgb(PackedRgb.HexString(new BridgeConfig().ColorFor(Agent.AgentStatus)));
}

public sealed class HerdrInputViewModel(PadInput input, string action, bool customized, bool isAvailable)
{
    public PadInput Input { get; } = input;
    public int Id => Input.Id;
    public string Name => Input.Name;
    public string Descriptor => $"AG{Input.Id} · {Input.Group}" +
        (Input.CapabilityDependent ? " · capability-dependent" : "");
    public string Action { get; } = $"{action} ({(customized ? "custom" : "default")})";
    public string Availability { get; } = isAvailable
        ? "Available on current profile"
        : "Unavailable on current active profile";
    public bool IsAvailable { get; } = isAvailable;
    public bool IsUnavailable { get; } = !isAvailable;
}
