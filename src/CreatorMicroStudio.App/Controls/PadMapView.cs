using System.Globalization;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;
using Microsoft.Maui.Graphics;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Controls;

/// <summary>Raised when a physical key, dial half, dial push or joystick sector is tapped.</summary>
public sealed class PadMapInputEventArgs(int inputId) : EventArgs
{
    public int InputId { get; } = inputId;
}

/// <summary>
/// The pad drawn the way it physically sits: two top keys flanked by the dial
/// and joystick, then the rows below and the non-key case mark in the last
/// corner. Draws live key colours and selection, and raises
/// <see cref="InputTapped"/> for clicks. Mirrors the macOS Studio's
/// `StudioPadMapView` / `ControlsPadView`.
/// </summary>
public sealed class PadMapView : GraphicsView, IDrawable
{
    private const float PanelPadding = 14f;
    private const float Gap = 8f;
    private const int Columns = 4;
    private const int Rows = 4;
    private const float CellAspect = 0.84f;

    private static readonly Color Panel = Color.FromArgb("#0A0F16");
    private static readonly Color Accent = Color.FromArgb("#F5D77A");
    private static readonly Color Keycap = Color.FromArgb("#1B2637");
    private static readonly Color KeycapDark = Color.FromArgb("#141B27");
    private static readonly Color KeycapStroke = Color.FromArgb("#34FFFFFF");
    private static readonly Color RingIdle = Color.FromArgb("#26FFFFFF");
    private static readonly Color RingStroke = Color.FromArgb("#3AFFFFFF");
    private static readonly Color Ink = Color.FromArgb("#E8EDF5");
    private static readonly Color InkFaint = Color.FromArgb("#88FFFFFF");

    private int? _flashed;
    private CancellationTokenSource? _flashCts;

    public PadMapView()
    {
        Drawable = this;
        StartInteraction += OnStartInteraction;
    }

    // MARK: Bindable surface

