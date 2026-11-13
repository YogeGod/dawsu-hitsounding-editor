using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>
/// One slider of a colour pop-up, as in Cubase's colour settings: a rounded track filled with the colours it reaches
/// (the hue rainbow, grey to full colour, black to bright) and a round handle. Drag or click it; Left/Right step by 1.
/// </summary>
internal sealed class ColorSlider : Control
{
    private const double TrackHeight = 10, Thumb = 16;

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;

    /// <summary>The colour at a position from 0 (left) to 1 (right), for the track.</summary>
    public required Func<double, Color> ColorAt { get; init; }

    private double value;

    public double Value
    {
        get => value;
        set
        {
            double v = Math.Clamp(Math.Round(value), Minimum, Maximum);
            if (v == this.value) return;
            this.value = v;
            InvalidateVisual();
            ValueChanged?.Invoke(v);
        }
    }

    public event Action<double>? ValueChanged;

    public ColorSlider()
    {
        Focusable = true;
        Height = Thumb + 4;
        MinWidth = 180;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private double Fraction => Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;

    public override void Render(DrawingContext ctx)
    {
        double left = Thumb / 2, width = Math.Max(1, Bounds.Width - Thumb), mid = Bounds.Height / 2;
        var stops = new GradientStops();
        for (int i = 0; i <= 12; i++) stops.Add(new GradientStop(ColorAt(i / 12.0), i / 12.0));
        var track = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative), GradientStops = stops };
        ctx.DrawRectangle(track, new Pen(Brushes.Black, 1), new Rect(left - TrackHeight / 2, mid - TrackHeight / 2, width + TrackHeight, TrackHeight), TrackHeight / 2, TrackHeight / 2);
        var centre = new Point(left + width * Fraction, mid);
        ctx.DrawEllipse(new SolidColorBrush(ColorAt(Fraction)), new Pen(Brushes.White, 2), centre, Thumb / 2 - 1, Thumb / 2 - 1);
        if (IsFocused) ctx.DrawEllipse(null, new Pen(Brushes.White, 1), centre, Thumb / 2 + 2, Thumb / 2 + 2);
    }

    private void SetFromX(double x) =>
        Value = Minimum + Math.Clamp((x - Thumb / 2) / Math.Max(1, Bounds.Width - Thumb), 0, 1) * (Maximum - Minimum);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        e.Pointer.Capture(this);
        SetFromX(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.Pointer.Captured == this) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        if (e.Key is Key.Left or Key.Down) { Value -= step; e.Handled = true; }
        else if (e.Key is Key.Right or Key.Up) { Value += step; e.Handled = true; }
    }

    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
}
