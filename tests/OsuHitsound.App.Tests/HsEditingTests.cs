using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class HsEditingTests
{
    private static (ArrangeModel Model, HitEditor Editor) Setup()
    {
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet: Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n"));
        var folder = new MapFolder([]);
        var session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        var model = new ArrangeModel(session, file, new HashSet<Lane>());
        session.Changed += model.RebuildNotes;
        session.SetHsMode(true);
        return (model, new HitEditor { Model = model });
    }

    [Fact]
    public void Paste_and_repeat_create_visible_circles_keep_volume_and_undo_as_one_operation()
    {
        var (model, editor) = Setup(); var session = model.Session;
        var lane = new Lane(LaneSource.Skin, "soft-hitnormal");
        var first = session.HitAt(lane, 1000); session.Add([first]);
        session.SetVolume([first.Anchor!.Value], 40);
        editor.Select([first]); editor.Copy();
        int before = session.Position;
        editor.Paste(2000);
        Assert.Equal(before + 1, session.Position);
        Assert.Equal(2, model.Objects.Count);
        Assert.Equal(40, session.VolumeOf(Assert.Single(editor.Selected)));
        session.Undo(); Assert.Single(model.Objects);
        session.Redo(); Assert.Equal(2, model.Objects.Count);
        editor.Select(session.State.Hits.Where(h => h.Time == 2000));
        before = session.Position;
        editor.Repeat(3, 1);
        Assert.Equal(before + 1, session.Position);
        Assert.Equal(5, model.Objects.Count);
        Assert.Equal(3, editor.Selected.Count);
        Assert.All(editor.Selected, h => Assert.Equal(40, session.VolumeOf(h)));
        session.Undo(); Assert.Equal(2, model.Objects.Count);
        session.Redo(); Assert.Equal(5, model.Objects.Count);
    }

    [Fact]
    public void Quantize_moves_owned_circles_and_range_deletion_removes_them()
    {
        var (model, editor) = Setup(); var session = model.Session;
        var lane = new Lane(LaneSource.Skin, "soft-hitnormal");
        var hit = session.HitAt(lane, 1030); session.Add([hit]);
        editor.Select([hit]);
        Assert.Equal(1, editor.Quantize(100));
        Assert.Equal(1000, Assert.Single(model.Objects).Start);
        var selected = Assert.Single(editor.Selected);
        Assert.True(session.MomentByKey.ContainsKey(selected.Anchor!.Value));
        session.EditRange(900, 1100, new HashSet<Lane> { lane }, RangeOperation.Delete, 0);
        Assert.Empty(model.Objects);
        session.Undo(); Assert.Single(model.Objects);
    }

    [Fact]
    public void Bulk_import_path_and_volume_scaling_use_live_object_anchors()
    {
        var (model, _) = Setup(); var session = model.Session;
        var lane = new Lane(LaneSource.Skin, "soft-hitclap");
        var batch = Enumerable.Range(0, 400).Select(i => session.HitAt(lane, i * 125)).ToList();
        session.Add(batch, "Import notes");
        Assert.Equal(400, model.Objects.Count);
        session.SetVolume(session.State.Hits.Select(h => h.Anchor!.Value), 65);
        Assert.All(session.State.Hits, h => Assert.Equal(65, session.VolumeOf(h)));
        session.Undo(); session.Undo();
        Assert.Empty(model.Objects);
    }
}
