using System.Text;

namespace OsuHitsound.Lanes.Tests;

public class SampleSlotTests
{
    private static Lane Lane(string name) => new(LaneSource.BeatmapFile, name);

    [Fact]
    public void Next_index_is_per_family_even_when_another_bank_uses_a_higher_index()
    {
        Lane[] slots = [Lane("soft-hitclap.wav"), Lane("soft-hitclap2.wav"), Lane("soft-hitclap3.wav"), Lane("normal-hitnormal9.wav")];
        var next = SampleSlots.Next(slots, slots[0]);
        Assert.Equal("soft-hitclap4.wav", next.Name);
        Assert.Equal("soft-hitclap4.wav", SampleSlots.BackingFile(new MapFolder(slots.Select(l => l.Name)), next, ".wav"));
    }

    [Fact]
    public void Shared_or_fallback_references_force_a_safe_backing_index_without_changing_the_editor_slot()
    {
        var map = Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,4,100,1,0\n[HitObjects]\n256,192,1000,1,8,0:0:0:0:\n");
        var folder = new MapFolder(["other.osu", "normal-hitnormal9.wav"], _ => map);
        var slot = Lane("soft-hitclap4.wav");
        Assert.Equal("soft-hitclap10.wav", SampleSlots.BackingFile(folder, slot, ".wav"));
        Assert.Equal(4, slot.Sample!.Value.Index);
    }

    [Theory]
    [InlineData("SOFT-HITCLAP3.OGG")]
    [InlineData("soft-hitclap3.mp3")]
    public void Replacement_never_overwrites_an_existing_extension_or_case_variant(string occupied)
    {
        var folder = new MapFolder([occupied, "normal-hitnormal9.wav"]);
        Assert.Equal("soft-hitclap10.wav", SampleSlots.BackingFile(folder, Lane("soft-hitclap3.wav"), ".wav"));
    }

    [Fact]
    public void Exhausted_indexes_and_unsupported_extensions_stop_before_writing()
    {
        Assert.Throws<IOException>(() => SampleSlots.Next([Lane("soft-hitclap2147483647.wav")], Lane("soft-hitclap.wav")));
        Assert.Throws<IOException>(() => SampleSlots.BackingFile(new MapFolder(["soft-hitclap2147483647.wav"]), Lane("soft-hitclap2147483647.wav"), ".wav"));
        Assert.Throws<ArgumentException>(() => SampleSlots.BackingFile(new MapFolder([]), Lane("soft-hitclap2.wav"), ".exe"));
    }
}
