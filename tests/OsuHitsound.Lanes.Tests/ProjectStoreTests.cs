using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class ProjectStoreTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hse-project-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_project_saves_and_loads_by_map_path()
    {
        var data = new ProjectData
        {
            Unanchored = [new ProjectHit("Skin|soft-hitclap", 1234.5)],
            LaneNames = new() { ["BeatmapFile|drum-hitclap2.ogg"] = "big clap" },
            LocatorLeft = 100,
            LocatorRight = 900,
            Cycle = true,
            SongOffset = -20,
        };

        ProjectStore.Save(root, @"C:\Songs\Map\a [x].osu", data);
        var loaded = ProjectStore.Load(root, @"C:\Songs\Map\a [x].osu")!;

        Assert.Equal(data.Unanchored, loaded.Unanchored);
        Assert.Equal("big clap", loaded.LaneNames["BeatmapFile|drum-hitclap2.ogg"]);
        Assert.Equal((100.0, 900.0, true, -20.0), (loaded.LocatorLeft!.Value, loaded.LocatorRight!.Value, loaded.Cycle, loaded.SongOffset));
        Assert.Null(ProjectStore.Load(root, @"C:\Songs\Other\a [x].osu")); // same name, other folder: another project
        Assert.StartsWith(root, ProjectStore.PathFor(root, @"C:\Songs\Map\a [x].osu"));
    }

    [Fact]
    public void Lane_keys_round_trip()
    {
        var lane = new Lane(LaneSource.BeatmapFile, "sfx|odd name.wav");
        Assert.Equal(lane, ProjectStore.LaneOf(ProjectStore.KeyOf(lane)));
        Assert.Null(ProjectStore.LaneOf("nonsense"));
    }
}
