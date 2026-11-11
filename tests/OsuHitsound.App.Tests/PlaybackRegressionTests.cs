using System.Text;
using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class PlaybackRegressionTests
{
    private static ArrangeModel Model()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,5,1,0\n[HitObjects]\n256,192,1000,1,0,0:0:0:0:\n"));
        var folder = new MapFolder([]);
        return new(new EditSession(file, folder, Decompiler.Decompile(file, folder)), file, new HashSet<Lane>());
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(5, 0.04f)]
    [InlineData(100, 0.8f)]
    public void Linked_layers_play_at_exact_gain_while_map_hits_keep_stables_minimum(int gain, float expected)
    {
        var model = Model();
        var lane = new Lane(LaneSource.BeatmapFile, "soft-hitwhistle3.wav");
        var set = new SoundSet(Guid.NewGuid(), "Whistle", [new(Guid.NewGuid(), ProjectStore.KeyOf(lane), lane, "soft-hitwhistle", 80, [1, 2, 3])]);
        model.Session.SaveSoundSet(set);
        var placed = Assert.Single(model.Session.ApplySoundSet(set.Id, 1000));
        model.Session.SetSoundSetGain(placed.SetPlacement, gain);

        var cues = MainWindow.PlaybackCues(model).ToList();
        Assert.Equal(expected, Assert.Single(cues, c => c.Lane == lane).Volume);
        Assert.Equal(0.08f, Assert.Single(cues, c => c.Lane.Source == LaneSource.Skin).Volume);
        foreach (var hit in model.Session.State.Hits)
            Assert.Equal(cues.Single(c => c.Lane == hit.Lane && c.Time == hit.Time).Volume, MainWindow.AuditionGain(hit, model.Session.VolumeOf(hit)));

        var mixer = new PlaybackMixer { Length = 2000 };
        mixer.SetSound(lane, [1, 1]);
        mixer.SetCues(new HitScheduler(cues));
        mixer.Seek(1000);
        mixer.Play();
        var output = new float[2];
        mixer.Read(output);
        Assert.Equal(new[] { expected, expected }, output);
    }

    [Fact]
    public void Playback_excludes_preview_mutes_without_changing_the_edit_state()
    {
        var model = Model();
        var hit = Assert.Single(model.Session.State.Hits);
        var state = model.Session.State;
        model.TogglePreviewMute(hit);
        Assert.Empty(MainWindow.PlaybackCues(model));
        model.TogglePreviewMute(hit);
        Assert.Single(MainWindow.PlaybackCues(model));
        Assert.Same(state, model.Session.State);
    }

    [Fact]
    public void A_declined_reload_is_skipped_until_the_bytes_change()
    {
        byte[] open = [1, 2, 3], same = [1, 2, 3], changed = [1, 2, 4];
        Assert.True(MainWindow.SkipMapReload(open, open, []));
        Assert.True(MainWindow.SkipMapReload(same, open, same));
        Assert.False(MainWindow.SkipMapReload(changed, open, same));
        Assert.True(MainWindow.SkipMapReload([], open, []));
    }
}
