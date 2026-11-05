using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Tests;

public class WorkflowPassTests
{
    private static EditSession Session(int version = 14)
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes($"osu file format v{version}\n[General]\nSampleSet:Soft\n[Editor]\nBookmarks:1000\n[TimingPoints]\n0,500,4,2,0,80,1,0\n[HitObjects]\n256,192,1000,1,8,0:0:0:80:\n"));
        var folder = new MapFolder([]);
        return new(file, folder, Decompiler.Decompile(file, folder));
    }

    [Fact]
    public void Independent_layer_volume_preserves_other_sounds_and_undo()
    {
        var s = Session(); var before = s.State; var first = s.State.Hits[0]; var other = s.State.Hits[1];
        s.SetLayerVolumes(new Dictionary<EditHit, int> { [first] = 25 });
        Assert.Equal(25, s.VolumeOf(first)); Assert.Equal(80, s.VolumeOf(other));
        Assert.Same(before.Volumes, s.State.Volumes); Assert.True(s.IsEdited);
        s.Undo(); Assert.Equal(80, s.VolumeOf(first));
        s.Redo(); Assert.Equal(25, s.VolumeOf(first));
    }

    [Fact]
    public void All_lane_edit_clears_independent_override_even_at_same_base_value()
    {
        var s = Session(); var first = s.State.Hits[0];
        s.SetLayerVolumes(new Dictionary<EditHit, int> { [first] = 25 });
        s.SetVolume([first.Anchor!.Value], 80);
        Assert.All(s.State.Hits, h => Assert.Equal(80, s.VolumeOf(h)));
        Assert.Empty(s.State.LayerVolumes);
    }

    [Fact]
    public void Layer_edits_clamp_ignore_missing_hits_and_skip_noop_history()
    {
        var s = Session(); var first = s.State.Hits[0];
        s.SetLayerVolumes(new Dictionary<EditHit, int> { [first] = 80, [first with { Time = 9999 }] = 3 });
        Assert.False(s.CanUndo);
        s.SetLayerVolumes(new Dictionary<EditHit, int> { [first] = 200 }); Assert.Equal(100, s.VolumeOf(first));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(3)]
    public void Bookmark_labels_use_raw_times_and_follow_undo_and_removal(int version)
    {
        var s = Session(version); double time = s.BookmarkTimes.Single();
        s.RenameBookmark(time, "  Chorus  ");
        Assert.Equal("Chorus", s.BookmarkLabel(time)); Assert.True(s.IsEdited);
        Assert.Equal("Chorus", s.State.BookmarkLabels[1000]);
        s.Undo(); Assert.Equal("", s.BookmarkLabel(time));
        s.Redo(); s.RemoveBookmarkNear(time, 1);
        Assert.Empty(s.State.BookmarkLabels); s.Undo(); Assert.Equal("Chorus", s.BookmarkLabel(time));
    }

    [Fact]
    public void Restoring_labels_ignores_stale_bookmarks_and_creates_no_edit()
    {
        var s = Session(); s.RestoreBookmarkLabels(new Dictionary<int, string> { [1000] = "Intro", [5000] = "Stale" });
        Assert.Single(s.State.BookmarkLabels); Assert.False(s.IsEdited); Assert.False(s.CanUndo);
        s.RenameBookmark(5000, "Absent"); Assert.False(s.CanUndo);
    }
}
