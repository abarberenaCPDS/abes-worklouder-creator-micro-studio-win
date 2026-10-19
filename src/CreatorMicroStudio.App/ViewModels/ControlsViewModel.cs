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

/// <summary>Edits host-side input actions; it never changes the pad keymap.</summary>
public sealed class ControlsViewModel : INotifyPropertyChanged
{
    private readonly IBridgeService _bridge;
    private InputActionKind? _editorKind;
    private string _slot = "1";
    private string _amount = "5";
    private string _value = "";
    private string _direction = nameof(InputDirection.Down);
    private string _mediaKey = nameof(MediaKey.PlayPause);
    private string _status = "Select an input to inspect or edit its action.";
    private bool _isBusy;
    private IReadOnlyDictionary<int, Color> _keyColors = new Dictionary<int, Color>();
    private IReadOnlyList<string> _agentNames = Array.Empty<string>();

    public ControlsViewModel(IBridgeService bridge)
    {
        _bridge = bridge;

        foreach (var group in new[] { PadInputGroup.Keys, PadInputGroup.Dial, PadInputGroup.Joystick })
        {
            var label = group switch
            {
                PadInputGroup.Keys => "Keys",
                PadInputGroup.Dial => "Dial",
                _ => "Joystick",
            };
            var items = Pad.Inputs.Where(input => input.Group == group)
                .Select(input => new ControlsInputViewModel(input, ToggleInputSelection))
                .ToArray();
            Groups.Add(new ControlsInputGroupViewModel(label, items));
        }

        // The firmware's top row is wired in reverse. DisplayRows is the
        // canonical left-to-right physical reading order: 1, 0, then 2…12.
        var byId = Pad.Inputs.ToDictionary(input => input.Id,
            input => Groups.SelectMany(group => group.Items).Single(item => item.Id == input.Id));
        PhysicalRows = Pad.DisplayRows.Select(row => (IReadOnlyList<ControlsInputViewModel>)row
            .Select(id => byId[id]).ToArray()).ToArray();
        DialInputs = Pad.Inputs.Where(input => input.Group == PadInputGroup.Dial).Select(input => byId[input.Id]).ToArray();
        JoystickInputs = new[] { Pad.JoyNorthId, Pad.JoyWestId, Pad.JoySouthId, Pad.JoyEastId }
            .Select(id => byId[id]).ToArray();

        ActionKinds = Enum.GetValues<InputActionKind>()
            .Select(kind => new ControlsActionKindOption(kind,
                $"{new InputAction(kind).Category} · {new InputAction(kind).Title}"))
            .ToArray();
        Directions = Enum.GetNames<InputDirection>();
        MediaKeys = Enum.GetNames<MediaKey>();

        ToggleSelectionCommand = new Command<ControlsInputViewModel>(item =>
        {
            if (item is not null) ToggleInputSelection(item);
        });
        SelectAllCommand = new Command(() => SetSelection(Pad.Inputs.Select(input => input.Id)));
        ClearSelectionCommand = new Command(() => SetSelection(Array.Empty<int>()));
        ApplyCommand = new Command(async () => await ApplyAsync(), () => HasSelection && !IsBusy);
        ResetCommand = new Command(ResetToDefaults, () => HasSelection && !IsBusy);
        TestCommand = new Command(async () => await TestAsync(), () => SelectedInputs.Count == 1 && !IsBusy);

        RefreshInputSummaries();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised with the input id after <c>Test</c> successfully sends it, so a view can flash it.</summary>
    public event Action<int>? Tested;

    public ObservableCollection<ControlsInputGroupViewModel> Groups { get; } = new();
    public IReadOnlyList<IReadOnlyList<ControlsInputViewModel>> PhysicalRows { get; }
    public IReadOnlyList<ControlsInputViewModel> DialInputs { get; }
    public IReadOnlyList<ControlsInputViewModel> JoystickInputs { get; }
    public ControlsInputViewModel DialRight => DialInputs.Single(input => input.Id == Pad.DialClockwiseId);
    public ControlsInputViewModel DialLeft => DialInputs.Single(input => input.Id == Pad.DialCounterClockwiseId);
    public ControlsInputViewModel DialPush => DialInputs.Single(input => input.Id == Pad.DialPushId);
    public ControlsInputViewModel JoystickNorth => JoystickInputs.Single(input => input.Id == Pad.JoyNorthId);
    public ControlsInputViewModel JoystickWest => JoystickInputs.Single(input => input.Id == Pad.JoyWestId);
    public ControlsInputViewModel JoystickSouth => JoystickInputs.Single(input => input.Id == Pad.JoySouthId);
    public ControlsInputViewModel JoystickEast => JoystickInputs.Single(input => input.Id == Pad.JoyEastId);
    public IReadOnlyList<ControlsActionKindOption> ActionKinds { get; }
    public IReadOnlyList<string> Directions { get; }
    public IReadOnlyList<string> MediaKeys { get; }
    public ICommand ToggleSelectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand TestCommand { get; }

    public IReadOnlyList<ControlsInputViewModel> SelectedInputs => Groups.SelectMany(group => group.Items)
        .Where(input => input.IsSelected).ToArray();
    public int SelectedCount => SelectedInputs.Count;
    public bool HasSelection => SelectedCount > 0;
    public bool HasOneSelection => SelectedCount == 1;
    public string SelectionSummary => SelectedCount switch
    {
        0 => "No inputs selected",
        1 => $"{SelectedInputs[0].Name} selected",
        _ => $"{SelectedCount} inputs selected",
    };
    public string CustomizedSummary =>
        $"{Groups.SelectMany(group => group.Items).Count(input => input.Customized)} customized";

    /// <summary>The selected input ids, as the pad map's <c>Selection</c> binding.</summary>
    public IReadOnlyList<int> SelectionIds => SelectedInputs.Select(input => input.Id).ToArray();

    /// <summary>The current host-side bindings, so the pad map can show its actions.</summary>
    public KeyBindings Bindings => _bridge.Bindings;

    /// <summary>Live lit colours by input id, rebuilt from each key-state push.</summary>
    public IReadOnlyDictionary<int, Color> KeyColors => _keyColors;

    /// <summary>Agent display names in slot order, for the pad map's agent labels.</summary>
    public IReadOnlyList<string> AgentNames => _agentNames;

    public string SelectedTitle => SelectedCount switch
    {
        0 => "No input selected",
        1 => $"{SelectedInputs[0].Name} (AG{SelectedInputs[0].Id})",
        _ => $"{SelectedCount} inputs selected",
    };

    public string SelectedSubtitle => SelectedCount switch
    {
        0 => "Select a physical input to inspect or edit its host-side action.",
        1 => SelectedInputs[0].Customized
            ? "Showing a customized host-side override."
            : "Showing the default host-side action.",
        _ => "Bulk edits apply to every selected input.",
    };

    public string Status
    {
        get => _status;
        private set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    private ControlsActionKindOption? _selectedActionKind;
    public ControlsActionKindOption? SelectedActionKind
    {
        get => _selectedActionKind;
        set
        {
            if (_selectedActionKind == value) return;
            _selectedActionKind = value;
            _editorKind = value?.Kind;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSlotVisible));
            OnPropertyChanged(nameof(IsDirectionVisible));
            OnPropertyChanged(nameof(IsAmountVisible));
            OnPropertyChanged(nameof(IsValueVisible));
            OnPropertyChanged(nameof(IsKeysValue));
            OnPropertyChanged(nameof(ValueFieldLabel));
            OnPropertyChanged(nameof(ValueFieldPlaceholder));
            OnPropertyChanged(nameof(IsMediaVisible));
        }
    }

