using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public sealed class MapWritesTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-writes-").FullName;
    private string Backups => Path.Combine(dir, "backups");
    private string Manifests => Path.Combine(dir, "manifests");
    private string MapDir => Path.Combine(dir, "map");

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");
    private static readonly ExportMergeSettings Pairing = new() { PairSamples = true };
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> NoSkin = new(() => new Dictionary<string, byte[]>());

    private static string MapText(string version, string objects) =>
        "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Metadata]\r\nTitle:Song\r\nArtist:Band\r\nCreator:Me\r\n" +
        $"Version:{version}\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n{objects}";

    private MapDocument Map(string version = "Insane", string objects = "256,192,1000,1,0,0:0:0:0:\r\n256,192,2000,1,0,0:0:0:0:\r\n")
    {
        Directory.CreateDirectory(MapDir);
        var path = Path.Combine(MapDir, $"Band - Song (Me) [{version}].osu");
        File.WriteAllText(path, MapText(version, objects));
        return MapDocument.Load(path);
    }

    private MapWrites Writes(MapDocument doc) => new(doc, Backups, Manifests);

    /// <summary>Answers the module's questions from a script and records what it asked.</summary>
    private sealed class Host : IMapWriteHost
    {
        public Func<string, string, Task<bool>> Confirm { get; init; } = (_, _) => Task.FromResult(true);
        public Func<(GeneratePlan Plan, LaneAudio Audio), Task<(GeneratePlan Plan, LaneAudio Audio)?>> Generate { get; init; } = p => Task.FromResult<(GeneratePlan, LaneAudio)?>(p);
        public bool ProjectSaves { get; init; } = true;
        public int ProjectSaveCount { get; private set; }
        public List<string> Asked { get; } = [];
        public List<string> Shown { get; } = [];

        public Task<bool> ConfirmSave(string title, string text) { Asked.Add(text); return Confirm(title, text); }
        public Task<(GeneratePlan Plan, LaneAudio Audio)?> ConfirmGenerate((GeneratePlan Plan, LaneAudio Audio) initial,
            Func<GeneratePlan, string> summary, Func<bool, Task<(GeneratePlan Plan, LaneAudio Audio)>> replan)
        { Asked.Add(summary(initial.Plan)); return Generate(initial); }
        public bool SaveProject() { ProjectSaveCount++; return ProjectSaves; }
        public void ShowStatus(string text) => Shown.Add(text);
    }

    [Fact]
    public async Task Save_writes_the_map_with_a_backup_and_asks_to_reopen()
    {
        var doc = Map();
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        var host = new Host();

        var outcome = await Writes(doc).SaveAsync(host, Pairing, NoSkin);

        Assert.True(outcome.Done);
        Assert.True(outcome.MapWritten);
        Assert.True(outcome.Reopen);
        Assert.Contains("Saved this difficulty", outcome.Status);
        Assert.Contains("256,192,1000,1,8,", File.ReadAllText(doc.Path));
        Assert.Single(Directory.EnumerateFiles(Backups, "*.osu", SearchOption.AllDirectories));
        Assert.Equal(2, host.ProjectSaveCount);
    }

    [Fact]
    public async Task A_cancelled_save_writes_nothing()
    {
        var doc = Map();
        doc.Session.Add([doc.Session.HitAt(Clap, 1000), doc.Session.HitAt(Clap, 5000)]); // a loose hit: Save asks first
        var before = File.ReadAllBytes(doc.Path);
        var host = new Host { Confirm = (_, _) => Task.FromResult(false) };

        var outcome = await Writes(doc).SaveAsync(host, Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Null(outcome.Status);
        Assert.Contains(host.Asked, a => a.Contains("no hit object"));
        Assert.Equal(before, File.ReadAllBytes(doc.Path));
    }

    [Fact]
    public async Task Only_loose_hits_are_not_saved_into_the_difficulty()
    {
        var doc = Map(objects: "");
        doc.Session.Add([doc.Session.HitAt(Clap, 5000)]);
        var before = File.ReadAllBytes(doc.Path);

        var outcome = await Writes(doc).SaveAsync(new Host(), Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Contains("cannot be saved into this difficulty", outcome.Status);
        Assert.Equal(before, File.ReadAllBytes(doc.Path));
    }

    [Fact]
    public async Task Save_stops_when_the_editor_project_cannot_be_written()
    {
        var doc = Map();
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        var before = File.ReadAllBytes(doc.Path);

        var outcome = await Writes(doc).SaveAsync(new Host { ProjectSaves = false }, Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.StartsWith("Save stopped: The editor project could not be written", outcome.Status);
        Assert.Equal(before, File.ReadAllBytes(doc.Path));
    }

    [Fact]
    public async Task Generate_with_no_hits_does_nothing()
    {
        var doc = Map(objects: "");
        var outcome = await Writes(doc).GenerateAsync(new Host(), Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Equal("Nothing to generate: there are no hits.", outcome.Status);
    }

    [Fact]
    public async Task Generate_refuses_when_the_open_map_is_the_hitsounds_diff_itself()
    {
        var doc = Map(version: "Hitsounds");
        var outcome = await Writes(doc).GenerateAsync(new Host(), Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Contains("is the [Hitsounds] diff itself", outcome.Status);
    }

    [Fact]
    public async Task Generate_writes_nothing_when_the_edits_change_before_it_is_confirmed()
    {
        var doc = Map();
        var host = new Host { Generate = p => { doc.Session.Add([doc.Session.HitAt(Clap, 1000)]); return Task.FromResult<(GeneratePlan, LaneAudio)?>(p); } };

        var outcome = await Writes(doc).GenerateAsync(host, Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Contains("changed before Generate was confirmed", outcome.Status);
        Assert.False(File.Exists(Path.Combine(MapDir, "Band - Song (Me) [Hitsounds].osu")));
    }

    [Fact]
    public async Task Generate_writes_nothing_when_the_map_is_closed_before_it_is_confirmed()
    {
        var doc = Map();
        var writes = Writes(doc);
        var host = new Host { Generate = p => { writes.Close(); return Task.FromResult<(GeneratePlan, LaneAudio)?>(p); } };

        var outcome = await writes.GenerateAsync(host, Pairing, NoSkin);

        Assert.False(outcome.Done);
        Assert.Null(outcome.Status); // the map that is open now has nothing to do with it
        Assert.False(File.Exists(Path.Combine(MapDir, "Band - Song (Me) [Hitsounds].osu")));
    }

    [Fact]
    public async Task A_cancelled_generate_says_so()
    {
        var doc = Map();
        var host = new Host { Generate = _ => Task.FromResult<(GeneratePlan, LaneAudio)?>(null) };

        var outcome = await Writes(doc).GenerateAsync(host, Pairing, NoSkin);

        Assert.Equal("Generate cancelled; nothing was written.", outcome.Status);
        Assert.False(File.Exists(Path.Combine(MapDir, "Band - Song (Me) [Hitsounds].osu")));
    }

    [Fact]
    public async Task Generate_writes_the_hitsounds_diff_and_records_it()
    {
        var doc = Map();
        doc.Session.Add([doc.Session.HitAt(Clap, 1000)]);
        var host = new Host();

        var outcome = await Writes(doc).GenerateAsync(host, Pairing, NoSkin);

        var generated = Path.Combine(MapDir, "Band - Song (Me) [Hitsounds].osu");
        Assert.True(outcome.Done);
        Assert.Equal(generated, outcome.GeneratedPath);
        Assert.StartsWith("Generated Band - Song (Me) [Hitsounds].osu: 2 circles", outcome.Status);
        Assert.True(File.Exists(generated));
        Assert.Single(Directory.EnumerateFiles(Manifests));
        Assert.Contains("Planning the hitsound diff…", host.Shown);
    }

    [Fact]
    public async Task A_second_write_while_one_runs_is_refused()
    {
        var doc = Map();
        doc.Session.Add([doc.Session.HitAt(Clap, 1000), doc.Session.HitAt(Clap, 5000)]);
        var answer = new TaskCompletionSource<bool>();
        var writes = Writes(doc);
        var saving = writes.SaveAsync(new Host { Confirm = (_, _) => answer.Task }, Pairing, NoSkin);
        Assert.True(writes.IsBusy);

        var second = await writes.GenerateAsync(new Host(), Pairing, NoSkin);
        answer.SetResult(false);
        await saving;

        Assert.False(second.Done);
        Assert.Equal(MapWrites.Busy, second);
        Assert.False(writes.IsBusy);
    }
}