    public static readonly BindableProperty SelectionProperty = BindableProperty.Create(
        nameof(Selection), typeof(IEnumerable<int>), typeof(PadMapView), null,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    public static readonly BindableProperty KeyColorsProperty = BindableProperty.Create(
        nameof(KeyColors), typeof(IReadOnlyDictionary<int, Color>), typeof(PadMapView), null,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    public static readonly BindableProperty AgentNamesProperty = BindableProperty.Create(
        nameof(AgentNames), typeof(IReadOnlyList<string>), typeof(PadMapView), null,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    public static readonly BindableProperty BindingsProperty = BindableProperty.Create(
        nameof(Bindings), typeof(KeyBindings), typeof(PadMapView), null,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    public static readonly BindableProperty ShowLabelsProperty = BindableProperty.Create(
        nameof(ShowLabels), typeof(bool), typeof(PadMapView), true,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    public static readonly BindableProperty ShowKeyIdsProperty = BindableProperty.Create(
        nameof(ShowKeyIds), typeof(bool), typeof(PadMapView), true,
        propertyChanged: (bindable, _, _) => ((PadMapView)bindable).Invalidate());

    /// <summary>Selected input ids, highlighted with the accent colour.</summary>
    public IEnumerable<int>? Selection
    {
        get => (IEnumerable<int>?)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Live lit colours by key id; unlit keys fall back to the keycap colour.</summary>
    public IReadOnlyDictionary<int, Color>? KeyColors
    {
        get => (IReadOnlyDictionary<int, Color>?)GetValue(KeyColorsProperty);
        set => SetValue(KeyColorsProperty, value);
    }

    /// <summary>Agent display names in slot order, so agent keys can show the agent's name.</summary>
    public IReadOnlyList<string>? AgentNames
    {
        get => (IReadOnlyList<string>?)GetValue(AgentNamesProperty);
        set => SetValue(AgentNamesProperty, value);
    }

    /// <summary>Current host-side bindings, so keycaps can show their action.</summary>
    public KeyBindings? Bindings
    {
        get => (KeyBindings?)GetValue(BindingsProperty);
        set => SetValue(BindingsProperty, value);
    }

    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public bool ShowKeyIds
    {
        get => (bool)GetValue(ShowKeyIdsProperty);
        set => SetValue(ShowKeyIdsProperty, value);
    }

    public event EventHandler<PadMapInputEventArgs>? InputTapped;

    /// <summary>Momentarily lights an input white, as the macOS controls map does on a test press.</summary>
    public void Flash(int inputId)
    {
        _flashCts?.Cancel();
        var cts = new CancellationTokenSource();
        _flashCts = cts;
        _flashed = inputId;
        Invalidate();
        _ = ResetFlashAsync(cts);
    }

    private async Task ResetFlashAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(180, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.Dispatch(() =>
        {
            if (!ReferenceEquals(_flashCts, cts)) return;
            _flashCts = null;
            _flashed = null;
            cts.Dispose();
            Invalidate();
        });
    }

    // MARK: Drawing

    void IDrawable.Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();

        canvas.FillColor = Panel;
        canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height, 18);
        canvas.StrokeColor = Color.FromArgb("#22293A");
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(dirtyRect.X + 0.5f, dirtyRect.Y + 0.5f,
            MathF.Max(0, dirtyRect.Width - 1), MathF.Max(0, dirtyRect.Height - 1), 18);

        var metrics = Metrics.For(dirtyRect);
        for (var row = 0; row < Pad.PhysicalDisplayRows.Count; row++)
        {
            var cells = Pad.PhysicalDisplayRows[row];
            for (var column = 0; column < cells.Count; column++)
                DrawCell(canvas, metrics, row, column, cells[column]);
        }

        canvas.RestoreState();
    }

    private void DrawCell(ICanvas canvas, Metrics metrics, int row, int column, PadDisplayCell cell)
    {
        var rect = metrics.Rect(row, column);
        switch (cell.Kind)
        {
            case PadDisplayCellKind.Key:
                DrawKey(canvas, rect, cell.InputId);
                break;
            case PadDisplayCellKind.Dial:
                DrawDial(canvas, rect);
                break;
            case PadDisplayCellKind.Joystick:
                DrawJoystick(canvas, rect);
                break;
            case PadDisplayCellKind.CaseMark:
                DrawCaseMark(canvas, rect);
                break;
        }
    }

    private void DrawKey(ICanvas canvas, RectF rect, int id)
    {
        var selected = IsSelected(id);
        var flashed = _flashed == id;
        var lit = LitColor(id);
        var radius = MathF.Min(9f, rect.Height * 0.16f);

        canvas.FillColor = selected ? Accent.WithAlpha(0.34f) : lit ?? KeySurface(id);
        canvas.FillRoundedRectangle(rect.X, rect.Y, rect.Width, rect.Height, radius);

        canvas.StrokeColor = selected ? Accent : KeycapStroke;
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(rect.X + 0.5f, rect.Y + 0.5f,
            MathF.Max(0, rect.Width - 1), MathF.Max(0, rect.Height - 1), radius);

        if (flashed)
        {
            canvas.FillColor = Colors.White.WithAlpha(0.35f);
            canvas.FillRoundedRectangle(rect.X, rect.Y, rect.Width, rect.Height, radius);
        }

        if (ShowLabels)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontColor = Ink;
            canvas.FontSize = MathF.Max(9f, rect.Height * 0.155f);
            canvas.DrawString(TitleFor(id), rect.X + 8, rect.Y + 7, MathF.Max(0, rect.Width - 16),
                MathF.Max(0, rect.Height - 14), HorizontalAlignment.Left, VerticalAlignment.Top,
                TextFlow.ClipBounds);
        }

        if (ShowKeyIds)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontColor = InkFaint;
            canvas.FontSize = MathF.Max(8f, rect.Height * 0.13f);
            canvas.DrawString(id.ToString(CultureInfo.InvariantCulture), rect.X + 6, rect.Y,
                MathF.Max(0, rect.Width - 12), MathF.Max(0, rect.Height - 6),
                HorizontalAlignment.Right, VerticalAlignment.Bottom, TextFlow.ClipBounds);
        }

        if (Bindings is { } bindings && bindings.ActionFor(id).Kind == InputActionKind.FocusAgent &&
            bindings.AgentSlotFor(id) is { } slot && AgentNames is { } names && slot < names.Count)
        {
            canvas.FillColor = lit ?? Color.FromArgb("#4A5A72");
            canvas.FillCircle(rect.X + 12, rect.Bottom - 12, 3.6f);
        }

        if (Bindings?.IsActionCustomized(id) == true)
        {
            canvas.FillColor = Accent;
            canvas.FillCircle(rect.Right - 11, rect.Y + 11, 3.4f);
        }
    }

    private void DrawDial(ICanvas canvas, RectF cell)
    {
        var diameter = MathF.Min(cell.Width, cell.Height) * 0.86f;
        var rect = new RectF(cell.Center.X - diameter / 2, cell.Center.Y - diameter / 2, diameter, diameter);

        DrawSector(canvas, rect, -90, 90, Pad.DialClockwiseId, "\u21BB", 0.46f);
        DrawSector(canvas, rect, 90, 270, Pad.DialCounterClockwiseId, "\u21BA", 0.46f);

        var inner = diameter * 0.46f;
        var selectedPush = IsSelected(Pad.DialPushId);
        canvas.FillColor = selectedPush ? Accent.WithAlpha(0.5f) : Color.FromArgb("#243247");
        canvas.FillCircle(cell.Center.X, cell.Center.Y, inner);
        canvas.StrokeColor = selectedPush ? Accent : RingStroke;
        canvas.StrokeSize = 1;
        canvas.DrawCircle(cell.Center.X, cell.Center.Y, inner);
        DrawGlyph(canvas, "\u25CF", cell.Center.X, cell.Center.Y, 9f);
    }

    private void DrawJoystick(ICanvas canvas, RectF cell)
    {
        var diameter = MathF.Min(cell.Width, cell.Height) * 0.86f;
        var rect = new RectF(cell.Center.X - diameter / 2, cell.Center.Y - diameter / 2, diameter, diameter);

        canvas.FillColor = Color.FromArgb("#1A2333");
        canvas.FillCircle(cell.Center.X, cell.Center.Y, diameter / 2);

        const float inner = 0.36f;
        DrawSector(canvas, rect, -135, -45, Pad.JoyNorthId, "\u25B2", inner);
        DrawSector(canvas, rect, -45, 45, Pad.JoyEastId, "\u25B6", inner);
        DrawSector(canvas, rect, 45, 135, Pad.JoySouthId, "\u25BC", inner);
        DrawSector(canvas, rect, 135, 225, Pad.JoyWestId, "\u25C0", inner);

        canvas.FillColor = Color.FromArgb("#0A0F16");
        canvas.FillCircle(cell.Center.X, cell.Center.Y, diameter * inner - 3);
        canvas.FillColor = Colors.White.WithAlpha(0.28f);
        canvas.FillCircle(cell.Center.X, cell.Center.Y, diameter * inner * 0.3f);
    }

    private void DrawSector(ICanvas canvas, RectF rect, float start, float end, int inputId, string glyph, float innerRatio)
    {
        var selected = IsSelected(inputId);
        var flashed = _flashed == inputId;

        canvas.FillColor = selected
            ? Accent.WithAlpha(0.85f)
            : flashed ? Colors.White.WithAlpha(0.45f) : RingIdle;
        canvas.FillArc(rect.X, rect.Y, rect.Width, rect.Height, start, end, true);

        canvas.StrokeColor = selected ? Accent : RingStroke;
        canvas.StrokeSize = 1;
        canvas.DrawArc(rect.X, rect.Y, rect.Width, rect.Height, start, end, true, false);

        var mid = (start + end) / 2f * MathF.PI / 180f;
        var radius = rect.Width / 2f * (innerRatio > 0 ? innerRatio + (1 - innerRatio) / 2f : 0.7f);
        DrawGlyph(canvas, glyph, rect.Center.X + MathF.Cos(mid) * radius,
            rect.Center.Y + MathF.Sin(mid) * radius, MathF.Max(9f, rect.Width * 0.14f));
    }

    private void DrawCaseMark(ICanvas canvas, RectF cell)
    {
        var diameter = MathF.Min(cell.Width, cell.Height) * 0.42f;
        canvas.FillColor = Color.FromArgb("#243247");
        canvas.FillCircle(cell.Center.X, cell.Center.Y, diameter / 2);
    }

    private void DrawGlyph(ICanvas canvas, string glyph, float cx, float cy, float size)
    {
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontColor = Colors.White.WithAlpha(0.85f);
        canvas.FontSize = size;
        const float box = 14f;
        canvas.DrawString(glyph, cx - box, cy - box, box * 2, box * 2,
            HorizontalAlignment.Center, VerticalAlignment.Center, TextFlow.ClipBounds);
    }

    // MARK: Helpers

    private bool IsSelected(int id) => Selection?.Contains(id) == true;

    private Color? LitColor(int id) =>
        KeyColors is not null && KeyColors.TryGetValue(id, out var color) && color is not null ? color : null;

    private static Color KeySurface(int id) => Pad.BoundKeyIds.Contains(id) ? Keycap : KeycapDark;

    private string TitleFor(int id)
    {
        var action = Bindings?.ActionFor(id);
        if (action is null) return Pad.Input(id)?.ShortName ?? id.ToString(CultureInfo.InvariantCulture);

        if (action.Kind == InputActionKind.FocusAgent)
        {
            if (Bindings?.AgentSlotFor(id) is not { } slot) return "Current agent";
            return AgentNames is { } names && slot < names.Count ? names[slot] : $"Slot {slot + 1}";
        }

        return action.Summary;
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        if (HitTest(e.Touches[0]) is { } inputId)
            InputTapped?.Invoke(this, new PadMapInputEventArgs(inputId));
    }

    private int? HitTest(PointF point)
    {
        var bounds = new RectF(0, 0, (float)Width, (float)Height);
        var metrics = Metrics.For(bounds);
        for (var row = 0; row < Pad.PhysicalDisplayRows.Count; row++)
        {
            var cells = Pad.PhysicalDisplayRows[row];
            for (var column = 0; column < cells.Count; column++)
            {
                var rect = metrics.Rect(row, column);
                if (!rect.Contains(point.X, point.Y)) continue;
                var cell = cells[column];
                return cell.Kind switch
                {
                    PadDisplayCellKind.Key => cell.InputId,
                    PadDisplayCellKind.CaseMark => null,
                    PadDisplayCellKind.Dial => HitDial(rect, point),
                    PadDisplayCellKind.Joystick => HitJoystick(rect, point),
                    _ => null,
                };
            }
        }

        return null;
    }

    private static int? HitDial(RectF cell, PointF point)
    {
        var diameter = MathF.Min(cell.Width, cell.Height) * 0.86f;
        var dx = point.X - cell.Center.X;
        var dy = point.Y - cell.Center.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > diameter / 2) return null;
        if (distance < diameter / 2 * 0.46f) return Pad.DialPushId;
        return dx < 0 ? Pad.DialCounterClockwiseId : Pad.DialClockwiseId;
    }

    private static int? HitJoystick(RectF cell, PointF point)
    {
        var diameter = MathF.Min(cell.Width, cell.Height) * 0.86f;
        var dx = point.X - cell.Center.X;
        var dy = point.Y - cell.Center.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance > diameter / 2 || distance < diameter / 2 * 0.36f) return null;

        var angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        if (angle >= -45 && angle < 45) return Pad.JoyEastId;
        if (angle >= 45 && angle < 135) return Pad.JoySouthId;
        if (angle >= -135 && angle < -45) return Pad.JoyNorthId;
        return Pad.JoyWestId;
    }

