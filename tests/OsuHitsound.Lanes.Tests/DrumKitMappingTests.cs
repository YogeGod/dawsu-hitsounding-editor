namespace OsuHitsound.Lanes.Tests;

public class DrumKitMappingTests
{
    [Theory]
    [InlineData("KICK", "Hit", "Kick")]
    [InlineData("SNARE", "Hit", "Snare")]
    [InlineData("SNARE", "Flam", "Flam")]
    [InlineData("SNARE", "Wires Off", "Wires Off")]
    [InlineData("TOMS", "Rack Tom 1: Hit", "Rack Tom 1")]
    [InlineData("CRASHES", "Main Crash L: Choke", "Main Crash L Choke")]
    [InlineData("CHINA", "Choke", "China Choke")]
    [InlineData("MINI HATS", "Hit", "Mini Hats")]
    [InlineData("RIDE", "Bell", "Bell")]
    public void Hits_are_named_after_their_drum_and_other_articulations_keep_their_own_name(string group, string item, string label) =>
        Assert.Equal(label, DrumKitMapping.Label(new(group, item, 1)));

    [Fact]
    public void The_GGD_Matt_Garstka_kit_maps_its_mapped_notes_and_leaves_chokes_out()
    {
        var kit = DrumKitMapping.Known("GGD Matt Garstka Signature Drums")!;
        var rows = DrumKitMapping.Rows(kit, includeChokes: false);
        Assert.Equal(
            ["Kick", "Snare", "Flam", "Wires Off", "Ruff", "Sidestick", "Rack Tom 1", "Rack Tom 2", "Floor Tom 1", "Floor Tom 2",
             "Tip Tight", "Edge Tight", "Tip Closed", "Open 1", "Open 2", "Open 3", "Pedal", "Bell", "Ride", "China", "Main Crash L", "Main Crash R", "Splash L"],
            rows.Select(r => r.Name));
        Assert.Equal(36, rows[0].Note);
        Assert.Equal(37, rows.Single(r => r.Name == "Snare").Note);
        Assert.Equal(74, rows.Single(r => r.Name == "Bell").Note);
        Assert.DoesNotContain(rows, r => r.Name is "Stick Click" or "Edge Closed" or "Mini Hats" or "Stack"); // unmapped ("---")
    }

    [Fact]
    public void Chokes_are_added_when_asked_for()
    {
        var rows = DrumKitMapping.Rows(DrumKitMapping.Known("GGD Matt Garstka Signature Drums")!, includeChokes: true);
        Assert.Equal(["China Choke", "Main Crash L Choke", "Main Crash R Choke", "Splash L Choke"],
            rows.Where(r => r.Name.EndsWith("Choke")).Select(r => r.Name));
        Assert.Equal(59, rows.Single(r => r.Name == "Main Crash L Choke").Note);
    }

    [Fact]
    public void Each_kind_of_drum_goes_to_the_usual_osu_sound()
    {
        var rows = DrumKitMapping.Rows(DrumKitMapping.Known("GGD Matt Garstka Signature Drums")!, includeChokes: false).ToDictionary(r => r.Name, r => r.Sound);
        Assert.Equal("hitnormal", rows["Kick"]);
        Assert.Equal("hitnormal", rows["Floor Tom 2"]);
        Assert.Equal("hitclap", rows["Snare"]);
        Assert.Equal("hitclap", rows["Sidestick"]);
        Assert.Equal("hitwhistle", rows["Tip Tight"]);
        Assert.Equal("hitwhistle", rows["Pedal"]);
        Assert.Equal("hitwhistle", rows["Bell"]);
        Assert.Equal("hitfinish", rows["China"]);
        Assert.Equal("hitfinish", rows["Splash L"]);
    }

    [Theory]
    [InlineData("Kick", "Kick")]
    [InlineData("Snare", "Snare")]
    [InlineData("Ghost Notes", "Snare")]
    [InlineData("T1", "Rack Tom 1")]
    [InlineData("T4", "Floor Tom 2")]
    [InlineData("Tight Tip", "Tip Tight")]
    [InlineData("Tight Edge", "Edge Tight")]
    [InlineData("Closed Tip", "Tip Closed")]
    [InlineData("Hi-Hat Open 2", "Open 2")]
    [InlineData("Ride Bell", "Bell")]
    [InlineData("Bow Ride (Low)", "Ride")]
    [InlineData("Right Crash", "Main Crash R")]
    [InlineData("Left Crash", "Main Crash L")]
    [InlineData("China", "China")]
    [InlineData("Splash", "Splash L")]
    public void The_starter_drum_map_rows_find_their_kit_instrument(string row, string kit)
    {
        var rows = DrumKitMapping.Rows(DrumKitMapping.Known("GGD Matt Garstka Signature Drums")!, includeChokes: false);
        Assert.Equal(kit, DrumKitMapping.Match(row, rows)?.Name);
    }

    [Fact]
    public void The_built_in_kits_can_be_listed_and_used()
    {
        var (name, kit) = Assert.Single(DrumKitMapping.BuiltIn);
        Assert.Contains("Matt Garstka", name);
        Assert.Equal(36, DrumKitMapping.Rows(kit, includeChokes: false)[0].Note);
    }

    [Fact]
    public void Rows_the_kit_has_nothing_for_stay_unmatched()
    {
        var rows = DrumKitMapping.Rows(DrumKitMapping.Known("GGD Matt Garstka Signature Drums")!, includeChokes: false);
        // GGD's "Edge Closed" and "Stack" are unmapped ("---"), and it has no ride crash or mini stacks.
        Assert.All(new[] { "Closed Edge", "Stack", "Ride Crash", "Mini Stack Left" }, name => Assert.Null(DrumKitMapping.Match(name, rows)));
    }

    [Fact]
    public void Reported_note_names_become_rows_in_note_order_and_unknown_kits_have_no_table()
    {
        Assert.Null(DrumKitMapping.Known("Some Other Drums"));
        var kit = DrumKitMapping.FromNoteNames(new Dictionary<int, string> { [38] = "Snare", [36] = "Kick", [40] = " ", [200] = "Bad" });
        var rows = DrumKitMapping.Rows(kit, includeChokes: false);
        Assert.Equal([("Kick", 36, "hitnormal"), ("Snare", 38, "hitclap")], rows.Select(r => (r.Name, r.Note, r.Sound)));
    }
}
