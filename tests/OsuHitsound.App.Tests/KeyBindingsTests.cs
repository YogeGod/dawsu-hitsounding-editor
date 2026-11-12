using System.Xml.Linq;
using Avalonia.Input;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class KeyBindingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-keys-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    /// <summary>The shape of Cubase's file: one key as a string, several as a list.</summary>
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <KeyCommands>
           <list name="Categories" type="list">
              <item>
                 <string name="Name" value="Edit"/>
                 <list name="Commands" type="list">
                    <item><string name="Name" value="Undo"/><string name="Key" value="Ctrl+Z" wide="true"/></item>
                    <item>
                       <string name="Name" value="Delete"/>
                       <list name="Key" type="string"><item value="Del"/><item value="Backspace"/></list>
                    </item>
                    <item><string name="Name" value="Group"/><string name="Key" value="Ctrl+G" wide="true"/></item>
                    <item><string name="Name" value="Repeat"/><string name="Key" value="Ctrl+K" wide="true"/></item>
                    <item><string name="Name" value="Unbound"/></item>
                 </list>
              </item>
              <item>
                 <string name="Name" value="Tool"/>
                 <list name="Commands" type="list">
                    <item><string name="Name" value="Draw Tool"/><string name="Key" value="8" wide="true"/></item>
                    <item><string name="Name" value="Range Tool"/><string name="Key" value="7" wide="true"/></item>
                    <item><string name="Name" value="Zoom Tool"/><string name="Key" value="6" wide="true"/></item>
                 </list>
              </item>
              <item>
                 <string name="Name" value="Transport"/>
                 <list name="Commands" type="list">
                    <item><string name="Name" value="Forward"/><string name="Key" value="Shift+Pad +" wide="true"/></item>
                 </list>
              </item>
           </list>
        </KeyCommands>
        """;

    private static IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> SampleKeys() => CubaseKeyFile.Parse(XDocument.Parse(Sample));

    private static IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>> NoOverrides => new Dictionary<string, IReadOnlyList<KeyGesture>>();

    private static string Keys(Dictionary<string, IReadOnlyList<KeyGesture>> map, string id) =>
        string.Join(", ", map.GetValueOrDefault(id, []).Select(EditorCommand.Name));

    [Theory]
    [InlineData("Ctrl+Z", Key.Z, KeyModifiers.Control)]
    [InlineData("Ctrl+Shift+Up Arrow", Key.Up, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Shift+Pad +", Key.Add, KeyModifiers.Shift)]
    [InlineData("Pad0", Key.NumPad0, KeyModifiers.None)]
    [InlineData("Pad /", Key.Divide, KeyModifiers.None)]
    [InlineData("Alt+,", Key.OemComma, KeyModifiers.Alt)]
    [InlineData("8", Key.D8, KeyModifiers.None)]
    [InlineData("F12", Key.F12, KeyModifiers.None)]
    [InlineData("Return", Key.Enter, KeyModifiers.None)]
    public void Cubase_key_texts_are_read(string text, Key key, KeyModifiers mods)
    {
        var g = CubaseKeyFile.ParseKey(text);
        Assert.NotNull(g);
        Assert.Equal((key, mods), (g.Key, g.KeyModifiers));
    }

    [Fact]
    public void Unknown_key_texts_are_skipped() => Assert.Null(CubaseKeyFile.ParseKey("Mystery Key"));

    [Fact]
    public void Cubase_file_gives_every_command_with_keys()
    {
        var keys = SampleKeys();
        Assert.Equal(["Ctrl+Z"], keys["Edit|Undo"].Select(EditorCommand.Name));
        Assert.Equal(["Del", "Backspace"], keys["Edit|Delete"].Select(EditorCommand.Name));
        Assert.False(keys.ContainsKey("Edit|Unbound"));
    }

    [Fact]
    public void Cubase_preset_takes_cubase_keys_and_keeps_built_in_keys_cubase_leaves_free()
    {
        var map = Keymap.Build(KeyPreset.Cubase, SampleKeys(), NoOverrides);
        Assert.Equal("Ctrl+Z", Keys(map, "undo"));
        // Tools keep the editor's own numbers (the toolbar's), whatever the Cubase file says: Draw is 3, not Cubase's 8,
        // and Cubase's Range Tool key doesn't take a tool key.
        Assert.Equal("1", Keys(map, "toolSelect"));
        Assert.Equal("2", Keys(map, "toolGrab"));
        Assert.Equal("3", Keys(map, "toolDraw"));
        Assert.Equal("4", Keys(map, "toolErase"));
        Assert.Equal("6", Keys(map, "toolRamp"));
        Assert.Equal("", Keys(map, "toolRange"));
        Assert.Equal(("7", "8"), (Keys(map, "ks.toolSplit"), Keys(map, "ks.toolGlue")));
        Assert.Equal("Ctrl+K", Keys(map, "repeat"));     // Cubase's Edit > Repeat, not the built-in Ctrl+R
        Assert.Equal("Q", Keys(map, "quantize"));        // no MIDI > Quantize key in this file: built-in
        Assert.Equal("Ctrl+N", Keys(map, "newDiff"));
    }

    [Fact]
    public void Osu_preset_uses_osu_editor_keys_over_built_in_ones()
    {
        var map = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        Assert.Equal("2", Keys(map, "toolDraw"));
        Assert.Equal("Space, X, C", Keys(map, "play"));
        // As in osu!'s editor: the arrows move the playhead by grid lines (Shift: four), so they don't select hits here.
        Assert.Equal(("Left", "Right", "Shift+Left", "Shift+Right"), (Keys(map, "seekBack"), Keys(map, "seekForward"), Keys(map, "seekBackFar"), Keys(map, "seekForwardFar")));
        Assert.Equal(("", ""), (Keys(map, "selectPrevious"), Keys(map, "selectNext")));
        Assert.Equal(("Z", "V"), (Keys(map, "first"), Keys(map, "last")));
        Assert.Equal("", Keys(map, "toolRange"));       // osu! uses 2 for placing circles, so Range has no key here
        Assert.Equal(("Q", "Ctrl+R", "Ctrl+N"), (Keys(map, "quantize"), Keys(map, "repeat"), Keys(map, "newDiff")));
        Assert.Equal("Ctrl+Z", Keys(map, "undo"));
    }

    [Fact]
    public void Alt_space_jumps_to_the_selection_in_both_presets()
    {
        Assert.Equal("Alt+Space", Keys(Keymap.Build(KeyPreset.Osu, null, NoOverrides), "jumpToSelection"));
        Assert.Equal("Alt+Space", Keys(Keymap.Build(KeyPreset.Cubase, SampleKeys(), NoOverrides), "jumpToSelection"));
    }

    [Fact]
    public void The_jump_goes_to_the_earliest_selected_hit_or_nowhere()
    {
        Assert.Equal(1500, MainWindow.SelectionStart([2000, 1500, 3000]));
        Assert.Null(MainWindow.SelectionStart([]));
    }

    [Fact]
    public void Every_preset_gives_each_key_to_one_command_only()
    {
        foreach (var map in new[] { Keymap.Build(KeyPreset.Osu, null, NoOverrides), Keymap.Build(KeyPreset.Cubase, SampleKeys(), NoOverrides) })
        {
            var all = map.Values.SelectMany(k => k).Select(k => k.ToString()).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
        }
    }

    [Fact]
    public void Adding_a_taken_key_moves_it_from_the_other_command()
    {
        var map = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        var ctrlZ = new KeyGesture(Key.Z, KeyModifiers.Control);
        Assert.Equal("undo", Keymap.Owner(map, ctrlZ, "copy"));

        var overrides = Keymap.AddKey(new Dictionary<string, List<string>>(), map, "copy", ctrlZ);
        var after = Keymap.Build(KeyPreset.Osu, null, Keymap.ParseOverrides(overrides));
        Assert.Equal("Ctrl+C, Ctrl+Z", Keys(after, "copy"));
        Assert.Equal("", Keys(after, "undo"));
    }

    [Fact]
    public void Cleared_command_has_no_key_and_its_key_stays_free()
    {
        var overrides = new Dictionary<string, List<string>> { ["undo"] = [] };
        var map = Keymap.Build(KeyPreset.Osu, null, Keymap.ParseOverrides(overrides));
        Assert.Equal("", Keys(map, "undo"));
        Assert.Null(Keymap.Owner(map, new KeyGesture(Key.Z, KeyModifiers.Control), "copy"));
    }

    [Fact]
    public void Stored_keys_round_trip()
    {
        foreach (var g in new[] { new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Shift), new KeyGesture(Key.OemComma), new KeyGesture(Key.Add, KeyModifiers.Alt) })
            Assert.Equal(g, Keymap.ParseStored(Keymap.Format(g)));
        Assert.Null(Keymap.ParseStored("nonsense"));
    }

    [Fact]
    public void Key_settings_survive_saving_preferences_and_are_kept_per_preset()
    {
        var path = Path.Combine(dir, "prefs.json");
        new Preferences
        {
            KeyPreset = KeyPreset.Osu,
            CubaseKeyFile = @"C:\x\Key Commands.xml",
            KeyOverrides = new() { ["Osu"] = new() { ["undo"] = ["Control+U"] } },
        }.Save(path);

        var loaded = Preferences.Load(path);
        Assert.Equal(KeyPreset.Osu, loaded.KeyPreset);
        Assert.Equal(@"C:\x\Key Commands.xml", loaded.CubaseKeyFile);
        Assert.Equal("Ctrl+U", Keys(Keymap.For(loaded).Keys, "undo"));
        Assert.Equal("Ctrl+Z", Keys(Keymap.For(loaded with { KeyOverrides = new() { ["Cubase"] = new() { ["undo"] = ["Control+U"] } } }).Keys, "undo"));
    }

    [Fact]
    public void Newest_cubase_folder_is_found()
    {
        foreach (var version in new[] { "Cubase 9.5_64", "Cubase 13_64", "Cubase 15_64", "Cubase 16_64" })
        {
            Directory.CreateDirectory(Path.Combine(dir, version));
            if (version != "Cubase 16_64") File.WriteAllText(Path.Combine(dir, version, "Key Commands.xml"), Sample);
        }

        Assert.Equal(Path.Combine(dir, "Cubase 15_64", "Key Commands.xml"), CubaseKeyFile.FindNewest(dir));
        Assert.Null(CubaseKeyFile.FindNewest(Path.Combine(dir, "nowhere")));
    }

    [Fact]
    public void Missing_cubase_file_falls_back_to_built_in_keys_with_a_note()
    {
        var (map, problem) = Keymap.For(new Preferences { CubaseKeyFile = Path.Combine(dir, "missing.xml") });
        Assert.NotNull(problem);
        Assert.Equal("3", Keys(map, "toolDraw"));
    }

    [Fact]
    public void A_command_that_lost_a_key_to_another_is_customized()
    {
        var preset = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        var overrides = Keymap.AddKey(new Dictionary<string, List<string>>(), preset, "copy", new KeyGesture(Key.Z, KeyModifiers.Control));
        var map = Keymap.Build(KeyPreset.Osu, null, Keymap.ParseOverrides(overrides));
        Assert.True(Keymap.IsCustomized(map, preset, "copy"));
        Assert.True(Keymap.IsCustomized(map, preset, "undo"));
        Assert.False(Keymap.IsCustomized(map, preset, "paste"));
    }

    [Fact]
    public void A_key_change_that_puts_back_the_preset_keys_is_not_customized()
    {
        var preset = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        var overrides = new Dictionary<string, List<string>> { ["undo"] = [.. preset["undo"].Select(Keymap.Format)] };
        var map = Keymap.Build(KeyPreset.Osu, null, Keymap.ParseOverrides(overrides));
        Assert.False(Keymap.IsCustomized(map, preset, "undo"));
    }

    /// <summary>Applies a read key file to a preset, the way Preferences does.</summary>
    private static Dictionary<string, IReadOnlyList<KeyGesture>> Import(string json, KeyPreset preset, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>? cubase, out KeyFile.ImportResult result)
    {
        result = KeyFile.Read(json, Keymap.Build(preset, cubase, NoOverrides));
        return Keymap.Build(preset, cubase, Keymap.ParseOverrides(result.Changes));
    }

    [Fact]
    public void A_written_key_file_reads_back_to_the_same_keys_on_either_preset()
    {
        var overrides = Keymap.AddKey(new Dictionary<string, List<string>>(), Keymap.Build(KeyPreset.Osu, null, NoOverrides), "copy", new KeyGesture(Key.Z, KeyModifiers.Control));
        var source = Keymap.Build(KeyPreset.Osu, null, Keymap.ParseOverrides(overrides));
        string json = KeyFile.Write("Osu", source);

        foreach (var (preset, cubase) in new[] { (KeyPreset.Osu, (IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>?)null), (KeyPreset.Cubase, SampleKeys()) })
        {
            var map = Import(json, preset, cubase, out var result);
            foreach (var c in EditorCommands.Defaults)
                Assert.Equal(Keys(source, c.Id), Keys(map, c.Id));
            Assert.Equal(EditorCommands.Defaults.Count, result.Imported);
            Assert.Equal((0, 0, 0), (result.UnknownCommands, result.DuplicateKeys, result.UnreadableKeys));
        }
    }

    [Fact]
    public void Importing_skips_unknown_commands_duplicate_and_unreadable_keys()
    {
        const string json = """
            { "version": 1, "preset": "Osu", "keys": {
                "copy": ["Control+Q"],
                "noSuchCommand": ["Control+W"],
                "paste": ["Control+Q", "nonsense", "Control+J"]
            } }
            """;
        var map = Import(json, KeyPreset.Osu, null, out var result);
        Assert.Equal("Ctrl+Q", Keys(map, "copy"));
        Assert.Equal("Ctrl+J", Keys(map, "paste"));
        Assert.Equal((2, 1, 1, 1), (result.Imported, result.UnknownCommands, result.DuplicateKeys, result.UnreadableKeys));
    }

    [Fact]
    public void Commands_a_key_file_leaves_out_keep_their_preset_keys_unless_the_file_uses_them()
    {
        var preset = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        string ctrlZ = Keymap.Format(new KeyGesture(Key.Z, KeyModifiers.Control));
        var map = Import($$"""{ "version": 1, "keys": { "copy": ["{{ctrlZ}}"] } }""", KeyPreset.Osu, null, out _);
        Assert.Equal("Ctrl+Z", Keys(map, "copy"));
        Assert.Equal("", Keys(map, "undo"));
        Assert.Equal(Keys(preset, "paste"), Keys(map, "paste"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": 2, "keys": {} }""")]
    [InlineData("""{ "version": 1 }""")]
    public void Files_that_are_not_key_files_are_refused(string json) =>
        Assert.Throws<FormatException>(() => KeyFile.Read(json, Keymap.Build(KeyPreset.Osu, null, NoOverrides)));

    /// <summary>The user's real file, when this machine has one: every mapped Cubase command name must exist in it.</summary>
    [Fact]
    public void Real_cubase_file_reads_and_mapped_commands_exist()
    {
        if (CubaseKeyFile.FindNewest() is not { } path) return;
        Assert.True(CubaseKeyFile.Read(path).Count > 100);
        // Every command, bound to a key or not: the user may have cleared a shortcut (the map then has no key for it).
        var doc = XDocument.Load(path);
        var names = doc.Root!.Elements("list").Elements("item").SelectMany(category =>
        {
            string? Name(XElement e) => e.Elements("string").FirstOrDefault(s => (string?)s.Attribute("name") == "Name")?.Attribute("value")?.Value;
            return category.Elements("list").Where(l => (string?)l.Attribute("name") == "Commands").Elements("item").Select(c => $"{Name(category)}|{Name(c)}");
        }).ToHashSet();
        var missing = CubaseKeyFile.CommandMap.Values.Where(c => !names.Contains(c)).ToList();
        Assert.Empty(missing);
    }
}
