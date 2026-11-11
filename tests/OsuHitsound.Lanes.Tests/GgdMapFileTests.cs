namespace OsuHitsound.Lanes.Tests;

public class GgdMapFileTests
{
    // GGD P4 Kit, mapping preset "Invasion", exported with EXPORT MAP (the export the user made for this).
    private static readonly string P4Invasion = "%ART__articulation_map\n-1\n24\n30\n27\n26\n28\n29\n33\n34\n35\n36\n-1\n44\n42\n45\n46\n47\n48\n43\n41\n17\n61\n62\n53\n52\n57\n56\n55\n54\n66\n65\n74\n73\n76\n75\n77\n78\n79\n"
        + string.Concat(Enumerable.Repeat("-1\n", 90));

    [Fact]
    public void An_export_reads_as_the_notes_its_Mappings_page_shows()
    {
        var kit = GgdMapFile.Parse(P4Invasion).ToDictionary(a => DrumKitMapping.Label(a), a => a.Note);
        // The P4 Kit's Mappings page (Kontakt names: C0 = 24).
        Assert.Equal(24, kit["Kick"]);        // C0
        Assert.Equal(26, kit["Snare"]);       // D0
        Assert.Equal(27, kit["Flam"]);        // D#0
        Assert.Equal(29, kit["Wires Off"]);   // F0
        Assert.Equal(28, kit["Ruff"]);        // E0
        Assert.Equal(30, kit["Sidestick"]);   // F#0
        Assert.Equal(33, kit["Rack Tom 1"]);  // A0
        Assert.Equal(36, kit["Floor Tom 2"]); // C1
        Assert.Equal(79, kit["Stick Click"]); // G4
        Assert.Equal(41, kit["Tip Tight"]);   // F1
        Assert.Equal(42, kit["Edge Tight"]);  // F#1
        Assert.Equal(43, kit["Tip Closed"]);  // G1
        Assert.Equal(44, kit["Edge Closed"]); // G#1
        Assert.Equal(47, kit["Open 3"]);      // B1
        Assert.Equal(48, kit["Pedal"]);       // C2
        Assert.Equal(17, kit["CC"]);          // F-1
        Assert.Equal(61, kit["Bell"]);        // C#3
        Assert.Equal(62, kit["Ride"]);        // D3
        Assert.Equal(65, kit["China"]);       // F3
        Assert.Equal(66, kit["China Choke"]); // F#3
        Assert.Equal(52, kit["Main Crash L"]);       // E2
        Assert.Equal(53, kit["Main Crash L Choke"]); // F2
        Assert.Equal(56, kit["Wide Crash L"]);       // G#2
        Assert.Equal(54, kit["Main Crash R"]);       // F#2
        Assert.Equal(55, kit["Main Crash R Choke"]); // G2
        Assert.Equal(73, kit["Splash L"]);    // C#4
        Assert.Equal(75, kit["Splash C"]);    // D#4
        Assert.Equal(76, kit["Splash C Choke"]); // E4
        Assert.Equal(77, kit["Mini Hats"]);   // F4
        Assert.Equal(78, kit["Stack"]);       // F#4
    }

    [Fact]
    public void An_exported_map_fills_the_starter_drum_map()
    {
        var rows = DrumKitMapping.Rows(GgdMapFile.Parse(P4Invasion), includeChokes: false);
        Assert.Equal(24, DrumKitMapping.Match("Kick", rows)!.Note);
        Assert.Equal(44, DrumKitMapping.Match("Closed Edge", rows)!.Note);
        Assert.Equal(52, DrumKitMapping.Match("Left Crash", rows)!.Note); // Main Crash L, not Wide Crash L (56)
        Assert.Equal(78, DrumKitMapping.Match("Stack", rows)!.Note);
    }

    [Theory]
    [InlineData("%ArticulationMap\n-1\n24\n")]
    [InlineData("hello")]
    public void Other_files_are_refused(string text) => Assert.Throws<FormatException>(() => GgdMapFile.Parse(text));

    [Fact]
    public void A_map_too_short_for_every_articulation_is_refused_not_misread()
    {
        // The header and 37 notes: one short of the last articulation line (37).
        string text = GgdMapFile.Header + "\n" + string.Concat(Enumerable.Repeat("24\n", 37));
        Assert.Throws<FormatException>(() => GgdMapFile.Parse(text));
        Assert.NotEmpty(GgdMapFile.Parse(text + "24\n"));
    }

    [Fact]
    public void Exports_are_found_in_the_libraries_data_folders_newest_first()
    {
        string root = Path.Combine(Path.GetTempPath(), "ggd-maps-" + Guid.NewGuid().ToString("N"));
        try
        {
            string data = Path.Combine(root, "P4 Matt Halpern Signature pack", "Data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "old.nka"), P4Invasion);
            File.SetLastWriteTimeUtc(Path.Combine(data, "old.nka"), new DateTime(2020, 1, 1));
            File.WriteAllText(Path.Combine(data, "new.nka"), P4Invasion);
            File.WriteAllText(Path.Combine(data, "other.nka"), "%SomethingElse\n1\n");
            var found = GgdMapFile.Find(root);
            Assert.Equal(["new", "old"], found.Select(f => f.Name));
            Assert.All(found, f => Assert.Equal("P4 Matt Halpern Signature pack", f.Library));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
