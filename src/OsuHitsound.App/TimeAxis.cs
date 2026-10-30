namespace OsuHitsound.App;

/// <summary>
/// The horizontal mapping shared by the arrange area, the volume lane and the overview strip: which time is at the left
/// edge of the grid and how many pixels a millisecond takes.
/// </summary>
internal sealed class TimeAxis
{
    public const double MinZoom = 0.01, MaxZoom = 2;

    private double left;
    private double zoom = 0.15;

    public event Action? Changed;

    /// <summary>Time at the grid's left edge, in ms.</summary>
    public double Left
    {
        get => left;
        set
        {
            value = Math.Clamp(value, -2000, Math.Max(End, 0));
            if (value == left) return;
            left = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Pixels per ms.</summary>
    public double Zoom
    {
        get => zoom;
        set
        {
            value = Math.Clamp(value, MinZoom, MaxZoom);
            if (value == zoom) return;
            zoom = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Song length in ms (the scroll limit).</summary>
    public double End { get; set; }

    /// <summary>Width of the grid in pixels, set by the arrange view on layout.</summary>
    public double Width { get; set; } = 1000;

    public double VisibleDuration => Width / zoom;

    public double X(double time) => (time - left) * zoom;

    public double Time(double x) => left + x / zoom;

    /// <summary>Zooms keeping the time under <paramref name="x"/> where it is.</summary>
    public void ZoomAt(double factor, double x)
    {
        double anchor = Time(x);
        Zoom = zoom * factor;
        Left = anchor - x / zoom;
    }
}
