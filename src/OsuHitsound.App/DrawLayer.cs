using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>
/// A hit-test-transparent layer composited above a control. Playback redraws one of these for the playhead
/// and leaves the scene underneath untouched.
/// </summary>
internal sealed class DrawLayer : Control
{
    private readonly Action<DrawingContext, Rect> paint;

    public DrawLayer(Action<DrawingContext, Rect> paint)
    {
        this.paint = paint;
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context) => paint(context, new Rect(Bounds.Size));

    public void Redraw() => InvalidateVisual();
}
