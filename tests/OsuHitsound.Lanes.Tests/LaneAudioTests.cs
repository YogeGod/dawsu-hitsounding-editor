using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class LaneAudioTests
{
    private static readonly Lane Clap = new(LaneSource.BeatmapFile, "soft-hitclap2.wav");
    private static readonly Lane SkinClap = new(LaneSource.Skin, "soft-hitclap");

    /// <summary>A folder whose files have the given contents, counting how often each is read.</summary>
    private static (MapFolder Folder, Dictionary<string, int> Reads) Folder(params (string Name, byte[] Bytes)[] files)
    {
        var reads = new Dictionary<string, int>();
        var contents = files.ToDictionary(f => f.Name, f => f.Bytes);
        return (new MapFolder(contents.Keys, name => { reads[name] = reads.GetValueOrDefault(name) + 1; return contents[name]; }), reads);
    }

    [Fact]
    public void A_file_lane_reads_the_mapset_file_and_records_its_hash()
    {
        var (folder, _) = Folder(("Soft-HitClap2.wav", [1, 2, 3]));
        var audio = new LaneAudio(folder);

        Assert.Equal([1, 2, 3], audio.Read(Clap));
        Assert.Equal(GenerateManifest.Hash([1, 2, 3]), audio.FilesRead["Soft-HitClap2.wav"]);
    }

    [Fact]
    public void A_skin_lane_reads_the_skin_and_is_not_recorded_as_a_mapset_file()
    {
        var audio = new LaneAudio(new MapFolder([]), new Dictionary<string, byte[]> { ["soft-hitclap"] = [7] });

        Assert.Equal([7], audio.Read(SkinClap));
        Assert.Empty(audio.FilesRead);
    }

    [Fact]
    public void A_storyboard_sample_reads_from_the_mapset_folder()
    {
        var (folder, _) = Folder(("sb/boom.ogg", [4]));
        Assert.Equal([4], new LaneAudio(folder).Read(new Lane(LaneSource.StoryboardFile, "sb/boom.ogg")));
    }

    public static TheoryData<string, Lane> MissingCases => new()
    {
        { "not in the folder", new Lane(LaneSource.BeatmapFile, "soft-hitclap3.wav") },
        { "not in the skin", new Lane(LaneSource.Skin, "drum-hitclap") },
        { "an empty file", new Lane(LaneSource.BeatmapFile, "empty.wav") },
        { "an empty skin sound", new Lane(LaneSource.Skin, "soft-hitwhistle") },
    };

    [Theory]
    [MemberData(nameof(MissingCases))]
    public void Missing_audio_is_null_from_TryRead_and_one_message_from_Read(string why, Lane lane)
    {
        var (folder, _) = Folder(("empty.wav", []));
        var audio = new LaneAudio(folder, new Dictionary<string, byte[]> { ["soft-hitwhistle"] = [] });

        Assert.Null(audio.TryRead(lane));
        Assert.Null(audio.TryHash(lane));
        Assert.Equal($"Missing {lane.Name}.", Assert.Throws<IOException>(() => audio.Read(lane)).Message);
        Assert.Equal($"Missing {lane.Name}.", Assert.Throws<IOException>(() => audio.Hash(lane)).Message);
        Assert.False(audio.FilesRead.ContainsKey(lane.Name), why);
    }

    [Fact]
    public void Without_a_skin_every_skin_lane_is_missing()
    {
        Assert.Null(new LaneAudio(new MapFolder([])).TryRead(SkinClap));
    }

    [Fact]
    public void A_folder_listing_names_only_has_no_audio()
    {
        Assert.Null(new LaneAudio(new MapFolder(["soft-hitclap2.wav"])).TryRead(Clap));
    }

    [Fact]
    public void A_locked_file_is_an_error_not_missing()
    {
        var dir = Directory.CreateTempSubdirectory("hse-laneaudio-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, Clap.Name), [1]);
            using var locked = new FileStream(Path.Combine(dir, Clap.Name), FileMode.Open, FileAccess.Read, FileShare.None);
            var error = Assert.ThrowsAny<IOException>(() => new LaneAudio(MapFolder.FromDirectory(dir)).TryRead(Clap));
            Assert.DoesNotContain("Missing", error.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Each_file_is_read_once_however_often_it_is_asked_for()
    {
        var (folder, reads) = Folder(("soft-hitclap2.wav", [1]));
        var audio = new LaneAudio(folder);

        audio.TryRead(Clap);
        audio.Read(Clap);
        Assert.Equal(GenerateManifest.Hash([1]), audio.TryHash(Clap));
        audio.TryHash(Clap);

        Assert.Equal(1, reads["soft-hitclap2.wav"]);
    }

    [Fact]
    public void With_more_files_lists_them_and_keeps_what_was_read()
    {
        var (folder, reads) = Folder(("soft-hitclap2.wav", [1]));
        var audio = new LaneAudio(folder);
        audio.Read(Clap);

        var more = audio.With(["soft-hitnormal9.wav"]);

        Assert.NotNull(more.Folder.Find("soft-hitnormal9.wav"));
        Assert.Equal([1], more.Read(Clap));
        Assert.Equal(1, reads["soft-hitclap2.wav"]);
        Assert.Equal(audio.FilesRead, more.FilesRead);
    }
}
