using OsuHitsound.App;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class PlaybackRedrawTests
{
    [Fact]
    public void Playhead_ticks_do_not_invalidate_the_cached_scene()
    {
        var lanes = new ArrangeView();
        var volume = new VolumeLane();
        var overview = new OverviewStrip();

        lanes.Playhead = 1500;
        volume.Playhead = 1500;
        overview.Playhead = 1500;
        lanes.InvalidatePlayhead();
        volume.InvalidatePlayhead();
        overview.InvalidatePlayhead();
        overview.InvalidateViewport();

        Assert.Equal(0, lanes.SceneInvalidations);
        Assert.Equal(0, volume.SceneInvalidations);
        Assert.Equal(0, overview.SceneInvalidations);

        lanes.InvalidateVisual();
        volume.InvalidateVisual();
        overview.InvalidateVisual();

        Assert.Equal(1, lanes.SceneInvalidations);
        Assert.Equal(1, volume.SceneInvalidations);
        Assert.Equal(1, overview.SceneInvalidations);
    }

    [Fact]
    public void Linked_selection_is_indexed_once_instead_of_scanned_per_note()
    {
        var lane = new Lane(LaneSource.BeatmapFile, "soft-hitclap.wav");
        var linked = Guid.NewGuid();
        var other = Guid.NewGuid();
        EditHit Hit(double time, Guid placement) => new(lane, time, null) { SetPlacement = placement };
        var into = new HashSet<Guid>();

        ArrangeView.CollectLinkedPlacements([Hit(0, linked), Hit(10, other), Hit(20, Guid.Empty), Hit(30, linked)], into);

        Assert.Equal(2, into.Count);
        Assert.Contains(linked, into);
        Assert.Contains(other, into);
        ArrangeView.CollectLinkedPlacements([Hit(20, Guid.Empty)], into);
        Assert.Empty(into);
    }
}

public class AudioLoadTests
{
    // osu!'s official blank.wav: a PCM WAV with an empty data chunk. BASS is not required to recognise it.
    private static readonly byte[] Blank =
    [
        0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45,
        0x66, 0x6d, 0x74, 0x20, 0x10, 0, 0, 0, 1, 0, 2, 0,
        0x44, 0xac, 0, 0, 0x10, 0xb1, 2, 0, 4, 0, 0x10, 0,
        0x64, 0x61, 0x74, 0x61, 0, 0, 0, 0,
    ];

    private static DecompiledMap Map(params Lane[] lanes) =>
        new(new MapSettings(14, 0, SampleSet.Soft, 1.4, 1), lanes, [], [], []);

    [Fact]
    public void Samples_are_decoded_off_the_caller_and_a_superseded_publish_is_dropped()
    {
        var file = new Lane(LaneSource.BeatmapFile, "blank.wav");
        var again = new Lane(LaneSource.BeatmapFile, "blank.wav");
        var missing = new Lane(LaneSource.Skin, "normal-hitnormal", true);
        var skinLane = new Lane(LaneSource.Skin, "soft-hitclap");
        var map = Map(file, again, missing, skinLane);
        int caller = Environment.CurrentManagedThreadId;
        int decodedOn = 0;
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var folder = new MapFolder(["blank.wav"], _ =>
        {
            decodedOn = Environment.CurrentManagedThreadId;
            reading.Set();
            release.Wait();
            return Blank;
        });
        var skin = new Dictionary<string, byte[]> { ["soft-hitclap"] = Blank };

        PreparedLoad? prepared = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { prepared = AudioEngine.Prepare(map, folder, skin); }
            catch (Exception ex) { failure = ex; release.Set(); }
        });
        worker.Start();
        Assert.True(reading.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(caller, decodedOn);
        release.Set();
        worker.Join();
        Assert.Null(failure);
        Assert.NotNull(prepared);
        Assert.Equal(3, prepared.Samples.Count); // the duplicate file lane is decoded once
        Assert.Contains(prepared.Samples, s => s.Lane == file && s.Stereo is { Length: > 0 });
        Assert.Contains(prepared.Samples, s => s.Lane == missing && s.Stereo is null);
        Assert.Contains(prepared.Samples, s => s.Lane == skinLane && s.Stereo is { Length: > 0 });

        using (var synchronous = AudioEngine.CreateForTests())
        {
            synchronous.Load(map, folder, skin);
            Assert.Contains(missing, synchronous.SilentLanes);
            Assert.DoesNotContain(file, synchronous.SilentLanes);
            Assert.DoesNotContain(skinLane, synchronous.SilentLanes);
        }

        using var engine = AudioEngine.CreateForTests();
        Assert.True(engine.Apply(prepared));
        Assert.DoesNotContain(file, engine.SilentLanes);
        Assert.DoesNotContain(skinLane, engine.SilentLanes);
        Assert.Contains(missing, engine.SilentLanes);

        int stale = engine.BeginLoad();
        int current = engine.BeginLoad();
        Assert.False(engine.Publish(stale, AudioEngine.Prepare(map, folder, skin)));
        Assert.Empty(engine.SilentLanes);
        Assert.True(engine.Publish(current, AudioEngine.Prepare(map, folder, skin)));
        Assert.Contains(missing, engine.SilentLanes);

        int abandoned = engine.BeginLoad();
        engine.Dispose();
        Assert.False(engine.Publish(abandoned, AudioEngine.Prepare(map, folder, skin)));
    }
}
