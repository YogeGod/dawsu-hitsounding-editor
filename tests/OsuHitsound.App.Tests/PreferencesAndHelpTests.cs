using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class PreferencesAndHelpTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-prefs-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Preferences_round_trip()
    {
        var path = Path.Combine(dir, "sub", "preferences.json");
        var prefs = new Preferences { SongsFolder = @"F:\osu!\Songs", AudioDevice = 3, BufferMs = 60, LazerResources = "x.dll", WindowWidth = 1400 }.WithRecent(@"C:\a.osu");
        prefs.Save(path);

        var loaded = Preferences.Load(path);
        Assert.Equal(prefs, loaded with { RecentMaps = prefs.RecentMaps });
        Assert.Equal(prefs.RecentMaps, loaded.RecentMaps);
    }

    [Fact]
    public void A_missing_or_broken_file_gives_defaults()
    {
        Assert.Equal(-1, Preferences.Load(Path.Combine(dir, "none.json")).AudioDevice);
        var broken = Path.Combine(dir, "broken.json");
        File.WriteAllText(broken, "{ not json");
        var loaded = Preferences.Load(broken);
        Assert.Equal(100, loaded.BufferMs);
        Assert.Empty(loaded.RecentMaps);
    }

    [Fact]
    public void Recent_maps_put_the_latest_first_without_duplicates_and_keep_ten()
    {
        var prefs = new Preferences();
        for (int i = 0; i < 12; i++) prefs = prefs.WithRecent($@"C:\m{i}.osu");
        prefs = prefs.WithRecent(@"C:\M5.OSU");

        Assert.Equal(Preferences.MaxRecent, prefs.RecentMaps.Count);
        Assert.Equal(@"C:\M5.OSU", prefs.RecentMaps[0]);
        Assert.Single(prefs.RecentMaps, p => p.Equals(@"C:\m5.osu", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(@"C:\m11.osu", prefs.RecentMaps[1]);
    }

    [Fact]
    public void Missing_maps_leave_the_recent_list()
    {
        var prefs = new Preferences().WithRecent(@"C:\gone.osu").WithRecent(@"C:\here.osu");
        Assert.Equal([@"C:\here.osu"], prefs.WithoutMissing(p => p.Contains("here")).RecentMaps);
    }

    [Fact]
    public void Songs_folder_is_found_in_a_usual_place()
    {
        Assert.Null(Preferences.FindSongsFolder(_ => false));
        Assert.EndsWith(Path.Combine("osu!", "Songs"), Preferences.FindSongsFolder(_ => true));
    }

    [Fact]
    public void Every_command_has_a_unique_id_and_unique_keys()
    {
        Assert.Equal(EditorCommands.All.Count, EditorCommands.All.Select(c => c.Id).Distinct().Count());
        var keys = EditorCommands.All.SelectMany(c => c.Keys).Select(k => k.ToString()).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(EditorCommands.All, c => Assert.NotEmpty(c.Keys));
    }

    [Fact]
    public void Help_lists_every_command_and_mouse_action_once_and_searches()
    {
        var rows = HelpWindow.Rows("").ToList();
        Assert.Equal(EditorCommands.All.Count + EditorCommands.Mouse.Count, rows.Count);
        foreach (var c in EditorCommands.All) Assert.Single(rows, r => r.Description == c.Description && r.Keys == c.KeysText);

        Assert.Contains(HelpWindow.Rows("bookmark"), r => r.Description == "Next bookmark");
        Assert.Empty(HelpWindow.Rows("zzz-nothing"));
    }
}

