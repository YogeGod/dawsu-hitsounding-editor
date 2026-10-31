using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OsuHitsound.Lanes;

/// <summary>An editor-only time signature from a bar on (never written to the .osu): e.g. bar 33 in 7/8.</summary>
public sealed record TimeSignature(int Bar, int Numerator, int Denominator);

/// <summary>A hit stored in a project: its lane and timeline time.</summary>
public sealed record ProjectHit(string Lane, double Time);

/// <summary>
/// What a .osu can't hold, kept per map by the editor: hits with no object, lane names, order, added and hidden
/// lanes, cycle locators, preview mutes and the song offset. Lanes are stored as keys (see <see cref="ProjectStore.KeyOf"/>).
/// </summary>
public sealed record ProjectData
{
    public int Version { get; init; } = 1;

    public List<ProjectHit> Unanchored { get; init; } = [];

    public Dictionary<string, string> LaneNames { get; init; } = [];

    public List<string> LaneOrder { get; init; } = [];

    public List<string> AddedLanes { get; init; } = [];

    public List<string> HiddenLanes { get; init; } = [];

    public double? LocatorLeft { get; init; }

    public double? LocatorRight { get; init; }

    public bool Cycle { get; init; }

    public List<ProjectHit> PreviewMuted { get; init; } = [];

    public double SongOffset { get; init; }

    /// <summary>Editor-only time signature changes, by bar.</summary>
    public List<TimeSignature> TimeSignatures { get; init; } = [];
}

/// <summary>
/// Reads and writes <see cref="ProjectData"/> under the editor's app-data folder (never in the mapset), one file per
/// map, named after the map and a hash of its full path.
/// </summary>
public static class ProjectStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "projects");

    /// <summary>A lane's stable key: its source and name ("Skin|soft-hitclap", "BeatmapFile|drum-hitclap2.ogg").</summary>
    public static string KeyOf(Lane lane) => $"{lane.Source}|{lane.Name}";

    public static Lane? LaneOf(string key) =>
        key.Split('|', 2) is [var source, var name] && Enum.TryParse(source, out LaneSource s) ? new Lane(s, name) : null;

    public static string PathFor(string root, string osuPath)
    {
        var full = Path.GetFullPath(osuPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..12];
        var name = SafeFile.Name(Path.GetFileNameWithoutExtension(full));
        if (name.Length > 80) name = name[..80];
        return Path.Combine(root, $"{name}-{hash}.json");
    }

    /// <summary>
    /// The project for a map, or null when there is none or it can't be read. A file that can't be read is kept as
    /// .unreadable rather than overwritten by the next save.
    /// </summary>
    public static ProjectData? Load(string root, string osuPath)
    {
        var path = PathFor(root, osuPath);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path), Json) : null;
        }
        catch (JsonException)
        {
            try
            {
                File.Move(path, path + ".unreadable", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string root, string osuPath, ProjectData data)
    {
        var path = PathFor(root, osuPath);
        Directory.CreateDirectory(root);
        SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, Json)));
    }
}