    public string Slot
    {
        get => _slot;
        set { if (_slot != value) { _slot = value; OnPropertyChanged(); } }
    }

    public string Amount
    {
        get => _amount;
        set { if (_amount != value) { _amount = value; OnPropertyChanged(); } }
    }

    public string Value
    {
        get => _value;
        set { if (_value != value) { _value = value; OnPropertyChanged(); } }
    }

    public string SelectedDirection
    {
        get => _direction;
        set { if (_direction != value) { _direction = value; OnPropertyChanged(); } }
    }

    public string SelectedMediaKey
    {
        get => _mediaKey;
        set { if (_mediaKey != value) { _mediaKey = value; OnPropertyChanged(); } }
    }

    public bool IsSlotVisible => _editorKind == InputActionKind.FocusSlot;
    public bool IsDirectionVisible => _editorKind is InputActionKind.FocusPane or InputActionKind.Scroll;
    public bool IsAmountVisible => _editorKind == InputActionKind.Scroll;
    public bool IsMediaVisible => _editorKind == InputActionKind.MediaKey;
    public bool IsValueVisible => _editorKind is InputActionKind.SendKeys or InputActionKind.HerdrCommand or
        InputActionKind.InsertText or InputActionKind.SubmitPrompt or InputActionKind.KeyboardShortcut or
        InputActionKind.TypeText or InputActionKind.OpenUrl or InputActionKind.ShellCommand;
    public bool IsKeysValue => _editorKind is InputActionKind.SendKeys or InputActionKind.KeyboardShortcut;
    public string ValueFieldLabel => _editorKind switch
    {
        InputActionKind.SendKeys => "Keys to send",
        InputActionKind.KeyboardShortcut => "Shortcut keys",
        InputActionKind.HerdrCommand => "Herdr command",
        InputActionKind.OpenUrl => "URL, file, or app",
        InputActionKind.ShellCommand => "Shell command",
        _ => "Text",
    };
    public string ValueFieldPlaceholder => _editorKind switch
    {
        InputActionKind.SendKeys => "e.g. ctrl+c esc",
        InputActionKind.KeyboardShortcut => "e.g. ctrl+shift+k",
        InputActionKind.HerdrCommand => "e.g. tab rename {tab} {ask:Name}",
        InputActionKind.OpenUrl => "https://… or an app/path",
        InputActionKind.ShellCommand => "Command to run",
        _ => "Text to insert or submit",
    };

