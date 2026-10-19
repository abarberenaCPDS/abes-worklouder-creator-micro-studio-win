using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

namespace AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

/// <summary>
/// Driving the pad's lights by hand.
///
/// This is the Windows counterpart of the macOS <b>Inspector</b>'s lighting
/// console — the only place in either implementation where a person picks a
/// colour; the menu-bar app paints from a compiled-in palette. The Inspector is
/// a separate process and has to fight the bridge for the pad, so a colour there
/// lasts one poll. Here the bridge is asked to stand down instead, which is what
/// <see cref="IBridgeService.ManualLighting"/> is for.
/// </summary>
public class LightingViewModel : INotifyPropertyChanged
{
    private readonly IBridgeService _bridge;
    private readonly LightingEditor _editor = new();

    public LightingViewModel(IBridgeService bridge)
    {
        _bridge = bridge;

        ToggleKeyCommand = new Command<LightingKeyViewModel>(ToggleKey);
        SelectAgentKeysCommand = new Command(() => Reselect(_editor.SelectAgentKeys));
        SelectAllCommand = new Command(() => Reselect(_editor.SelectEveryKey));
        SelectNoneCommand = new Command(() => Reselect(_editor.SelectNone));
        ApplySelectedCommand = new Command(async () => await ApplySelectedAsync());
        ClearSelectedCommand = new Command(async () => await ClearSelectedAsync());
        ApplyKeysZoneCommand = new Command(async () => await ApplyKeysZoneAsync());
        ApplyUnderglowCommand = new Command(async () => await ApplyUnderglowAsync());
        AllOffCommand = new Command(async () => await AllOffAsync());
        UseColorCommand = new Command<string>(hex => ColorHex = hex);
        UseAmbientColorCommand = new Command<string>(hex => AmbientColorHex = hex);

        // Reading order, so this row reads like the pad does.
        foreach (var id in Pad.DisplayRows.SelectMany(row => row))
        {
            Keys.Add(new LightingKeyViewModel(id) { SelectCommand = ToggleKeyCommand });
        }
    }

