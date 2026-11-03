using System.Text;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class LanePreviewTests
{
    private static readonly Lane DefaultClap = new(LaneSource.Skin, "normal-hitclap");

    private static MapDocument Document(string[] files, int version = 14) => MapDocument.FromBytes("preview.osu",
        Encoding.UTF8.GetBytes($"osu file format v{version}\r\n[General]\r\nSampleSet:Soft\r\n[TimingPoints]\r\n" +
            "0,500,4,2,1,100,1,0\r\n1000,-100,4,2,2,100,0,0\r\n2000,-100,4,2,0,100,0,0\r\n3000,-100,4,2,101,100,0,0\r\n"),
        new MapFolder(files));

    [Theory]
    [InlineData(0, "normal-hitclap.wav")]
    [InlineData(994, "normal-hitclap.wav")]
    [InlineData(995, "normal-hitclap2.ogg")]
    [InlineData(1500, "normal-hitclap2.ogg")]
    [InlineData(3000, "normal-hitclap101.wav")]
    public void Default_row_uses_playhead_index_and_clicked_sampleset(double time, string expected)
    {
        var doc = Document(["normal-hitclap.wav", "normal-hitclap2.ogg", "normal-hitclap101.wav", "soft-hitclap2.wav"]);
        var lane = doc.LanePreviewAt(DefaultClap, time);
        Assert.Equal(LaneSource.BeatmapFile, lane.Source);
        Assert.Equal(expected, lane.Name);
    }

    [Fact]
    public void Skin_index_and_missing_numbered_samples_fall_back_without_using_another_index()
    {
        var doc = Document(["normal-hitclap.wav", "normal-hitclap3.wav"]);
        Assert.Equal(DefaultClap, doc.LanePreviewAt(DefaultClap, 2000));
        Assert.Equal(DefaultClap, doc.LanePreviewAt(DefaultClap, 1000));
    }

    [Fact]
    public void File_rows_preview_the_exact_file_independent_of_playhead()
    {
        var doc = Document(["normal-hitclap2.wav", "boom.wav"]);
        foreach (var lane in new[] { new Lane(LaneSource.BeatmapFile, "normal-hitclap2.wav"), new Lane(LaneSource.BeatmapFile, "boom.wav") })
        {
            Assert.Same(lane, doc.LanePreviewAt(lane, 0));
            Assert.Same(lane, doc.LanePreviewAt(lane, 2000));
        }
    }

    [Fact]
    public void Preview_respects_extension_priority_and_legacy_timeline_offset()
    {
        var doc = Document(["normal-hitclap.wav", "normal-hitclap2.ogg", "NORMAL-HITCLAP2.MP3", "Normal-Hitclap2.WAV"], version: 4);
        Assert.Equal("normal-hitclap.wav", doc.LanePreviewAt(DefaultClap, 1018).Name);
        Assert.Equal("Normal-Hitclap2.WAV", doc.LanePreviewAt(DefaultClap, 1019).Name);
    }

    [Fact]
    public void Custom_audition_renders_custom_audio_without_replacing_default_note_audio_or_editing_map()
    {
        var doc = Document(["normal-hitclap2.wav"]);
        var before = doc.File.ToBytes();
        var state = doc.Session.State;
        var custom = doc.LanePreviewAt(DefaultClap, 1000);
        var mixer = new PlaybackMixer();
        mixer.SetSound(DefaultClap, [0.25f, 0.25f]);
        mixer.SetSound(custom, [0.75f, 0.75f]);
        var output = new float[2];
        mixer.Preview(custom, 1);
        mixer.Read(output);
        Assert.Equal([0.75f, 0.75f], output);
        mixer.Preview(DefaultClap, 1);
        mixer.Read(output);
        Assert.Equal([0.25f, 0.25f], output);
        Assert.Equal(before, doc.File.ToBytes());
        Assert.Same(state, doc.Session.State);
        Assert.False(doc.Session.IsEdited);
    }
}
