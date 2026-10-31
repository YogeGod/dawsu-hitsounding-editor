using System.Text;
using System.Text.Json;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// Editor preferences, kept in %LocalAppData%\OsuHitsoundEditor\preferences.json: the osu! Songs folder, audio output,
/// where the default sounds come from, recent maps and the window's place. Missing or unreadable files give defaults.
/// </summary>
internal sealed record Preferences
{
    public const int MaxRecent = 10;

    /// <summary>Where Open starts; null until found or chosen.</summary>
    public string? SongsFolder { get; init; }

    /// <summary>BASS output device number; -1 is the system default.</summary>
    public int AudioDevice { get; init; } = -1;

    /// <summary>Playback buffer in ms: smaller reacts faster, larger is safer against crackles.</summary>
    public int BufferMs { get; init; } = AudioEngine.DefaultBufferMs;

    /// <summary>osu.Game.Resources.dll to read the default sounds from; null uses the standard lazer install.</summary>
    public string? LazerResources { get; init; }

    /// <summary>Most recent first.</summary>
    public List<string> RecentMaps { get; init; } = [];

    /// <summary>The key set to start from; Cubase falls back to the built-in keys when its file can't be found.</summary>
    public KeyPreset KeyPreset { get; init; } = KeyPreset.Cubase;

    /// <summary>Cubase's Key Commands.xml; null finds the newest Cubase's file.</summary>
    public string? CubaseKeyFile { get; init; }

    /// <summary>Preset name → command id → the user's keys for it (see <see cref="Keymap.Format"/>), kept apart per preset.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> KeyOverrides { get; init; } = [];

    /// <summary>How dragging snaps (Grid or Grid relative).</summary>
    public SnapMode SnapMode { get; init; } = SnapMode.Grid;

    /// <summary>The strength Q quantizes with, in %.</summary>
    public double QuantizeStrength { get; init; } = 100;

    /// <summary>View → Colour by Volume instead of by lane.</summary>
    public bool ColourByVolume { get; init; }

    /// <summary>The inspector on the left: shown, its width, its cards top to bottom and the folded ones.</summary>
    public bool InspectorOpen { get; init; } = true;

    public double InspectorWidth { get; init; } = 250;

    public List<string> InspectorCards { get; init; } = [];

    public List<string> InspectorFolded { get; init; } = [];

    public double? WindowX { get; init; }

    public double? WindowY { get; init; }

    public double? WindowWidth { get; init; }

    public double? WindowHeight { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OsuHitsoundEditor", "preferences.json");

    public static Preferences Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json)));
    }

    /// <summary>The recent list with this map first, no duplicates (paths compare case-insensitively), at most 10.</summary>
    public Preferences WithRecent(string map) => this with
    {
        RecentMaps = [.. new[] { Path.GetFullPath(map) }.Concat(RecentMaps)
            .DistinctBy(p => p.ToLowerInvariant()).Take(MaxRecent)],
    };

    /// <summary>The recent list without maps that no longer exist.</summary>
    public Preferences WithoutMissing(Func<string, bool> exists) => this with { RecentMaps = [.. RecentMaps.Where(exists)] };

    /// <summary>A Songs folder in a usual place: stable's default under %LocalAppData%, or osu!\Songs on any drive.</summary>
    public static string? FindSongsFolder(Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var candidates = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Songs") };
        candidates.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => Path.Combine(d.RootDirectory.FullName, "osu!", "Songs")));
        return candidates.FirstOrDefault(directoryExists);
    }
}
