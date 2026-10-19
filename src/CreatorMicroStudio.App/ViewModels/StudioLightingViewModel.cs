using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using AbesWorkLouderCreatorMicroStudio.Windows.Services;
using Microsoft.Maui.Graphics;

namespace AbesWorkLouderCreatorMicroStudio.Windows.ViewModels;

/// <summary>Persistent, host-side lighting preferences (separate from manual lighting).</summary>
public sealed class StudioLightingViewModel : INotifyPropertyChanged
{
    private readonly IBridgeService _bridge;
    private CancellationTokenSource? _saveDelay;
    private bool _updating;
    private readonly Dictionary<string, LightingPreferences.Look> _otherStatusLooks = new(StringComparer.Ordinal);
    private string _status = "Changes are saved automatically after a short pause.";
    private double _brightness;
    private double _speed;
    private string _underglowMode = nameof(LightingPreferences.UnderglowMode.Status);
    private string _backlightMode = nameof(LightingPreferences.BacklightMode.Off);
    private readonly LightingLookViewModel _noKeyLook = new();
    private LightingKeyLookViewModel? _selectedKey;
    private IReadOnlyDictionary<int, Color> _padColors = new Dictionary<int, Color>();
    private IReadOnlyList<int> _selectionIds = Array.Empty<int>();

    /// <summary>The unlit keycap shade shown inside the pad map where a key has no override.</summary>
    private static readonly Color DimKeycap = Color.FromArgb("#0D131B");

    public StudioLightingViewModel(IBridgeService bridge)
    {
        _bridge = bridge;
        Effects = Enum.GetNames<Oai.Effect>();
        UnderglowModes = Enum.GetNames<LightingPreferences.UnderglowMode>();
        BacklightModes = Enum.GetNames<LightingPreferences.BacklightMode>();

        foreach (var state in LightingPreferences.StatusStates)
            StatusLooks.Add(new LightingStatusLookViewModel(state, Changed));
        foreach (var key in Pad.DisplayRows.SelectMany(row => row))
            KeyLooks.Add(new LightingKeyLookViewModel(key, Changed));
        UnderglowLook.SetChanged(Changed);
        BacklightLook.SetChanged(Changed);

        PreviewCommand = new Command(Preview);
        RevertCommand = new Command(Revert);
        SaveNowCommand = new Command(SaveNow);
        RestoreDefaultsCommand = new Command(RestoreDefaults);
        SelectKeyCommand = new Command<int>(SelectKey);
        UseColorCommand = new Command<string>(hex => SelectedLook.ColorHex = hex);
        Load(_bridge.Lighting);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> Effects { get; }
    public IReadOnlyList<string> UnderglowModes { get; }
    public IReadOnlyList<string> BacklightModes { get; }
    public ObservableCollection<LightingStatusLookViewModel> StatusLooks { get; } = new();
    public ObservableCollection<LightingKeyLookViewModel> KeyLooks { get; } = new();
    public ICommand PreviewCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand SaveNowCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public Command<int> SelectKeyCommand { get; }
    public Command<string> UseColorCommand { get; }

    /// <summary>The key currently open in the per-key editor, as pad-map selection ids.</summary>
    public IReadOnlyList<int> SelectionIds => _selectionIds;

    public int? SelectedKeyId => _selectedKey?.KeyId;

    public LightingKeyLookViewModel? SelectedKey => _selectedKey;

    public bool HasSelectedKey => _selectedKey is not null;

    public bool HasNoSelectedKey => _selectedKey is null;

    /// <summary>A non-null proxy for the editor: the selected key's look, or a harmless default.</summary>
    public LightingLookViewModel SelectedLook => _selectedKey?.Look ?? _noKeyLook;

    /// <summary>Lit colours for the pad map: the override colour, or a dim keycap otherwise.</summary>
    public IReadOnlyDictionary<int, Color> PadColors => _padColors;

    /// <summary>Preset hex colours offered as one-tap chips in the key editor.</summary>
    public IReadOnlyList<string> ColorChips { get; } =
        new[] { "#FF2D2D", "#FFA000", "#00B0FF", "#00C853", "#7C4DFF", "#FFFFFF" };

    public double Brightness
    {
        get => _brightness;
        set { if (_brightness == value) return; _brightness = value; OnPropertyChanged(); OnPropertyChanged(nameof(BrightnessText)); Changed(); }
    }
    public string BrightnessText => _brightness.ToString("0.00");

    public double Speed
    {
        get => _speed;
        set { if (_speed == value) return; _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); Changed(); }
    }
    public string SpeedText => _speed.ToString("0.00");

    public string UnderglowMode
    {
        get => _underglowMode;
        set { if (_underglowMode == value || !Enum.TryParse<LightingPreferences.UnderglowMode>(value, out _)) return; _underglowMode = value; OnPropertyChanged(); Changed(); }
    }

