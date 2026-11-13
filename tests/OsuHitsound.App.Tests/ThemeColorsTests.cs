using Avalonia.Media;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class ThemeColorsTests
{
    [Theory]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#2f6fd0")]
    [InlineData("#1b1b1e")]
    [InlineData("#ff453a")]
    [InlineData("#3fb5b0")]
    public void Hsb_round_trips_colours(string hex)
    {
        var colour = Color.Parse(hex);
        Assert.Equal(colour, Hsb.FromColor(colour).ToColor());
    }

    [Fact]
    public void Hsb_reads_hue_saturation_and_brightness()
    {
        var red = Hsb.FromColor(Color.Parse("#ff0000"));
        Assert.Equal((0d, 1d, 1d), (red.H, red.S, red.B));
        var grey = Hsb.FromColor(Color.Parse("#808080"));
        Assert.Equal(0, grey.S);
        Assert.Equal(128 / 255.0, grey.B, 6);
    }

    [Fact]
    public void Schemes_tint_the_greys_and_keep_coloured_keys()
    {
        var theme = new UiTheme();
        theme.SetScheme(1);
        string darkPanel = theme.Get("panel.bg"), darkPlay = theme.Get("play.bg"), darkText = theme.Get("button.text");
        theme.SetScheme(3);
        Assert.NotEqual(darkPanel, theme.Get("panel.bg"));
        Assert.True(Hsb.FromColor(theme.Color("panel.bg")).S > 0.2); // teal, not grey
        Assert.Equal(darkPlay, theme.Get("play.bg"));
        Assert.Equal(darkText, theme.Get("button.text"));
        Assert.True(theme.DarkMode);
        theme.SetScheme(0);
        Assert.False(theme.DarkMode);
        Assert.Equal("#383838", theme.Get("panel.bg"));
    }

    [Fact]
    public void Slate_scheme_has_its_own_surfaces_and_dark_text_on_the_accent()
    {
        var theme = new UiTheme();
        theme.SetScheme(UiTheme.SlateScheme);
        Assert.True(theme.DarkMode);
        Assert.Equal("#181b21", theme.Get("title.bg"));
        Assert.Equal("#2e343f", theme.Get("control.bg"));
        Assert.Equal("#13161b", theme.Get("arrange.bg"));
        Assert.Equal("#3d9bff", theme.Get("button.active"));
        Assert.Equal("#06121f", theme.Get("text.onAccent"));
        // Bars differ in shade: the title bar is darker than the toolbar, the grid darker than both.
        double B(string key) => Hsb.FromColor(theme.Color(key)).B;
        Assert.True(B("arrange.bg") < B("title.bg") && B("title.bg") < B("control.bg"));
        // The user's accent still wins over the scheme, and leaving the scheme restores the default text colour.
        ThemeColors.Apply(theme, new UserColors { Scheme = UiTheme.SlateScheme }.With(ThemeColors.Accent, new Hsb(10, 1, 1)), darkModeFallback: false);
        Assert.NotEqual("#3d9bff", theme.Get("button.active"));
        ThemeColors.Apply(theme, new UserColors { Scheme = 1 }, darkModeFallback: false);
        Assert.Equal("#ffffff", theme.Get("text.onAccent"));
    }

    [Fact]
    public void Nothing_ticked_changes_nothing_and_ticking_starts_from_the_current_colour()
    {
        var theme = new UiTheme();
        ThemeColors.Apply(theme, new UserColors { Scheme = 1 }, darkModeFallback: false);
        Assert.Equal("#1b1b1e", theme.Get("panel.bg"));
        foreach (var row in ThemeColors.Rows)
        {
            // Ticking a row without moving its sliders keeps the colour it shows (within rounding).
            var before = theme.Color(row.MainKey);
            var colours = new UserColors { Scheme = 1 }.With(row.Id, ThemeColors.Start(row, theme.BaseValue));
            if (row.Requires is { } parent) colours = colours.With(parent, ThemeColors.Start(ThemeColors.Rows.Single(r => r.Id == parent), theme.BaseValue));
            var built = ThemeColors.Build(colours, theme.BaseValue);
            if (row.Id == ThemeColors.GridCycle) continue; // a new shading, not an existing colour
            Assert.True(built.TryGetValue(row.MainKey, out var hex), row.Id);
            var after = Color.Parse(hex);
            Assert.True(Math.Abs(after.R - before.R) <= 2 && Math.Abs(after.G - before.G) <= 2 && Math.Abs(after.B - before.B) <= 2, $"{row.Id}: {before} became {after}");
        }
    }

    [Fact]
    public void Rows_set_their_keys_and_two_slider_rows_take_the_scheme_hue()
    {
        var theme = new UiTheme();
        var colours = new UserColors { Scheme = 1 }
            .With(ThemeColors.UserScheme, new Hsb(200, 0.3, 0.2))
            .With(ThemeColors.Accent, new Hsb(30, 1, 1))
            .With(ThemeColors.GridBackground, new Hsb(0, 0.5, 0.15))
            .With(ThemeColors.GridLines, new Hsb(0, 0.5, 0.4))
            .With(ThemeColors.GridCycle, new Hsb(0, 0.5, 0.6))
            .With(ThemeColors.RulerActive, new Hsb(263, 0.8, 0.48));
        ThemeColors.Apply(theme, colours, darkModeFallback: false);

        Assert.Equal(new Hsb(30, 1, 1).ToHex(), theme.Get("button.active"));
        Assert.Equal(new Hsb(30, 1, 1).ToHex(), theme.Get("note.selected"));
        Assert.Equal(new Hsb(263, 0.8, 0.48).ToHex(), theme.Get("cycle.active"));
        Assert.Equal(new Hsb(200, 0.5, 0.15).ToHex(), theme.Get("arrange.bg")); // the scheme's hue, the row's saturation and brightness
        Assert.Equal(new Hsb(200, 0.5, 0.4).ToHex(), theme.Get("line.bar"));
        Assert.Equal(0x40, theme.Color("cycle.grid").A);
        Assert.Equal(200, Hsb.FromColor(theme.Color("panel.bg")).H, 0);
        Assert.Equal(0.2, Hsb.FromColor(theme.Color("panel.bg")).B, 2);
    }

    [Fact]
    public void A_light_user_scheme_turns_the_text_dark()
    {
        var theme = new UiTheme();
        ThemeColors.Apply(theme, new UserColors { Scheme = 1 }.With(ThemeColors.UserScheme, new Hsb(0, 0, 0.85)), darkModeFallback: false);
        Assert.True(Hsb.FromColor(theme.Color("button.text")).B < 0.3);
    }

    [Fact]
    public void Grid_lines_and_cycle_need_the_grid_background()
    {
        var theme = new UiTheme();
        var colours = new UserColors { Scheme = 1 }.With(ThemeColors.GridLines, new Hsb(0, 1, 1));
        Assert.False(ThemeColors.Build(colours, theme.BaseValue).ContainsKey("line.bar"));
    }

    [Fact]
    public void The_user_layer_never_reaches_the_theme_file()
    {
        var theme = UiTheme.FromJson("{\"row.height\":\"42\"}");
        string json = theme.ToJson();
        ThemeColors.Apply(theme, new UserColors { Scheme = 2 }.With(ThemeColors.Accent, new Hsb(10, 1, 1)), darkModeFallback: false);
        Assert.Equal(json, theme.ToJson());
        Assert.Equal(42, theme.Number("row.height"));
    }

    [Fact]
    public void No_scheme_picked_follows_the_old_dark_mode_switch_and_colours_round_trip_in_preferences()
    {
        var theme = new UiTheme();
        ThemeColors.Apply(theme, new UserColors(), darkModeFallback: false);
        Assert.Equal(0, theme.Scheme);
        ThemeColors.Apply(theme, new UserColors(), darkModeFallback: true);
        Assert.Equal(UiTheme.SlateScheme, theme.Scheme); // dark mode with no scheme picked: Slate, the default look since 2026-11-13

        var path = Path.Combine(Path.GetTempPath(), $"dawsu-colours-{Guid.NewGuid():N}.json");
        try
        {
            var colours = new UserColors { Scheme = 3 }.With(ThemeColors.RulerReverse, new Hsb(12.5, 0.75, 0.9));
            new Preferences { Colors = colours }.Save(path);
            Assert.Equal(colours, Preferences.Load(path).Colors);
        }
        finally { File.Delete(path); }
    }
}
