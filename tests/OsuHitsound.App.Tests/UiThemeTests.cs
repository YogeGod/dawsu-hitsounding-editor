using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class UiThemeTests
{
    [Fact]
    public void Every_setting_has_a_unique_key_and_a_valid_default()
    {
        var settings = UiTheme.Parts.SelectMany(p => p.Settings).ToList();
        Assert.Equal(settings.Count, settings.Select(s => s.Key).Distinct().Count());
        Assert.All(settings, s => Assert.True(UiTheme.IsValid(s, s.Default), s.Key));
        Assert.Equal(UiTheme.Parts.Count, UiTheme.Parts.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Only_changed_values_are_saved_and_they_load_back()
    {
        var theme = new UiTheme();
        theme.Set("lane.tint", "0.5");
        theme.Set("title.bg", "#112233");
        theme.Set("row.height", "22"); // the default: not written

        var loaded = UiTheme.FromJson(theme.ToJson());

        Assert.Equal(0.5, loaded.Number("lane.tint"));
        Assert.Equal("#112233", loaded.Get("title.bg"));
        Assert.DoesNotContain("row.height", theme.ToJson());
    }

    [Fact]
    public void Unknown_keys_and_bad_values_in_the_file_are_ignored()
    {
        var theme = UiTheme.FromJson("""{ "nope": "1", "row.height": "999", "lane.text": "not a colour", "note.size": "14" }""");

        Assert.Equal(22, theme.Number("row.height"));
        Assert.Equal("#e4e4e6", theme.Get("lane.text"));
        Assert.Equal(14, theme.Number("note.size"));
    }

    [Fact]
    public void Invalid_edits_are_refused_and_change_nothing()
    {
        var theme = new UiTheme();
        int changes = 0;
        theme.Changed += () => changes++;

        Assert.Throws<FormatException>(() => theme.Set("row.height", "5"));
        Assert.Throws<FormatException>(() => theme.Set("colors.soft", "#c04fd0,purple-ish"));
        Assert.Equal(0, changes);
        Assert.Equal(22, theme.Number("row.height"));
    }

    [Fact]
    public void Edits_reach_the_cached_colours_at_once()
    {
        var theme = new UiTheme();
        Assert.Equal(Avalonia.Media.Color.Parse("#c04fd0"), theme.Colors("colors.soft")[0]);

        theme.Set("colors.soft", "#ff0000,#00ff00");

        Assert.Equal([Avalonia.Media.Color.Parse("#ff0000"), Avalonia.Media.Color.Parse("#00ff00")], theme.Colors("colors.soft"));
    }

    [Fact]
    public void Theme_file_saves_and_reloads_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ui-theme-{Guid.NewGuid():N}.json");
        try
        {
            var theme = new UiTheme();
            theme.Set("label.open", "Open map");
            theme.Save(path);
            Assert.Equal("Open map", UiTheme.Load(path).Get("label.open"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hand_written_numbers_in_the_file_are_read()
    {
        var theme = UiTheme.FromJson("""{ "row.height": 30, "title.bg": "#112233" }""");
        Assert.Equal(30, theme.Number("row.height"));
        Assert.Equal("#112233", theme.Get("title.bg"));
    }
}
