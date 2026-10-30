using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class MapSaverTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hse-save-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private (string Path, byte[] Bytes, MapFolder Folder, EditSession Session) Setup()
    {
        var map = Path.Combine(dir, "mapset");
        Directory.CreateDirectory(map);
        var path = Path.Combine(map, "a.osu");
        var bytes = Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[General]\r\nSampleSet: Soft\r\n\r\n[Difficulty]\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            "[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n\r\n[HitObjects]\r\n256,192,1000,1,0,0:0:0:0:\r\n");
        File.WriteAllBytes(path, bytes);
        var folder = MapFolder.FromDirectory(map);
        var file = OsuFile.Parse(bytes);
        return (path, bytes, folder, new EditSession(file, folder, Decompiler.Decompile(file, folder)));
    }

    [Fact]
    public void Saves_the_osu_and_keeps_the_original_as_a_backup_outside_the_mapset()
    {
        var (path, bytes, folder, session) = Setup();
        session.Add([session.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 1000)]);

        var saved = MapSaver.Save(path, bytes, folder, session.Wanted(), Path.Combine(dir, "backups"));

        Assert.True(saved.Written);
        Assert.Contains("256,192,1000,1,8,0:0:0:0:", File.ReadAllText(path));
        Assert.Equal(bytes, File.ReadAllBytes(saved.Backup!));
        Assert.StartsWith(Path.Combine(dir, "backups"), saved.Backup);
        Assert.Equal(["a.osu"], Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName)); // no temp left behind
    }

    [Fact]
    public void Nothing_wanted_writes_nothing()
    {
        var (path, bytes, folder, session) = Setup();
        var saved = MapSaver.Save(path, bytes, folder, session.Wanted(), Path.Combine(dir, "backups"));
        Assert.False(saved.Written);
        Assert.False(Directory.Exists(Path.Combine(dir, "backups")));
    }

    [Fact]
    public void A_file_changed_on_disk_since_opening_is_not_overwritten()
    {
        var (path, bytes, folder, session) = Setup();
        File.AppendAllText(path, "// edited elsewhere\r\n");
        session.Add([session.HitAt(new Lane(LaneSource.Skin, "soft-hitclap"), 1000)]);

        Assert.Throws<IOException>(() => MapSaver.Save(path, bytes, folder, session.Wanted(), Path.Combine(dir, "backups")));
        Assert.EndsWith("// edited elsewhere\r\n", File.ReadAllText(path));
    }
}
