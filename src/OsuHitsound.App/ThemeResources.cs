using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OsuHitsound.App;

/// <summary>
/// Puts the design tokens (<see cref="UiTheme"/>) into the application's resources under the names DesignSystem.axaml and
/// the windows use, and re-applies them whenever a token changes, so every window and dialog follows the inspector.
/// </summary>
internal static class ThemeResources
{
    public static void Attach(Application app)
    {
        Apply(app.Resources, UiTheme.Current);
        UiTheme.Current.Changed += () => Apply(app.Resources, UiTheme.Current);
    }

    public static void Apply(IResourceDictionary r, UiTheme t)
    {
        IBrush B(string key) => Paint.Token(key);
        IBrush Lighter(string key, double amount) => Paint.Brush(Paint.Brighten(t.Color(key), amount));

        // Surfaces, top to bottom
        r["WindowBg"] = B("window.bg");
        r["WindowText"] = B("window.text");
        r["WindowBarHeight"] = t.Number("window.height");
        r["WindowFontSize"] = t.Number("window.fontSize");
        r["WindowClose"] = B("window.close");
        r["HeaderBg"] = B("title.bg");
        r["HeaderText"] = B("title.text");
        r["ChromeBg"] = B("control.bg");
        r["InfoBg"] = B("edit.bg");
        r["StatusBg"] = B("status.bg");
        r["FooterBg"] = B("footer.bg");
        r["PanelBg"] = B("panel.bg");
        r["PanelRaised"] = B("panel.raised");
        r["VolumeSide"] = B("volume.side");
        r["PaneTitle"] = Lighter("volume.side", 0.06);
        r["Divider"] = B("divider");
        r["ChromeBorder"] = B("chrome.border");
        r["LineDivider"] = B("line.divider");

        // Text
        r["ButtonText"] = B("button.text");
        r["TextSecondary"] = B("text.secondary");
        r["TextDim"] = B("text.dim");
        r["OnAccent"] = B("text.onAccent");
        r["Brand"] = B("brand");

        // Controls
        r["ButtonBg"] = B("button.bg");
        r["ButtonBorder"] = B("button.border");
        r["ControlHover"] = Lighter("button.bg", 0.12);
        r["ControlPressed"] = Lighter("button.bg", 0.2);
        r["ButtonActive"] = B("button.active");
        r["ButtonActiveHover"] = Lighter("button.active", 0.12);
        r["ButtonActiveBorder"] = B("button.activeBorder");
        r["PanelActive"] = Lighter("button.bg", 0.08);
        // A switch that is on (ribbon tiles): the accent as a tint behind, and lightened for the icon and label.
        var accent = t.Color("button.active");
        r["AccentTint"] = Paint.Brush(accent, 0.26);
        r["AccentText"] = Paint.Brush(Color.FromRgb(Mix(accent.R), Mix(accent.G), Mix(accent.B)));
        static byte Mix(byte c) => (byte)(c + (255 - c) * 0.45);
        r["ButtonFontSize"] = t.Number("button.fontSize");
        r["ControlHeight"] = t.Number("button.height");
        r["ControlRadius"] = new CornerRadius(t.Number("button.radius"));
        r["ToolActive"] = B("tool.active");
        r["ToolActiveIcon"] = B("tool.activeIcon");
        r["FieldBg"] = B("field.bg");
        r["PlayBg"] = B("play.bg");
        r["CycleBg"] = B("cycle.bg");
        r["RecordBg"] = B("record.bg");
        r["LcdBg"] = B("lcd.bg");
        r["LcdText"] = B("lcd.text");
        r["LcdCaption"] = B("lcd.caption");
        r["LcdFontSize"] = t.Number("lcd.fontSize");
        r["WaveLow"] = B("wave.low");
        r["WaveMid"] = B("wave.mid");
        r["WaveHigh"] = B("wave.high");

        // Fluent's own resource keys (read inside its templates, not by this app): the slider, recoloured: a thin track, a small square handle.
        var track = B("slider.track");
        var thumb = B("slider.thumb");
        var value = B("button.active");
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) r[key] = track;
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" }) r[key] = value;
        r["SliderThumbBackground"] = thumb;
        r["SliderThumbBackgroundPointerOver"] = Lighter("slider.thumb", 0.15);
        r["SliderThumbBackgroundPressed"] = B("button.text");
        r["SliderTrackThemeHeight"] = 3.0;
        r["SliderHorizontalThumbWidth"] = 8.0;
        r["SliderHorizontalThumbHeight"] = 12.0;
        r["SliderVerticalThumbWidth"] = 12.0;
        r["SliderVerticalThumbHeight"] = 8.0;
        r["SliderThumbCornerRadius"] = new CornerRadius(1);

        // Menus and pop-ups
        r["MenuFlyoutPresenterBackground"] = B("panel.bg");
        r["MenuFlyoutPresenterBorderBrush"] = B("chrome.border");
        r["MenuFlyoutItemBackgroundPointerOver"] = Lighter("panel.bg", 0.1);
        r["ComboBoxDropDownBackground"] = B("panel.bg");
        r["ComboBoxDropDownBorderBrush"] = B("chrome.border");
        // UI refresh (2026-11-13): menus and pop-ups are rounded panels with a little room around their rows.
        r["OverlayCornerRadius"] = new CornerRadius(10);
        r["MenuFlyoutPresenterThemePadding"] = new Thickness(6);
    }
}