    public string BacklightMode
    {
        get => _backlightMode;
        set { if (_backlightMode == value || !Enum.TryParse<LightingPreferences.BacklightMode>(value, out _)) return; _backlightMode = value; OnPropertyChanged(); Changed(); }
    }

    public LightingLookViewModel UnderglowLook { get; } = new();
    public LightingLookViewModel BacklightLook { get; } = new();

    public string Status
    {
        get => _status;
        private set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public void Attach()
    {
        _bridge.OnLightingChanged += LightingChanged;
        Load(_bridge.Lighting);
    }

    public void Detach()
    {
        _bridge.OnLightingChanged -= LightingChanged;
        if (_saveDelay is not null)
        {
            _saveDelay.Cancel();
            _saveDelay.Dispose();
            _saveDelay = null;
            SaveNow();
        }
    }

    private void LightingChanged(LightingPreferences preferences)
    {
        if (_updating) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_updating) return;
            CancelSaveDelay();
            Load(preferences);
            Status = _bridge.IsPreviewingLighting ? "Previewing lighting changes." : "Lighting preferences loaded.";
        });
    }

    private void Changed()
    {
        if (_updating) return;
        RefreshPadColors();
        var draft = Snapshot();
        _updating = true;
        try { _bridge.PreviewLightingPreferences(draft); }
        finally { _updating = false; }
        Status = "Previewing · saving in about 3 seconds…";
        ScheduleSave();
    }

    private void Preview()
    {
        _updating = true;
        try { _bridge.PreviewLightingPreferences(Snapshot()); }
        finally { _updating = false; }
        Status = "Preview active · use Save now to keep these preferences.";
    }

    private void Revert()
    {
        CancelSaveDelay();
        _updating = true;
        try { _bridge.RevertLightingPreferences(); }
        finally { _updating = false; }
        Load(_bridge.Lighting);
        Status = "Unsaved changes reverted.";
    }

    private void SaveNow()
    {
        CancelSaveDelay();
        _updating = true;
        try { _bridge.SaveLightingPreferences(Snapshot()); }
        finally { _updating = false; }
        Status = "Lighting preferences saved.";
    }

    private void RestoreDefaults()
    {
        CancelSaveDelay();
        Load(LightingPreferences.Defaults);
        Changed();
        Status = "Defaults restored · saving in about 3 seconds…";
    }

    private void ScheduleSave()
    {
        CancelSaveDelay();
        var cancellation = new CancellationTokenSource();
        _saveDelay = cancellation;
        _ = SaveAfterDelayAsync(cancellation);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!ReferenceEquals(_saveDelay, cancellation) || cancellation.IsCancellationRequested) return;
                _saveDelay = null;
                cancellation.Dispose();
                SaveNow();
            });
        }
        catch (OperationCanceledException) { }
    }

    private void CancelSaveDelay()
    {
        if (_saveDelay is null) return;
        _saveDelay.Cancel();
        _saveDelay.Dispose();
        _saveDelay = null;
    }

    private LightingPreferences Snapshot()
    {
        var result = LightingPreferences.Defaults;
        result.Brightness = Brightness;
        result.Speed = Speed;
        foreach (var (state, look) in _otherStatusLooks) result.Status[state] = look with { };
        foreach (var status in StatusLooks) result.Status[status.State] = status.Look.ToLook();
        result.Underglow = new LightingPreferences.UnderglowPreference(
            Enum.Parse<LightingPreferences.UnderglowMode>(UnderglowMode), UnderglowLook.ToLook());
        result.Backlight = new LightingPreferences.BacklightPreference(
            Enum.Parse<LightingPreferences.BacklightMode>(BacklightMode), BacklightLook.ToLook());
        foreach (var key in KeyLooks)
        {
            if (key.HasOverride) result.Keys[key.KeyId] = key.Look.ToLook();
        }
        return result;
    }

    private void Load(LightingPreferences value)
    {
        _updating = true;
        try
        {
            Brightness = value.Brightness;
            Speed = value.Speed;
            _underglowMode = value.Underglow.Mode.ToString();
            _backlightMode = value.Backlight.Mode.ToString();
            OnPropertyChanged(nameof(UnderglowMode));
            OnPropertyChanged(nameof(BacklightMode));
            UnderglowLook.Load(value.Underglow.Look);
            BacklightLook.Load(value.Backlight.Look);
            _otherStatusLooks.Clear();
            foreach (var (state, look) in value.Status)
            {
                if (!LightingPreferences.StatusStates.Contains(state, StringComparer.Ordinal))
                    _otherStatusLooks[state] = look with { };
            }
            foreach (var status in StatusLooks)
                status.Look.Load(value.Status.GetValueOrDefault(status.State) ?? LightingPreferences.DefaultStatus[status.State]);
            foreach (var key in KeyLooks)
            {
                key.HasOverride = value.Keys.TryGetValue(key.KeyId, out var look);
                key.Look.Load(look ?? new LightingPreferences.Look(0xFFA000));
            }
        }
        finally { _updating = false; }
        RefreshPadColors();
    }

    private void SelectKey(int keyId)
    {
        var key = KeyLooks.FirstOrDefault(candidate => candidate.KeyId == keyId);
        if (ReferenceEquals(key, _selectedKey)) return;

        _selectedKey = key;
        _selectionIds = key is null ? Array.Empty<int>() : new[] { key.KeyId };
        OnPropertyChanged(nameof(SelectedKey));
        OnPropertyChanged(nameof(SelectedKeyId));
        OnPropertyChanged(nameof(HasSelectedKey));
        OnPropertyChanged(nameof(HasNoSelectedKey));
        OnPropertyChanged(nameof(SelectedLook));
        OnPropertyChanged(nameof(SelectionIds));
        RefreshPadColors();
    }

    private void RefreshPadColors()
    {
        var colors = new Dictionary<int, Color>(KeyLooks.Count);
        foreach (var key in KeyLooks)
            colors[key.KeyId] = key.HasOverride ? ColorFromHex(key.Look.ColorHex) : DimKeycap;

        _padColors = colors;
        OnPropertyChanged(nameof(PadColors));
    }

    private static Color ColorFromHex(string hex) =>
        PackedRgb.FromHex(hex) is { } packed ? Color.FromArgb(PackedRgb.HexString(packed)) : DimKeycap;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class LightingStatusLookViewModel(string state, Action changed)
{
    public string State { get; } = state;
    public LightingLookViewModel Look { get; } = new(changed);
}

