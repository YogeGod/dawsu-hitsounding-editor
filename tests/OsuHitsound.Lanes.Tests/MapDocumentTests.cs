using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MapDocumentTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-doc-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private const string Text =
        "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.mp3\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\nVersion:Insane\r\n\r\n" +
        "[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n";

    private string Map()
    {
        var mapset = Path.Combine(dir, "mapset");
        Directory.CreateDirectory(mapset);
        var path = Path.Combine(mapset, "Band - Song (Me) [Insane].osu");
        File.WriteAllText(path, Text);
        return path;
    }

    private static Lane Clap => new(LaneSource.Skin, "soft-hitclap");

    [Fact]
    public void Save_blocks_unanchored_hits_before_writing_any_other_edits()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000), doc.Session.HitAt(Clap, 1250)]);
        var state = doc.Session.State;
        int history = doc.Session.Position;
        string backups = Path.Combine(dir, "backups");

        var error = Assert.Throws<SaveBlockedException>(() => doc.Save(backups));

        Assert.Null(Assert.Single(error.Issues).Moment);
        Assert.Equal(Text, File.ReadAllText(path));
        Assert.False(Directory.Exists(backups));
        Assert.Same(state, doc.Session.State);
        Assert.Equal(history, doc.Session.Position);
        Assert.True(doc.Session.IsEdited);
    }

    [Fact]
    public void Save_blocks_a_conflict_without_losing_the_intended_arrangement()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Remove(doc.Session.State.Hits); // osu! requires a hitnormal
        var state = doc.Session.State;

        var error = Assert.Throws<SaveBlockedException>(() => doc.Save(Path.Combine(dir, "backups")));

        Assert.NotNull(Assert.Single(error.Issues).Moment);
        Assert.Equal(Text, File.ReadAllText(path));
        Assert.Same(state, doc.Session.State);
        Assert.True(doc.Session.CanUndo);
    }

    [Fact]
    public void Saving_keeps_undo_and_redo_and_tracks_the_saved_state()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        var desired = doc.Session.State;
        doc.Save(Path.Combine(dir, "backups"));
        var savedBytes = File.ReadAllBytes(path);

        Assert.Same(desired, doc.Session.State);
        Assert.True(doc.Session.CanUndo);
        Assert.False(doc.Session.IsEdited);
        doc.Session.Undo();
        Assert.True(doc.Session.IsEdited);
        doc.Session.Redo();
        Assert.False(doc.Session.IsEdited);
        Assert.False(doc.Save(Path.Combine(dir, "backups")).Written);
        Assert.Equal(savedBytes, File.ReadAllBytes(path));

        doc.Session.Undo();
        Assert.True(doc.Save(Path.Combine(dir, "backups")).Written);
        Assert.DoesNotContain(MapDocument.Load(path).Session.State.Hits, h => h.Lane == Clap);
        Assert.True(doc.Session.CanRedo);
        doc.Session.Redo();
        Assert.True(doc.Session.IsEdited);
        doc.Save(Path.Combine(dir, "backups"));
        Assert.Contains(MapDocument.Load(path).Session.State.Hits, h => h.Lane == Clap);
    }

    [Fact]
    public void Explicit_discard_returns_to_the_last_saved_arrangement_including_loose_hits()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        doc.Save(Path.Combine(dir, "backups"));
        var saved = doc.Session.State;
        doc.Session.Add([doc.Session.HitAt(Clap, 1250)]);
        doc.Session.Remove(doc.Session.State.Hits.Where(h => h.Time == 1000));

        doc.Session.DiscardUnsaved();

        Assert.Same(saved, doc.Session.State);
        Assert.Empty(doc.Session.Unanchored);
        Assert.False(doc.Session.IsEdited);
        Assert.False(doc.Session.CanUndo);
    }

    [Fact]
    public void A_sample_added_to_the_folder_is_found_and_a_hit_on_it_saves_with_its_index()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        int index = HitsoundDiff.FirstFreeIndex(doc.Folder);
        var name = $"soft-hitclap{index}.wav";
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(path)!, name), [0]);

        Assert.Equal([name], doc.RescanFolder());
        Assert.Empty(doc.RescanFolder()); // nothing new the second time

        doc.Session.Add([doc.Session.HitAt(new Lane(LaneSource.BeatmapFile, name), 1000)]);
        doc.Save(Path.Combine(dir, "backups"));
        var reopened = MapDocument.Load(path);
        Assert.Contains(reopened.Session.State.Hits, h => h.Lane.Name == name && h.Time == 1000);
    }

    [Fact]
    public void An_edit_is_saved_and_the_reopened_map_starts_from_it()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);

        var saved = doc.Save(Path.Combine(dir, "backups"));

        Assert.True(saved.Written);
        Assert.Equal(Text, File.ReadAllText(saved.Backup!));
        var reopened = MapDocument.Load(path);
        Assert.Contains(reopened.Session.State.Hits, h => h.Lane == Clap && h.Time == 1000);
        Assert.False(reopened.Session.IsEdited);
    }

    [Fact]
    public void Save_refuses_a_map_changed_on_disk_since_it_was_opened()
    {
        var path = Map();
        var doc = MapDocument.Load(path);
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        File.AppendAllText(path, "\r\n");

        Assert.Throws<IOException>(() => doc.Save(Path.Combine(dir, "backups")));
        Assert.EndsWith("\r\n\r\n", File.ReadAllText(path)); // left as the other program wrote it
    }

    [Fact]
    public void Compiling_works_on_a_copy_and_never_changes_the_map_as_opened()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);

        var (result, copy) = doc.Compile(doc.Session.State);

        Assert.Empty(result.Conflicts);
        Assert.Contains("256,192,1000,1,8,", Encoding.UTF8.GetString(copy.ToBytes()));
        Assert.Equal(Text, Encoding.UTF8.GetString(doc.Opened));
        Assert.False(doc.File.IsEdited);
    }

    [Fact]
    public void Generate_is_planned_next_to_the_map_with_the_previous_record()
    {
        var doc = MapDocument.Load(Map());
        doc.Session.Add([doc.Session.HitAt(new Lane(LaneSource.Skin, "drum-hitclap"), 1250)]); // no hitnormal: needs a mix

        var (plan, folder) = doc.PlanGenerate(Path.Combine(dir, "generated"));

        Assert.Equal(Path.Combine(Path.GetDirectoryName(doc.Path)!, "Band - Song (Me) [Hitsounds].osu"), plan.OsuPath);
        Assert.Equal("soft-hitnormal2.wav", Assert.Single(plan.Mixes).FileName);
        Assert.Contains(Path.GetFileName(doc.Path), folder.Files);
    }
}
