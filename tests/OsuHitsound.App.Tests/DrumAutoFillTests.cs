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

    // 120 BPM from 0 ms: a 1/4 step is 125 ms, a 1/3 step 166.7 ms.
    private static (double, double) Grid(double time, int divisor)
    {
        double step = 500.0 / divisor;
        return (Math.Round(time / step) * step, step);
    }

    [Fact]
    public void Off_grid_detections_are_dropped_and_on_grid_ones_snap_to_the_line()
    {
        var session = Session();
        // 1135 ms is 10 ms after the 1/4 line at 1125; 1190 is 60 ms from the nearest line (1250); 1515 is on the object at 1500.
        DrumHitCandidate[] candidates = [new("kick", 1135, .9), new("snare", 1190, .8), new("kick", 1515, .7)];
        var plan = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [4] }, grid: Grid);
        Assert.Equal(1, plan.OffGrid);
        Assert.Equal([1125.0, 1500.0], plan.Hits.Select(h => h.Time));
        Assert.NotNull(plan.Hits.Single(h => h.Time == 1500).Anchor);
    }

    [Fact]
    public void Triplet_grid_keeps_triplet_hits_and_no_grid_keeps_everything()
    {
        var session = Session();
        DrumHitCandidate[] candidates = [new("kick", 1170, .9)]; // 1/3 line at 1166.7, 45 ms from any 1/4 line
        var straight = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [4] }, grid: Grid);
        Assert.Equal(1, straight.OffGrid);
        Assert.Empty(straight.Hits);
        var triplets = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [4, 3] }, grid: Grid);
        Assert.Equal(1166.67, Assert.Single(triplets.Hits).Time, 2);
        var off = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [] }, grid: Grid);
        Assert.Equal(0, off.OffGrid);
        Assert.Single(off.Hits);
    }

    [Fact]
    public void Two_detections_snapping_to_one_line_keep_only_the_stronger()
    {
        var session = Session();
        DrumHitCandidate[] candidates = [new("kick", 1118, .4), new("kick", 1132, .9)];
        var plan = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [4] }, grid: Grid);
        Assert.Equal(1125.0, Assert.Single(plan.Hits).Time);
        Assert.Equal(1, plan.Duplicates);
    }

    [Fact]
    public void A_constant_audio_lag_is_measured_and_allowed_for()
    {
        var session = Session();
        // Twelve hits on 1/4 lines, all detected 28 ms late (outside the 20 ms tolerance), plus one off-grid hit.
        var candidates = Enumerable.Range(0, 12).Select(i => new DrumHitCandidate("kick", 2000 + i * 125 + 28, .9))
            .Append(new("kick", 4060 + 28, .5)).ToArray();
        var plan = DrumAutoFillPlan.Create(session, candidates, Options(unmatched: true) with { GridDivisors = [4] }, grid: Grid);
        Assert.Equal(28, plan.GridShiftMs, 1);
        Assert.Equal(1, plan.OffGrid);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 2000.0 + i * 125), plan.Hits.Select(h => h.Time));
        // Too few hits to measure a lag: no compensation, so the late hits count as off the grid.
        var few = DrumAutoFillPlan.Create(session, candidates.Take(3), Options(unmatched: true) with { GridDivisors = [4] }, grid: Grid);
        Assert.Equal(0, few.GridShiftMs);
        Assert.Equal(3, few.OffGrid);
    }

    [Fact]
    public void Grid_tolerance_narrows_for_fine_divisions_and_ignores_unmapped_time()
    {
        // At 1/16 the step is 31.25 ms, so the tolerance is about 10.4 ms rather than 20 ms.
        Assert.Null(DrumAutoFillPlan.OnGrid(1015, Grid, [16]));
        Assert.Equal(1000.0, DrumAutoFillPlan.OnGrid(1008, Grid, [16]));
        // No red line: the snap returns the time itself with an infinite step, so nothing is dropped.
        Assert.Equal(777.0, DrumAutoFillPlan.OnGrid(777, (t, _) => (t, double.PositiveInfinity), [4]));
    }
}