    /// <summary>A one-line description of what was just done, for the status line.</summary>
    public Action<string>? OnActivity { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ToggleKeyCommand { get; }
    public ICommand SelectAgentKeysCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand ApplySelectedCommand { get; }
    public ICommand ClearSelectedCommand { get; }
    public ICommand ApplyKeysZoneCommand { get; }
    public ICommand ApplyUnderglowCommand { get; }
    public ICommand AllOffCommand { get; }
    public ICommand UseColorCommand { get; }
    public ICommand UseAmbientColorCommand { get; }

    /// <summary>The thirteen keys, as buttons that select what an apply reaches.</summary>
    public ObservableCollection<LightingKeyViewModel> Keys { get; } = new();

    public string SelectionText =>
        _editor.Selection.Count == 0 ? "no keys selected" : $"{_editor.Selection.Count} selected";

    /// <summary>
    /// True while the lights belong to this panel. The bridge keeps tracking
    /// Herdr but stops painting, so what you set here stays set.
    /// </summary>
    private bool _isManual;
    public bool IsManual
    {
        get => _isManual;
        set
        {
            if (_isManual == value) return;
            _isManual = value;
            _bridge.ManualLighting = value;
            OnPropertyChanged();
            Report(value
                ? "The lights are yours — the bridge will not repaint them."
                : "The bridge has the lights back.");
        }
    }

    private string _colorHex = PackedRgb.HexString(0xFFA000);
    public string ColorHex
    {
        get => _colorHex;
        set
        {
            _colorHex = value;
            if (PackedRgb.FromHex(value) is { } packed) _editor.Color = packed;
            OnPropertyChanged();
        }
    }

    private string _ambientColorHex = PackedRgb.HexString(0x00C853);
    public string AmbientColorHex
    {
        get => _ambientColorHex;
        set
        {
            _ambientColorHex = value;
            if (PackedRgb.FromHex(value) is { } packed) _editor.AmbientColor = packed;
            OnPropertyChanged();
        }
    }

    /// <summary>The firmware's own effects, in its own order — `OAI.Effect`.</summary>
    public IReadOnlyList<string> Effects { get; } = Enum.GetNames<Oai.Effect>();

    public string Effect
    {
        get => _editor.Effect.ToString();
        set
        {
            if (Enum.TryParse<Oai.Effect>(value, out var effect)) _editor.Effect = effect;
            OnPropertyChanged();
        }
    }

    public string AmbientEffect
    {
        get => _editor.AmbientEffect.ToString();
        set
        {
            if (Enum.TryParse<Oai.Effect>(value, out var effect)) _editor.AmbientEffect = effect;
            OnPropertyChanged();
        }
    }

    public double Brightness
    {
        get => _editor.Brightness;
        set
        {
            _editor.Brightness = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public string BrightnessText => _editor.Brightness.ToString("0.00");

    public double Speed
    {
        get => _editor.Speed;
        set
        {
            _editor.Speed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpeedText));
        }
    }

    public string SpeedText => _editor.Speed.ToString("0.00");

    public double AmbientBrightness
    {
        get => _editor.AmbientBrightness;
        set
        {
            _editor.AmbientBrightness = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AmbientBrightnessText));
        }
    }

    public string AmbientBrightnessText => _editor.AmbientBrightness.ToString("0.00");

    public double AmbientSpeed
    {
        get => _editor.AmbientSpeed;
        set
        {
            _editor.AmbientSpeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AmbientSpeedText));
        }
    }

    public string AmbientSpeedText => _editor.AmbientSpeed.ToString("0.00");

    private void ToggleKey(LightingKeyViewModel? key)
    {
        if (key is null) return;
        _editor.Toggle(key.KeyId);
        key.IsSelected = _editor.IsSelected(key.KeyId);
        OnPropertyChanged(nameof(SelectionText));
    }

    private void Reselect(Action select)
    {
        select();
        foreach (var key in Keys) key.IsSelected = _editor.IsSelected(key.KeyId);
        OnPropertyChanged(nameof(SelectionText));
    }

    /// <summary>
    /// The bridge repaints every 2.5 s, so an apply has to take the lights off it
    /// first or the colour is gone before it is seen. Doing that silently would
    /// be the surprising part, so the switch reports itself.
    /// </summary>
    private void EnsureManual()
    {
        if (!IsManual) IsManual = true;
    }

    private async Task ApplySelectedAsync()
    {
        if (_editor.Selection.Count == 0)
        {
            Report("Pick at least one key to light first.");
            return;
        }

        EnsureManual();
        await _bridge.ApplyLightingAsync(threads: _editor.SelectedThreads());
        Report($"Lit {SelectionText}: {ColorHex} · {Effect}");
    }

    private async Task ClearSelectedAsync()
    {
        if (_editor.Selection.Count == 0)
        {
            Report("Pick at least one key to clear first.");
            return;
        }

        EnsureManual();
        await _bridge.ApplyLightingAsync(threads: LightingEditor.OffThreads(_editor.Selection));
        Report($"Cleared {SelectionText}");
    }

    private async Task ApplyKeysZoneAsync()
    {
        EnsureManual();
        await _bridge.ApplyLightingAsync(keys: _editor.KeysZone());
        Report($"The whole keys zone: {ColorHex} · {Effect}");
    }

    private async Task ApplyUnderglowAsync()
    {
        EnsureManual();
        await _bridge.ApplyLightingAsync(ambient: _editor.AmbientZone());
        Report($"Underglow: {AmbientColorHex} · {AmbientEffect}");
    }

    private async Task AllOffAsync()
    {
        EnsureManual();
        await _bridge.ApplyLightingAsync(
            threads: LightingEditor.AllOffThreads(),
            keys: Oai.Zone.Dark,
            ambient: Oai.Zone.Dark);
        Report("All lights off");
    }

    private void Report(string message) => OnActivity?.Invoke(message);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>One key of the lighting console's pad: a selection, not a colour.</summary>
public class LightingKeyViewModel : INotifyPropertyChanged
{
    public LightingKeyViewModel(int keyId) => KeyId = keyId;

    public int KeyId { get; }

    public string Label => KeyId.ToString();

    /// <summary>
    /// Set by the panel rather than looked up from an ancestor binding, which
    /// inside a template is fiddly and easy to get subtly wrong.
    /// </summary>
    public ICommand SelectCommand { get; init; } = new Command(() => { });

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextColor)));
        }
    }

    /// <summary>Selected keys are the accent colour, so a selection is legible at a glance.</summary>
    public Color Background =>
        IsSelected ? Color.FromArgb("#007ACC") : Color.FromArgb("#2D2D2D");

    public Color TextColor =>
        IsSelected ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#CCCCCC");

    public event PropertyChangedEventHandler? PropertyChanged;
}
