using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class DrumAutoFillTests
{
    private static readonly Lane Kick = new(LaneSource.BeatmapFile, "kick.wav");
    private static readonly Lane Snare = new(LaneSource.BeatmapFile, "snare.wav");
    private static DrumAutoFillOptions Options(bool unmatched = false) => new(Kick, Snare, false, 1.25, 30, unmatched, null);
    private static EditSession Session()
    {
        var file = OsuFile.Parse("""
            osu file format v14
            [General]
            SampleSet:Soft
            Mode:0
            [Metadata]
            Version:Test
            [TimingPoints]
            0,500,4,2,0,45,1,0
            [HitObjects]
            100,100,1000,1,0,0:0:0:0:
            100,100,1500,1,0,0:0:0:0:
            """u8);
        var folder = new MapFolder([]);
        return new EditSession(file, folder, Decompiler.Decompile(file, folder));
    }

    [Fact]
    public void Applying_both_drums_is_one_undo_step_and_keeps_existing_hits_and_volume()
    {
        var session = Session();
        var before = session.State;
        var plan = DrumAutoFillPlan.Create(session, [new("kick", 1010, .9), new("snare", 1490, .8)], Options());
        Assert.Equal(2, plan.Hits.Count);
        session.Add(plan.Hits, "Drum Hits auto-fill");
        Assert.Equal(before.Hits.Count + 2, session.State.Hits.Count);
        Assert.All(before.Hits, h => Assert.Contains(h, session.State.Hits));
        Assert.Same(before.Volumes, session.State.Volumes);
        Assert.All(plan.Hits, h => Assert.Equal(45, session.VolumeOf(h)));
        session.Undo();
        Assert.Same(before, session.State);
        session.Redo();
        Assert.Equal(before.Hits.Count + 2, session.State.Hits.Count);
    }

    [Fact]
    public void Reapplying_skips_existing_and_same_job_duplicates_without_another_undo_step()
    {
        var session = Session();
        DrumHitCandidate[] candidates = [new("kick", 1001, .8), new("kick", 1002, .9), new("snare", 1002, .9)];
        var plan = DrumAutoFillPlan.Create(session, candidates, Options());
        Assert.Equal(2, plan.Hits.Count);
        Assert.Equal(1, plan.Duplicates);
        session.Add(plan.Hits, "Drum Hits auto-fill");
        int steps = session.StepCount;
        plan = DrumAutoFillPlan.Create(session, candidates, Options());
        Assert.Empty(plan.Hits);
        session.Add(plan.Hits, "Drum Hits auto-fill");
        Assert.Equal(steps, session.StepCount);
    }

    [Fact]
    public void Unmatched_hits_are_optional_and_toms_and_invalid_times_are_always_excluded()
    {
        var session = Session();
        DrumHitCandidate[] candidates = [new("kick", 1200, .8), new("tom", 1000, 1), new("snare", double.NaN, 1)];
        Assert.Empty(DrumAutoFillPlan.Create(session, candidates, Options()).Hits);
        var plan = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true));
        var hit = Assert.Single(plan.Hits);
        Assert.Null(hit.Anchor);
        Assert.Equal(1200, hit.Time);
    }

    [Fact]
    public void Selected_range_excludes_outside_objects_and_end_boundary()
    {
        var plan = DrumAutoFillPlan.Create(Session(), [new("kick", 1001, 1), new("snare", 1490, 1), new("kick", 1510, 1)],
            Options(), 1010, 1510);
        Assert.Equal(1500, Assert.Single(plan.Hits).Time);
    }

    [Fact]
    public void Skipped_category_and_same_destination_cannot_add_duplicate_sounds()
    {
        var session = Session();
        DrumHitCandidate[] candidates = [new("kick", 1000, .8), new("snare", 1000, .9)];
        Assert.Single(DrumAutoFillPlan.Create(session, candidates, Options() with { Snare = null }).Hits);
        Assert.Single(DrumAutoFillPlan.Create(session, candidates, Options() with { Snare = Kick }).Hits);
    }

    [Fact]
    public void Empty_hitsound_diff_can_include_new_hits_and_undo_them()
    {
        var file = OsuFile.Parse("""
            osu file format v14
            [General]
            Mode:0
            [Metadata]
            Version:Hitsounds
            [TimingPoints]
            0,500,4,2,0,45,1,0
            [HitObjects]
            """u8);
        var folder = new MapFolder([]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        session.SetHsMode(true);
        var before = session.State;
        DrumHitCandidate[] candidates = [new("kick", 1000, .9)];
        Assert.Empty(DrumAutoFillPlan.Create(session, candidates, Options()).Hits);
        var plan = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true));
        Assert.Single(plan.Hits);
        Assert.Same(before, session.State);
        session.Add(plan.Hits, "Drum Hits auto-fill");
        Assert.Single(session.State.HsCircles);
        session.Undo();
        Assert.Same(before, session.State);
    }
}
