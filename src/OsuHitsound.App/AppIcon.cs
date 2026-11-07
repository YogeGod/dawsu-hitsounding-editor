using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OsuHitsound.App;

/// <summary>
/// A Studio icon asset, or a Lucide outline from Icons.axaml ("Icon" + <see cref="Glyph"/>), scaled to <see cref="Size"/>
/// in the inherited text colour, so a button's foreground (normal, selected, disabled) colours its icon.
/// </summary>
public sealed class AppIcon : Control
{
    public static readonly StyledProperty<string> GlyphProperty = AvaloniaProperty.Register<AppIcon, string>(nameof(Glyph), "");
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<AppIcon, double>(nameof(Size), 16);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<AppIcon>();

    public string Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static readonly Dictionary<string, IBrush?> StudioMasks = [];
    private IBrush? studioMask;
    private bool checkedStudioAsset;

    private static IBrush? StudioMask(string glyph)
    {
        if (StudioMasks.TryGetValue(glyph, out var cached)) return cached;
        var uri = new Uri($"avares://OsuHitsoundEditor/Assets/StudioIcons/{glyph}.png");
        IBrush? mask = null;
        if (AssetLoader.Exists(uri))
        {
            using var stream = AssetLoader.Open(uri);
            mask = new ImageBrush(new Bitmap(stream)) { Stretch = Stretch.Fill };
        }
        StudioMasks[glyph] = mask;
        return mask;
    }

    internal static bool HasStudioAsset(string glyph) => StudioMask(glyph) is not null;

    private Geometry? geometry; // looked up once per glyph
    private IPen? pen;          // rebuilt when the colour changes

    static AppIcon()
    {
        AffectsRender<AppIcon>(GlyphProperty, ForegroundProperty);
        AffectsMeasure<AppIcon>(SizeProperty);
    }

    public AppIcon()
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        IsHitTestVisible = false;
    }

    /// <summary>A transport Play button: shows Pause and turns green (the "playing" class) while playing.</summary>
    public static void ShowPlaying(Button button, AppIcon glyph, bool playing)
    {
        glyph.Glyph = playing ? "Pause" : "Play";
        button.Classes.Set("playing", playing);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphProperty) { geometry = null; studioMask = null; checkedStudioAsset = false; }
        else if (change.Property == ForegroundProperty) pen = null;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush) return;
        if (!checkedStudioAsset) { studioMask = StudioMask(Glyph); checkedStudioAsset = true; }
        if (studioMask is not null)
        {
            var bounds = new Rect(0, 0, Size, Size);
            using (context.PushOpacityMask(studioMask, bounds)) context.FillRectangle(brush, bounds);
            return;
        }
        geometry ??= this.TryFindResource("Icon" + Glyph, out var found) ? found as Geometry : null;
        if (geometry is null) return;
        // Lucide's 2-unit stroke with round ends, scaled with the shape as in the SVGs.
        pen ??= new ImmutablePen(brush.ToImmutable(), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        double scale = Size / 24;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, geometry);
    }
}
