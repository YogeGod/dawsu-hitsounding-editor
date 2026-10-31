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
        Assert.Equal("8", Keys(map, "toolDraw"));        // from Cubase, not the built-in 3
        Assert.Equal("Ctrl+Z", Keys(map, "undo"));
        Assert.Equal("1", Keys(map, "toolSelect"));      // no Cubase key: built-in, which is free
        Assert.Equal("6", Keys(map, "toolRamp"));        // Cubase's Zoom Tool has 6, but this editor has no such command
        Assert.Equal("4", Keys(map, "toolErase"));       // no key in this file: built-in
        Assert.Equal("7", Keys(map, "toolRange"));       // Cubase's Range Tool
        Assert.Equal("Ctrl+K", Keys(map, "repeat"));     // Cubase's Edit > Repeat, not the built-in Ctrl+R
        Assert.Equal("Q", Keys(map, "quantize"));        // no MIDI > Quantize key in this file: built-in
        Assert.Equal("Ctrl+N", Keys(map, "newDiff"));
    }

    [Fact]
    public void Osu_preset_uses_osu_editor_keys_over_built_in_ones()
    {
        var map = Keymap.Build(KeyPreset.Osu, null, NoOverrides);
        Assert.Equal("2", Keys(map, "toolDraw"));
        Assert.Equal("Space, X", Keys(map, "play"));
        Assert.Equal("", Keys(map, "toolRange"));       // osu! uses 2 for placing circles, so Range has no key here
        Assert.Equal(("Q", "Ctrl+R", "Ctrl+N"), (Keys(map, "quantize"), Keys(map, "repeat"), Keys(map, "newDiff")));
        Assert.Equal("Ctrl+Z", Keys(map, "undo"));
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

    /// <summary>The user's real file, when this machine has one: every mapped Cubase command name must exist in it.</summary>
    [Fact]
    public void Real_cubase_file_reads_and_mapped_commands_exist()
    {
        if (CubaseKeyFile.FindNewest() is not { } path) return;
        var keys = CubaseKeyFile.Read(path);
        Assert.True(keys.Count > 100);
        var missing = CubaseKeyFile.CommandMap.Values.Where(c => !keys.ContainsKey(c)).ToList();
        Assert.Empty(missing);
    }
}