public sealed class LightingKeyLookViewModel(int keyId, Action changed) : INotifyPropertyChanged
{
    private bool _hasOverride;
    public int KeyId { get; } = keyId;
    public LightingLookViewModel Look { get; } = new(changed);
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasOverride
    {
        get => _hasOverride;
        set
        {
            if (_hasOverride == value) return;
            _hasOverride = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOverride)));
            changed();
        }
    }
}

/// <summary>Editor for one color/effect look, including optional local multipliers.</summary>
public sealed class LightingLookViewModel : INotifyPropertyChanged
{
    private Action? _changed;
    private string _colorHex = "#FFA000";
    private string _effect = nameof(Oai.Effect.Solid);
    private bool _useBrightness;
    private bool _useSpeed;
    private double _brightness = 1;
    private double _speed = 0.5;
    private bool _loading;

    public LightingLookViewModel() { }
    internal LightingLookViewModel(Action changed) => _changed = changed;
    internal void SetChanged(Action changed) => _changed = changed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (_colorHex == value) return;
            _colorHex = value;
            OnPropertyChanged();
            if (TryColor(value, out _)) Changed();
        }
    }
    public string Effect { get => _effect; set { if (_effect == value || !Enum.TryParse<Oai.Effect>(value, out _)) return; _effect = value; OnPropertyChanged(); Changed(); } }
    public bool UseBrightness { get => _useBrightness; set { if (_useBrightness == value) return; _useBrightness = value; OnPropertyChanged(); Changed(); } }
    public bool UseSpeed { get => _useSpeed; set { if (_useSpeed == value) return; _useSpeed = value; OnPropertyChanged(); Changed(); } }
    public double Brightness { get => _brightness; set { if (_brightness == value) return; _brightness = value; OnPropertyChanged(); OnPropertyChanged(nameof(BrightnessText)); Changed(); } }
    public string BrightnessText => _brightness.ToString("0.00");
    public double Speed { get => _speed; set { if (_speed == value) return; _speed = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedText)); Changed(); } }
    public string SpeedText => _speed.ToString("0.00");

    internal void Load(LightingPreferences.Look look)
    {
        _loading = true;
        try
        {
            ColorHex = PackedRgb.HexString(look.Color);
            Effect = look.Effect.ToString();
            UseBrightness = look.Brightness.HasValue;
            UseSpeed = look.Speed.HasValue;
            Brightness = look.Brightness ?? 1;
            Speed = look.Speed ?? 0.5;
        }
        finally { _loading = false; }
    }

    internal LightingPreferences.Look ToLook()
    {
        var color = TryColor(ColorHex, out var parsed) ? parsed : 0xFFA000;
        return new LightingPreferences.Look(color, Enum.Parse<Oai.Effect>(Effect),
            UseBrightness ? Brightness : null, UseSpeed ? Speed : null);
    }

    private static bool TryColor(string value, out int color)
    {
        color = default;
        try
        {
            if (PackedRgb.FromHex(value) is not { } parsed) return false;
            color = parsed;
            return true;
        }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private void Changed() { if (!_loading) _changed?.Invoke(); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
