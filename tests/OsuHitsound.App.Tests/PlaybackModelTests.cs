using System.Text;
using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class PlaybackModelTests
{
    private static (DecompiledMap Map, OsuFile File) Load(string timingPoints, string hitObjects, string[]? files = null)
    {
        var text = "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n" +
                   "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
                   $"[TimingPoints]\r\n{timingPoints.Replace("\n", "\r\n")}\r\n\r\n[HitObjects]\r\n{hitObjects.Replace("\n", "\r\n")}\r\n";
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(text));
        return (Decompiler.Decompile(file, new MapFolder(files ?? [])), file);
    }

    private static EditSession Session(DecompiledMap map, OsuFile file) => new(file, new MapFolder([]), map);

    [Fact]
    public void Scheduler_starts_each_hit_once_when_the_playhead_passes_it()
    {
        var (map, _) = Load("0,500,4,2,0,100,1,0", "256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:");
        var scheduler = new HitScheduler(map);

        Assert.Empty(scheduler.Between(0, 999));
        Assert.Equal(["soft-hitnormal", "soft-hitclap"], scheduler.Between(999, 1000).Select(c => c.Lane.Name));
        Assert.Empty(scheduler.Between(1000, 1499)); // already started at 1000
        Assert.Single(scheduler.Between(1499, 2000));
    }

    [Fact]
    public void Scheduler_plays_ticks_and_looped_slides_at_stables_minimum_volume()
    {
        // A two-beat slider (280 px at 140 px/beat) has one tick halfway; the timing point's volume is 5 %.
        var (map, _) = Load("0,500,4,2,0,5,1,0", "100,100,1000,2,0,L|380:100,1,280");
        var cues = new HitScheduler(map).Cues;

        Assert.Contains(cues, c => c.Lane.Name == "soft-slidertick" && Math.Abs(c.Time - 1500) < 0.01);
        Assert.Contains(cues, c => c.Lane.Name == "soft-sliderslide" && c.EndTime > c.Time);
        Assert.All(cues, c => Assert.Equal(0.08f, c.Volume));
    }

    [Fact]
    public void Copied_slide_samples_are_decompiled_and_scheduled_after_reopening()
    {
        var (_, source) = Load("0,1000,4,1,0,80,1,0", "256,192,500,1,8,0:0:0:0:");
        var (_, target) = Load("0,1000,4,1,0,25,1,0", "256,192,0,2,0,L|396:192,1,140,0|0,0:0|0:0,2:0:0:0:");
        var folder = new MapFolder([]);
        var options = new HitsoundCopyOptions { Mode = CopyMode.OverwriteOnlyDefined, CopyToSliderSlides = true, CopyBodyHitsounds = false };
        var plan = HitsoundCopier.Plan(source, folder, target, folder, options, new CustomSampleIndexes(folder, 100));
        Compiler.Apply(target, folder, plan.Wanted);
        plan.ApplyExtras(target);
        var reopened = Decompiler.Decompile(OsuFile.Parse(target.ToBytes()), new MapFolder(plan.Mixes.Select(m => m.FileName)));

        var copied = Assert.Single(new HitScheduler(reopened).Cues, c => c.Lane.Name == "soft-sliderslide100.wav");
        Assert.Equal(500, copied.Time);
        Assert.Equal(1000, copied.EndTime!.Value, 6);
        Assert.Equal(0.8f, copied.Volume);

        var mixer = new PlaybackMixer { Length = 2000 };
        mixer.SetCues(new HitScheduler(reopened));
        mixer.SetSound(copied.Lane, [0.5f, 0.5f]);
        mixer.Seek(500);
        mixer.Play();
        var audio = new float[882];
        mixer.Read(audio);
        Assert.All(audio, value => Assert.Equal(0.4f, value));
    }

    [Fact]
    public void Lanes_are_flat_and_numbered_with_their_index()
    {
        var (map, file) = Load("0,500,4,3,2,100,1,0", "256,192,1000,1,8,0:0:0:0:\n256,192,1500,1,8,0:0:3:0:",
            ["drum-hitnormal2.wav", "drum-hitclap2.wav", "drum-hitclap3.ogg"]);
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());

        // The default sounds sit in their own set's group: no separate skin group.
        Assert.Empty(model.Rows.OfType<GroupRow>());
        Assert.Equal(Enumerable.Range(1, model.LaneRows.Count), model.LaneRows.Select(r => r.Number));
        Assert.Equal(3, model.LaneRows.Single(r => r.Lane.Name == "drum-hitclap3.ogg").Index);
        Assert.All(model.LaneRows.Where(r => r.Lane.Source == LaneSource.Skin), r => Assert.Equal(0, r.Index));
        Assert.Equal(12, model.LaneRows.Count(r => r.Lane.Source == LaneSource.Skin)); // every skin sound can be drawn
    }

    [Fact]
    public void Each_sampleset_group_starts_with_its_default_sounds_in_osu_order()
    {
        // A whistle and a clap from index 3 files, plus the hitnormal from index 2: files sit under their default sound.
        var (map, file) = Load("0,500,4,3,3,100,1,0\n1500,500,4,3,2,100,0,0", "256,192,1000,1,10,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:",
            ["drum-hitclap3.ogg", "drum-hitwhistle3.wav", "drum-hitnormal2.wav", "drum-hitnormal3.wav"]);
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());

        Assert.Equal(["drum-hitnormal", "drum-hitnormal2.wav", "drum-hitnormal3.wav", "drum-hitwhistle", "drum-hitwhistle3.wav", "drum-hitfinish", "drum-hitclap", "drum-hitclap3.ogg"],
            model.LaneRows.Where(r => r.Group == LaneGroup.DrumSet).Select(r => r.Lane.Name));
        // A sample added later goes under its own sound, by number.
        model.AddLane(new Lane(LaneSource.BeatmapFile, "drum-hitwhistle5.wav"));
        Assert.Equal(["drum-hitwhistle", "drum-hitwhistle3.wav", "drum-hitwhistle5.wav", "drum-hitfinish"],
            model.LaneRows.Select(r => r.Lane.Name).SkipWhile(n => n != "drum-hitwhistle").Take(4));
        Assert.Equal(LaneGroup.SoftSet, ArrangeModel.DisplayGroup(new Lane(LaneSource.Skin, "soft-hitclap")));
        Assert.Equal(LaneGroup.Skin, new Lane(LaneSource.Skin, "soft-hitclap").Group); // the map's own grouping is unchanged
    }

    [Fact]
    public void The_built_in_default_sounds_fill_in_what_lazer_does_not_have()
    {
        var bundled = DefaultSkin.Bundled();
        Assert.Equal(12, bundled.Count);
        Assert.All(bundled.Values, wav => Assert.Equal("RIFF"u8.ToArray(), wav[..4]));

        var lazerClap = new byte[] { 1, 2, 3 };
        var merged = DefaultSkin.WithFallback(new Dictionary<string, byte[]> { ["soft-hitclap"] = lazerClap, ["soft-slidertick"] = lazerClap });
        Assert.Same(lazerClap, merged["soft-hitclap"]); // lazer's own sound wins
        Assert.Equal(bundled["drum-hitnormal"], merged["drum-hitnormal"]);
        Assert.Equal(13, merged.Count);
    }

    [Fact]
    public void Legacy_group_collapse_cannot_hide_lanes_in_the_flat_list()
    {
        var (map, file) = Load("0,500,4,2,0,100,1,0", "256,192,1000,1,14,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());
        int before = model.Rows.Count;

        model.ToggleCollapsed(LaneGroup.SoftSet);

        Assert.Empty(model.Rows.OfType<GroupRow>());
        Assert.Contains(model.Rows.OfType<LaneRow>(), r => r.Group == LaneGroup.SoftSet);
        Assert.Equal(before, model.Rows.Count);
    }

    [Fact]
    public void Solo_silences_every_other_lane_and_mute_wins_over_solo()
    {
        var (map, file) = Load("0,500,4,2,0,100,1,0", "256,192,1000,1,8,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());
        var (normal, clap) = (model.LaneRows.Single(r => r.Lane.Name == "soft-hitnormal").Lane, model.LaneRows.Single(r => r.Lane.Name == "soft-hitclap").Lane);

        model.ToggleSolo(clap);
        Assert.False(model.IsAudible(normal));
        Assert.True(model.IsAudible(clap));

        model.ToggleMute(clap);
        Assert.False(model.IsAudible(clap));

        model.ToggleGroupSolo(LaneGroup.SoftSet);
        Assert.True(model.IsAudible(normal));
    }

    [Fact]
    public void Soloing_the_song_silences_the_lanes_except_soloed_ones()
    {
        var (map, file) = Load("0,500,4,2,0,100,1,0", "256,192,1000,1,8,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());
        var (normal, clap) = (model.LaneRows.Single(r => r.Lane.Name == "soft-hitnormal").Lane, model.LaneRows.Single(r => r.Lane.Name == "soft-hitclap").Lane);

        model.ToggleSongSolo();
        Assert.False(model.IsAudible(normal));
        Assert.False(model.IsAudible(clap));

        model.ToggleSolo(clap); // song and clap together
        Assert.True(model.IsAudible(clap));
        Assert.False(model.IsAudible(normal));

        model.ToggleSongSolo();
        Assert.True(model.IsAudible(clap));
    }

    [Theory]
    [InlineData(1000, 2, 1, 1)]   // bar lines run back from the first red line, so 1000 ms is bar 2
    [InlineData(1250, 2, 1, 9)]   // half a beat = tick 9 of 16
    [InlineData(2500, 3, 2, 1)]   // the 3/4 red line at 2000 starts bar 3; 2500 is its second beat
    public void Lcd_shows_bar_beat_and_tick(double time, int bar, int beat, int tick)
    {
        var (map, file) = Load("1000,500,4,2,0,100,1,0\n2000,500,3,2,0,100,1,0", "256,192,1000,1,0,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());
        Assert.Equal((bar, beat, tick), model.BarBeatTick(time));
    }

    [Fact]
    public void Bars_restart_at_every_red_line_with_its_meter()
    {
        var (map, file) = Load("1000,500,4,2,0,100,1,0\n2000,500,3,2,0,100,1,0", "256,192,1000,1,0,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());
        var bars = model.GridLines(0, 4000).Where(l => l.IsBar).Select(l => (l.Time, l.Bar)).ToList();

        Assert.Equal([(-1000.0 + 2000, 2), (2000.0, 3), (3500.0, 4)], bars.Where(b => b.Time >= 1000));
    }

    [Fact]
    public void Default_skin_sounds_load_from_the_lazer_install()
    {
        if (DefaultSkin.FindLazerResources() is not { } dll) return; // osu!lazer not installed here

        var samples = DefaultSkin.LoadSamples(dll);

        foreach (var name in new[] { "soft-hitnormal", "normal-hitclap", "drum-hitfinish", "soft-slidertick" })
            Assert.True(samples.TryGetValue(name, out var bytes) && bytes.Length > 44, name);
    }

    [Fact]
    public void A_tiny_beat_length_is_clamped_so_the_grid_stays_small()
    {
        var (map, file) = Load("0,0.0001,4,2,0,100,1,0", "256,192,1000,1,0,0:0:0:0:");
        var model = new ArrangeModel(Session(map, file), file, new HashSet<Lane>());

        // 6 ms beats (lazer's minimum): about 1667 lines in 10 s, not 100 million.
        Assert.InRange(model.GridLines(0, 10000).Count(), 1600, 1700);
    }
}
