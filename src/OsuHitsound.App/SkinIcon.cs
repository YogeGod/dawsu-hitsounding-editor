using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OsuHitsound.App;

/// <summary>Monochrome icons from the supplied skin, tinted by the owning control's foreground.</summary>
public sealed class SkinIcon : TemplatedControl
{
    public static readonly StyledProperty<string> GlyphProperty = AvaloniaProperty.Register<SkinIcon, string>(nameof(Glyph), "Play");
    public string Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    private static readonly Lazy<Bitmap> Sheet = new(() => new Bitmap(AssetLoader.Open(new Uri("avares://OsuHitsoundEditor/Assets/skin-icons.png"))));
    private static readonly Dictionary<string, IBrush> Masks = [];

    static SkinIcon() => AffectsRender<SkinIcon>(GlyphProperty, ForegroundProperty);
    public SkinIcon()
    {
        Width = Height = 18;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        if (!Masks.TryGetValue(Glyph, out var mask))
        {
            // Coordinates in skin/icons.xml, scaled for its supplied 2x bitmap.
            var (x, y) = Glyph switch
            {
                "Cycle" => (120, 60), "Stop" => (140, 60), "Play" => (160, 60),
                "Pause" => (600, 120), "Help" => (220, 80), "Settings" => (280, 120),
                _ => throw new ArgumentOutOfRangeException(nameof(Glyph)),
            };
            mask = new ImageBrush(Sheet.Value) { SourceRect = new RelativeRect(x * 2, y * 2, 40, 40, RelativeUnit.Absolute), Stretch = Stretch.Uniform };
            Masks[Glyph] = mask;
        }
        var bounds = new Rect(Bounds.Size);
        using (context.PushOpacityMask(mask, bounds)) context.DrawRectangle(Foreground, null, bounds);
    }
}