    private readonly record struct Metrics(float CellWidth, float CellHeight, float OriginX, float OriginY)
    {
        public static Metrics For(RectF bounds)
        {
            var cellWidth = (bounds.Width - PanelPadding * 2 - Gap * (Columns - 1)) / Columns;
            var cellHeight = cellWidth * CellAspect;
            var maxCellHeight = (bounds.Height - PanelPadding * 2 - Gap * (Rows - 1)) / Rows;
            if (maxCellHeight > 0 && cellHeight > maxCellHeight)
            {
                cellHeight = maxCellHeight;
                cellWidth = cellHeight / CellAspect;
            }

            var totalWidth = cellWidth * Columns + Gap * (Columns - 1);
            var totalHeight = cellHeight * Rows + Gap * (Rows - 1);
            var originX = PanelPadding + MathF.Max(0, (bounds.Width - PanelPadding * 2 - totalWidth) / 2f);
            var originY = PanelPadding + MathF.Max(0, (bounds.Height - PanelPadding * 2 - totalHeight) / 2f);
            return new Metrics(cellWidth, cellHeight, originX, originY);
        }

        public RectF Rect(int row, int column) => new(
            OriginX + column * (CellWidth + Gap),
            OriginY + row * (CellHeight + Gap),
            CellWidth,
            CellHeight);
    }
}
