using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class FlatLaneTests
{
    private static ArrangeModel Model()
    {
        var file=OsuFile.Parse(Encoding.UTF8.GetBytes("osu file format v14\n[General]\nSampleSet:Soft\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n256,192,1000,1,8,0:0:0:80:\n"));
        var folder=new MapFolder(new[]{"soft-hitclap2.wav","soft-hitclap3.wav"});
        return new(new EditSession(file,folder,Decompiler.Decompile(file,folder)),file,new HashSet<Lane>());
    }

    [Fact]
    public void Sample_set_folders_move_as_a_whole_and_undo_restores_their_order()
    {
        var model=Model(); var s=model.Session;
        var before=model.Rows.OfType<GroupRow>().Select(g=>g.Group).ToArray();
        Assert.True(before.Length>=2);
        model.MoveFolderTo(before[^1],before[0],false);
        var after=model.Rows.OfType<GroupRow>().Select(g=>g.Group).ToArray();
        Assert.Equal(before[^1],after[0]);
        Assert.Equal(before.OrderBy(g=>g),after.OrderBy(g=>g));
        // Every lane still sits under its own set's header, hitnormal first.
        LaneGroup? current=null; bool first=false;
        foreach(var row in model.Rows)
        {
            if(row is GroupRow g){current=g.Group; first=true;}
            else if(row is LaneRow l)
            {
                Assert.Equal(current,l.Group);
                if(first&&l.Group is LaneGroup.NormalSet or LaneGroup.SoftSet or LaneGroup.DrumSet) Assert.Equal("hitnormal",model.SlotOf(l.Lane).Sample?.Sound);
                first=false;
            }
        }
        s.Undo();
        Assert.Equal(before,model.Rows.OfType<GroupRow>().Select(g=>g.Group));
    }

    [Fact]
    public void Families_move_across_sample_sets_and_custom_order_survives_reopen_and_undo()
    {
        var model=Model(); var s=model.Session; var original=model.LaneRows.Select(r=>r.Lane).ToArray();
        var clap=model.LaneRows.First(r=>r.Lane.Name=="soft-hitclap").Lane;
        var target=model.LaneRows.First(r=>r.Lane.Name=="normal-hitnormal").Lane;
        var state=s.State;
        model.MoveLaneTo(clap,target,false);
        Assert.Equal(clap,model.LaneRows[0].Lane);
        Assert.Equal(new[]{"soft-hitclap","soft-hitclap2.wav","soft-hitclap3.wav"},model.LaneRows.Take(3).Select(r=>r.Lane.Name));
        Assert.Same(state.Hits,s.State.Hits); Assert.Same(state.Volumes,s.State.Volumes);
        var (names,order,added,hidden)=model.LaneLayout(); var reopened=Model();
        reopened.ApplyLaneLayout(new ProjectData{LaneNames=names,LaneOrder=order,AddedLanes=added,HiddenLanes=hidden},_=>true);
        Assert.Equal(model.LaneRows.Select(r=>r.Lane),reopened.LaneRows.Select(r=>r.Lane));
        s.Undo(); Assert.Equal(original,model.LaneRows.Select(r=>r.Lane));
        s.Redo(); Assert.Equal(clap,model.LaneRows[0].Lane); Assert.False(s.IsEdited);
    }
}
