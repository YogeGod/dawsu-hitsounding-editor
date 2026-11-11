using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public sealed class EditorProjectTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hse-project-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private const string MapPath = @"C:\maps\x\Band - Song (Me) [Insane].osu";

    private static readonly byte[] MapBytes = Encoding.UTF8.GetBytes(
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Editor]\r\nBookmarks: 1000\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
        "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n256,192,2000,1,8,0:0:0:0:\r\n");

    private static readonly Lane Own = new(LaneSource.BeatmapFile, "soft-hitclap2.wav");
    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");
    private static readonly Lane Normal = new(LaneSource.Skin, "soft-hitnormal");

    /// <summary>A freshly opened map: its document and lane model, as the window makes them.</summary>
    private static (MapDocument Doc, ArrangeModel Model) Open()
    {
        var doc = MapDocument.FromBytes(MapPath, MapBytes, new MapFolder(["soft-hitclap2.wav"], _ => [1]));
        return (doc, new ArrangeModel(doc.Session, doc.File, new HashSet<Lane>()));
    }

    private EditorProject Project((MapDocument Doc, ArrangeModel Model) map) => new(root, map.Doc, map.Model);

    private static ProjectView View(double offset = 12) =>
        new(1500, 3500, true, new ExportMergeSettings { PairSamples = true, Style = ExportStyle.PerObject }, offset);

    [Fact]
    public void A_saved_project_comes_back_when_the_map_is_opened_again()
    {
        var first = Open();
        Project(first).Restore(ExportStyle.GreenLines);
        var (session, model) = (first.Doc.Session, first.Model);
        session.RenameBookmark(1000, "Chorus");
        var loose = session.HitAt(Clap, 3000);
        session.Add([loose], volume: 40);
        model.AddLane(Own);
        model.RenameLane(Clap, "Snare");
        model.MoveLane(Clap, -1);
        model.SetActive(Clap);
        model.SetTimeSignatures([new TimeSignature(2, 7, 8)]);
        model.TogglePreviewMute(session.State.Hits.First(h => h.Lane == Clap && h.Anchor is not null));
        var order = model.LaneRows.Select(r => r.Lane).ToList();
        Project(first).Save(View());

        var again = Open();
        var (view, warning) = Project(again).Restore(ExportStyle.GreenLines);

        Assert.Null(warning);
        Assert.Equal((1500.0, 3500.0, true, 12.0, ExportStyle.PerObject),
            (view.LocatorLeft!.Value, view.LocatorRight!.Value, view.Cycle, view.SongOffset, view.ExportSettings.Style));
        Assert.Equal("Chorus", again.Doc.Session.BookmarkLabel(1000));
        var restored = Assert.Single(again.Doc.Session.Unanchored);
        Assert.Equal((Clap, 3000.0, 40), (restored.Lane, restored.Time, again.Doc.Session.VolumeOf(restored)));
        Assert.Equal("Snare", again.Model.NameOf(Clap));
        Assert.Equal(order, again.Model.LaneRows.Select(r => r.Lane));
        Assert.Single(again.Model.LaneRows, r => r.Lane == Own);
        Assert.Equal(Clap, again.Model.ActiveLane);
        Assert.Equal([new TimeSignature(2, 7, 8)], again.Model.TimeSignatures);
        Assert.Single(again.Model.PreviewMutedHits);
    }

    [Fact]
    public void Discarding_on_close_keeps_the_view_but_not_the_unsaved_edits()
    {
        var first = Open();
        var project = Project(first);
        project.Restore(ExportStyle.GreenLines);
        first.Doc.Session.RenameBookmark(1000, "Chorus");
        first.Doc.Session.Add([first.Doc.Session.HitAt(Clap, 3000)]);
        first.Model.RenameLane(Clap, "Snare");
        project.Save(View(offset: 30), discardUnsaved: true);

        var again = Open();
        var (view, _) = Project(again).Restore(ExportStyle.GreenLines);

        Assert.Empty(again.Doc.Session.Unanchored);
        Assert.Equal("", again.Doc.Session.BookmarkLabel(1000));
        Assert.Equal(30, view.SongOffset);
        Assert.Equal("Snare", again.Model.NameOf(Clap));
    }

    [Fact]
    public void A_damaged_project_restores_what_it_can()
    {
        var map = Open();
        ProjectStore.Save(root, MapPath, new ProjectData
        {
            ExportSettings = new ExportMergeSettings { Version = 2, PairSamples = false },
            SongOffset = -99999,
            AddedLanes = ["Nope|x", "BeatmapFile|gone.wav"],
            HiddenLanes = ["Nope|y"],
            Unanchored = [new ProjectHit("Nope|z", 3000)],
        });

        var (view, _) = Project(map).Restore(ExportStyle.PerObject);

        Assert.True(view.ExportSettings.PairSamples);
        Assert.Equal(ExportStyle.PerObject, view.ExportSettings.Style);
        Assert.Equal(-10000, view.SongOffset);
        Assert.DoesNotContain(map.Model.LaneRows, r => r.Lane.Name == "gone.wav");
        Assert.Empty(map.Doc.Session.Unanchored);
    }

    [Fact]
    public void An_out_of_range_offset_is_clamped_and_saved_export_settings_always_pair()
    {
        var map = Open();
        ProjectStore.Save(root, MapPath, new ProjectData
        {
            SongOffset = 99999,
            ExportSettings = new ExportMergeSettings { PairSamples = false, Style = ExportStyle.GreenLines },
        });

        var (view, _) = Project(map).Restore(ExportStyle.PerObject);

        Assert.Equal(10000, view.SongOffset);
        Assert.True(view.ExportSettings.PairSamples);
        Assert.Equal(ExportStyle.GreenLines, view.ExportSettings.Style);
    }

    [Fact]
    public void A_saved_hit_with_no_object_comes_back_only_where_there_is_still_no_object()
    {
        var map = Open();
        ProjectStore.Save(root, MapPath, new ProjectData
        {
            Unanchored = [new ProjectHit(ProjectStore.KeyOf(Clap), 2000), new ProjectHit(ProjectStore.KeyOf(Normal), 4000)],
        });

        Project(map).Restore(ExportStyle.GreenLines);

        var loose = Assert.Single(map.Doc.Session.Unanchored);
        Assert.Equal((Normal, 4000.0), (loose.Lane, loose.Time));
    }

    [Fact]
    public void Without_a_saved_project_the_defaults_apply()
    {
        var map = Open();
        var (view, warning) = Project(map).Restore(ExportStyle.PerObject);

        Assert.Null(warning);
        Assert.Equal((null, null, false, 0.0), (view.LocatorLeft, view.LocatorRight, view.Cycle, view.SongOffset));
        Assert.True(view.ExportSettings.PairSamples);
        Assert.Equal(ExportStyle.PerObject, view.ExportSettings.Style);
    }
}
