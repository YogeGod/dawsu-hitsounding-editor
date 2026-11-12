using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

/// <summary>Undo history carried into the map reloaded after Save (issue #39).</summary>
public class HistoryAcrossSaveTests
{
    private const string Path = @"C:\maps\x\Band - Song (Me) [Insane].osu";
    private static readonly MapFolder NoFiles = new([]);
    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");

    private static byte[] MapBytes(params int[] circles) => Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n" + string.Concat(circles.Select(t => $"256,192,{t},1,0,0:0:0:0:\r\n")));

    // Saving: what the edits compile to, opened again as the window does after Save.
    private static MapDocument Saved(MapDocument doc) => MapDocument.FromBytes(Path, doc.Compile(doc.Session.State).File.ToBytes(), NoFiles);

    private static bool HasClapAt(EditSession s, double time) => s.State.Hits.Any(h => h.Lane == Clap && h.Time == time);

    [Fact]
    public void Undo_after_save_brings_the_edit_back_as_unsaved_and_redo_makes_it_clean_again()
    {
        var doc = MapDocument.FromBytes(Path, MapBytes(1000, 2000), NoFiles);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        var reloaded = Saved(doc);
        Assert.True(HasClapAt(reloaded.Session, 1000));

        Assert.Equal(0, reloaded.Session.AdoptHistory(doc.Session));

        Assert.False(reloaded.Session.IsEdited);
        Assert.True(reloaded.Session.CanUndo);
        reloaded.Session.Undo();
        Assert.False(HasClapAt(reloaded.Session, 1000));
        Assert.True(reloaded.Session.IsEdited);   // undone past the save: unsaved again
        reloaded.Session.Redo();
        Assert.True(HasClapAt(reloaded.Session, 1000));
        Assert.False(reloaded.Session.IsEdited);  // back to what's on disk
    }

    [Fact]
    public void Steps_keep_their_names_and_redo_steps_come_along()
    {
        var doc = MapDocument.FromBytes(Path, MapBytes(1000, 2000), NoFiles);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)], "Add clap");
        doc.Session.Add([doc.Session.HitAt(Clap, 2000)], "Add second clap");
        doc.Session.Undo(); // saved with the second clap undone: it can still be redone afterwards
        var reloaded = Saved(doc);

        reloaded.Session.AdoptHistory(doc.Session);

        Assert.Equal(doc.Session.Steps, reloaded.Session.Steps);
        reloaded.Session.Redo();
        Assert.True(HasClapAt(reloaded.Session, 2000));
        Assert.True(reloaded.Session.IsEdited);
    }

    [Fact]
    public void A_step_whose_object_is_gone_ends_the_history_there()
    {
        var doc = MapDocument.FromBytes(Path, MapBytes(1000, 2000), NoFiles);
        doc.Session.Add([doc.Session.HitAt(Clap, 2000)], "Clap on the object that disappears");
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)], "Clap on the one that stays");
        // The file on disk no longer has the object at 2000 (changed elsewhere between edits and reload).
        var reloaded = MapDocument.FromBytes(Path, MapBytes(1000), NoFiles);

        int dropped = reloaded.Session.AdoptHistory(doc.Session);

        Assert.Equal(2, dropped); // both older states still hold the 2000 object
        Assert.False(reloaded.Session.CanUndo);
    }
}