    public void Attach()
    {
        _bridge.OnBindingsChanged += BindingsChanged;
        _bridge.OnInputCapabilitiesChanged += CapabilitiesChanged;
        _bridge.OnKeyStateChanged += KeyStateChanged;
        _bridge.OnAgentStateChanged += AgentStateChanged;
        RefreshInputSummaries();
    }

    public void Detach()
    {
        _bridge.OnBindingsChanged -= BindingsChanged;
        _bridge.OnInputCapabilitiesChanged -= CapabilitiesChanged;
        _bridge.OnKeyStateChanged -= KeyStateChanged;
        _bridge.OnAgentStateChanged -= AgentStateChanged;
    }

    /// <summary>Toggles exactly one input, for a tap on the pad map.</summary>
    public void ToggleFromPad(int id)
    {
        var input = Groups.SelectMany(group => group.Items).FirstOrDefault(item => item.Id == id);
        if (input is not null) ToggleInputSelection(input);
    }

    private void ToggleInputSelection(ControlsInputViewModel input)
    {
        input.IsSelected = !input.IsSelected;
        SelectionChanged();
    }

    private void SetSelection(IEnumerable<int> ids)
    {
        var selected = ids.ToHashSet();
        foreach (var input in Groups.SelectMany(group => group.Items)) input.IsSelected = selected.Contains(input.Id);
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedInputs));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasOneSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectionIds));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedSubtitle));
        if (SelectedCount == 1) LoadAction(SelectedInputs[0].Id);
        RefreshCommands();
    }

    private void LoadAction(int id)
    {
        var action = _bridge.Bindings.ActionFor(id);
        _editorKind = action.Kind;
        _selectedActionKind = ActionKinds.Single(option => option.Kind == action.Kind);
        Slot = (action.Slot ?? 1).ToString();
        Amount = (action.Amount ?? 5).ToString();
        Value = action.Value ?? "";
        SelectedDirection = (action.Direction ?? InputDirection.Down).ToString();
        SelectedMediaKey = (action.Media ?? MediaKey.PlayPause).ToString();
        OnPropertyChanged(nameof(SelectedActionKind));
        OnPropertyChanged(nameof(IsSlotVisible));
        OnPropertyChanged(nameof(IsDirectionVisible));
        OnPropertyChanged(nameof(IsAmountVisible));
        OnPropertyChanged(nameof(IsValueVisible));
        OnPropertyChanged(nameof(IsKeysValue));
        OnPropertyChanged(nameof(ValueFieldLabel));
        OnPropertyChanged(nameof(ValueFieldPlaceholder));
        OnPropertyChanged(nameof(IsMediaVisible));
        Status = $"Current action: {action.Title}{(_bridge.Bindings.IsActionCustomized(id) ? " (custom)" : " (default)")}.";
    }

    private async Task ApplyAsync()
    {
        if (!HasSelection || _editorKind is not { } kind) return;
        InputAction action;
        try { action = BuildAction(kind); }
        catch (FormatException ex) { Status = ex.Message; return; }

        IsBusy = true;
        try
        {
            _bridge.SetActionOverrides(SelectedInputs.Select(input => input.Id), action);
            Status = $"Applied {action.Title} to {SelectedCount} input(s).";
        }
        catch (Exception ex) { Status = $"Could not save action: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private InputAction BuildAction(InputActionKind kind)
    {
        int ParseNumber(string text, int fallback, int min, int max, string name)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            if (!int.TryParse(text, out var number)) throw new FormatException($"Enter a valid {name}.");
            return Math.Clamp(number, min, max);
        }

        var direction = Enum.TryParse<InputDirection>(SelectedDirection, out var parsedDirection)
            ? parsedDirection : InputDirection.Down;
        var media = Enum.TryParse<MediaKey>(SelectedMediaKey, out var parsedMedia)
            ? parsedMedia : MediaKey.PlayPause;
        return kind switch
        {
            InputActionKind.FocusSlot => new(kind, Slot: ParseNumber(Slot, 1, 1, 6, "slot (1–6)")),
            InputActionKind.FocusPane => new(kind, Direction: direction),
            InputActionKind.Scroll => new(kind, Direction: direction, Amount: ParseNumber(Amount, 5, 1, 20, "amount (1–20)")),
            InputActionKind.SendKeys or InputActionKind.KeyboardShortcut or InputActionKind.HerdrCommand or
                InputActionKind.InsertText or InputActionKind.SubmitPrompt or InputActionKind.TypeText or
                InputActionKind.OpenUrl or InputActionKind.ShellCommand => new(kind, Value),
            InputActionKind.MediaKey => new(kind, Media: media),
            _ => new(kind),
        };
    }

    private void ResetToDefaults()
    {
        var ids = SelectedInputs.Select(input => input.Id).ToArray();
        if (ids.Length == 0) return;
        try
        {
            _bridge.SetActionOverrides(ids, null);
            Status = $"Reset {ids.Length} input(s) to their defaults.";
        }
        catch (Exception ex) { Status = $"Could not reset action: {ex.Message}"; }
    }

    private async Task TestAsync()
    {
        if (!HasOneSelection) return;
        IsBusy = true;
        try
        {
            var input = SelectedInputs[0];
            if (_editorKind is not { } kind) return;
            var draft = BuildAction(kind);
            await _bridge.TestActionAsync(input.Id, draft);
            Status = $"Tested {input.Name}: {draft.Title}.";
            Tested?.Invoke(input.Id);
        }
        catch (Exception ex) { Status = $"Test failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void BindingsChanged(KeyBindings _)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(Bindings));
            RefreshInputSummaries();
            OnPropertyChanged(nameof(SelectedSubtitle));
            if (HasOneSelection) LoadAction(SelectedInputs[0].Id);
        });
    }

    private void KeyStateChanged(IReadOnlyDictionary<int, int> lit)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // A fresh dictionary, never the pushed one: the pad map binds to it
            // and only redraws when the reference changes.
            var colors = new Dictionary<int, Color>();
            foreach (var (id, packed) in lit)
                colors[id] = Color.FromArgb(PackedRgb.HexString(packed));
            _keyColors = colors;
            OnPropertyChanged(nameof(KeyColors));
        });
    }

    private void AgentStateChanged(List<HerdrAgent> agents)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _agentNames = agents.Select(agent => agent.ShortName).ToArray();
            OnPropertyChanged(nameof(AgentNames));
        });
    }

    private void RefreshInputSummaries()
    {
        foreach (var input in Groups.SelectMany(group => group.Items))
        {
            var action = _bridge.Bindings.ActionFor(input.Id);
            input.ActionSummary = action.Title;
            input.Customized = _bridge.Bindings.IsActionCustomized(input.Id);
            input.Available = _bridge.AvailableInputIds.Contains(input.Id);
        }
        OnPropertyChanged(nameof(CustomizedSummary));
    }

    private void CapabilitiesChanged(IReadOnlySet<int> available)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var input in Groups.SelectMany(group => group.Items))
                input.Available = available.Contains(input.Id);
        });
    }

    private void RefreshCommands()
    {
        (ApplyCommand as Command)?.ChangeCanExecute();
        (ResetCommand as Command)?.ChangeCanExecute();
        (TestCommand as Command)?.ChangeCanExecute();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ControlsActionKindOption(InputActionKind Kind, string Title)
{
    public override string ToString() => Title;
}

public sealed class ControlsInputGroupViewModel(string title, IReadOnlyList<ControlsInputViewModel> items)
{
    public string Title { get; } = title;
    public IReadOnlyList<ControlsInputViewModel> Items { get; } = items;
}

public sealed class ControlsInputViewModel : INotifyPropertyChanged
{
    public ControlsInputViewModel(PadInput input, Action<ControlsInputViewModel> toggle)
    {
        Input = input;
        ToggleCommand = new Command(() => toggle(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public PadInput Input { get; }
    public int Id => Input.Id;
    public string Name => Input.Name;
    public string ShortName => Input.ShortName;
    public bool CapabilityDependent => Input.CapabilityDependent;
    private bool _available = true;
    public bool Available
    {
        get => _available;
        set { if (_available != value) { _available = value; OnPropertyChanged(); OnPropertyChanged(nameof(CapabilityStatus)); } }
    }
    public string CapabilityStatus => !CapabilityDependent ? "" : Available
        ? "Available on the active pad profile."
        : "Unavailable on this pad profile; it does not report this direction/press.";
    public ICommand ToggleCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionMark)); } }
    }
    public string SelectionMark => IsSelected ? "●" : "○";

    private bool _customized;
    public bool Customized
    {
        get => _customized;
        set { if (_customized != value) { _customized = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActionState)); } }
    }
    public string ActionState => Customized ? "Custom" : "Default";

    private string _actionSummary = "";
    public string ActionSummary
    {
        get => _actionSummary;
        set { if (_actionSummary != value) { _actionSummary = value; OnPropertyChanged(); } }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
