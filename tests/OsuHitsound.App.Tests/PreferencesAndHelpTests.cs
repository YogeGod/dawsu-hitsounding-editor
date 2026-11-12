using OsuHitsound.Audio;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class PreferencesAndHelpTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-prefs-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Hitnormal_protection_defaults_off_and_can_be_enabled()
    {
        Assert.False(new Preferences().ProtectHitnormals);
        var path = Path.Combine(dir, "protection.json");
        new Preferences { ProtectHitnormals = true }.Save(path);
        Assert.True(Preferences.Load(path).ProtectHitnormals);
    }

    [Fact]
    public void Objects_snap_mode_is_remembered()
    {
        var path = Path.Combine(dir, "objects-snap.json");
        new Preferences { SnapMode = SnapMode.Objects }.Save(path);
        Assert.Equal(SnapMode.Objects, Preferences.Load(path).SnapMode);
    }

    [Fact]
    public void Preferences_round_trip()
    {
        var path = Path.Combine(dir, "sub", "preferences.json");
        var prefs = new Preferences
        {
            SongsFolder = @"F:\osu!\Songs", AudioOutput = OutputKind.Asio, OutputDevice = "ASIO4ALL v2", BufferMs = 60, LazerResources = "x.dll", WindowWidth = 1400,
            SnapMode = SnapMode.GridRelative, QuantizeStrength = 60, AuditionOnPlacement = false, InsertVolume = 37, InspectorOpen = false, InspectorWidth = 300,
            InspectorCards = ["recent", "selection"], InspectorFolded = ["bookmarks"], KeysoundCards = ["midi", "tracks"], KeysoundFolded = ["instrument"], KeysoundInspectorOpen = false, GlobalVolume = 35, TutorialSeen = true, GlobalSongOffset = -25, ClassicBrowser = true,
        }.WithRecent(@"C:\a.osu");
        prefs.Save(path);

        var loaded = Preferences.Load(path);
        // Lists compare by reference in a record: they're checked one by one below.
        Assert.Equal(prefs, loaded with { RecentMaps = prefs.RecentMaps, KeyOverrides = prefs.KeyOverrides, InspectorCards = prefs.InspectorCards, InspectorFolded = prefs.InspectorFolded,
            KeysoundCards = prefs.KeysoundCards, KeysoundFolded = prefs.KeysoundFolded });
        Assert.Equal(prefs.InspectorCards, loaded.InspectorCards);
        Assert.Equal(prefs.InspectorFolded, loaded.InspectorFolded);
        Assert.Equal(prefs.KeysoundCards, loaded.KeysoundCards);
        Assert.Equal(prefs.KeysoundFolded, loaded.KeysoundFolded);
        Assert.Equal(prefs.RecentMaps, loaded.RecentMaps);
    }

    [Theory]
    [InlineData(-8, -17, -25)]
    [InlineData(0, -17, -17)]
    [InlineData(10000, 1000, 10000)]
    [InlineData(0, 1e300, 1000)]
    [InlineData(double.NaN, -17, -17)]
    [InlineData(8, double.PositiveInfinity, 8)]
    public void Global_and_map_offsets_are_combined_without_nonfinite_or_out_of_range_values(double map, double global, double expected) =>
        Assert.Equal(expected, MainWindow.EffectiveSongOffset(map, global));

    [Fact]
    public void A_missing_or_broken_file_gives_defaults()
    {
        Assert.Equal(OutputKind.WasapiShared, Preferences.Load(Path.Combine(dir, "none.json")).AudioOutput);
        var broken = Path.Combine(dir, "broken.json");
        File.WriteAllText(broken, "{ not json");
        var loaded = Preferences.Load(broken);
        Assert.Equal(30, loaded.BufferMs);
        Assert.Equal(20, loaded.GlobalVolume);
        Assert.Equal(20, Preferences.Load(Path.Combine(dir, "none.json")).GlobalVolume);
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
        Assert.Equal(EditorCommands.Defaults.Count, EditorCommands.Defaults.Select(c => c.Id).Distinct().Count());
        var keys = EditorCommands.Defaults.SelectMany(c => c.Keys).Select(k => k.ToString()).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        // These menu commands have no built-in key; users can assign one in Preferences. The grid-line seeks have keys
        // only in the osu! editor preset (the arrows), where the built-in arrows select hits instead.
        string[] menuOnly = ["toolRange", "editRange", "scaleByVolume", "quantizeDialog", "importOsz", "openFromOsu", "openHitsoundFolder", "reviewOutput", "ks.sync", "ks.generate",
            "seekBack", "seekForward", "seekBackFar", "seekForwardFar"];
        Assert.All(EditorCommands.Defaults.Where(c => !menuOnly.Contains(c.Id)), c => Assert.NotEmpty(c.Keys));
    }

    [Fact]
    public void Help_lists_every_command_once_per_area_and_every_mouse_action_once_and_searches()
    {
        var rows = HelpWindow.Rows("").ToList();
        int shared = EditorCommands.All.Count(c => c.KeysoundGroup is not null);
        Assert.Equal(EditorCommands.All.Count + shared + EditorCommands.Mouse.Count, rows.Count);
        foreach (var c in EditorCommands.All)
        {
            Assert.Single(rows, r => r.Group == c.Group && r.Description == c.Description && r.Keys == c.KeysText);
            if (c.KeysoundGroup is { } ks) Assert.Single(rows, r => r.Group == ks && r.Description == c.Description && r.Keys == c.KeysText);
        }

        Assert.Contains(HelpWindow.Rows("bookmark"), r => r.Description == "Next bookmark");
        Assert.Empty(HelpWindow.Rows("zzz-nothing"));
    }

    [Fact]
    public void Help_groups_are_feature_areas_in_area_order()
    {
        var groups = HelpWindow.Rows("").Select(r => r.Group).ToList();
        Assert.All(groups, g => Assert.True(Areas.Order(g) < Areas.Order("not an area"), $"'{g}' is not a feature area"));
        Assert.Equal(groups.OrderBy(Areas.Order), groups);
        // A command both editors run is listed under each mode's area, the key editor's own only under Keysound.
        Assert.Equal(Areas.HsHitEditing, EditorCommands.Get("undo").Group);
        Assert.Equal(Areas.KsNoteEditing, EditorCommands.Get("undo").KeysoundGroup);
        Assert.Equal(Areas.KsPlayback, EditorCommands.Get("play").KeysoundGroup);
        Assert.Null(EditorCommands.Get("ks.sync").KeysoundGroup);
        Assert.Null(EditorCommands.Get("save").KeysoundGroup);
    }
}
