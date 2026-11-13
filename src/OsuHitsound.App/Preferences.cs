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
    public string? HitsoundFolder { get; init; }
    public string? LastSampleFolder { get; init; }
    public string? AutoFillKickLane { get; init; }
    public string? AutoFillSnareLane { get; init; }
    public bool ShowMissingObjectWarnings { get; init; }
    public bool ShowExportWarnings { get; init; } = true;
    /// <summary>After Save, make osu!stable's editor reload the map when it has the same one open (Ctrl+L, Enter).</summary>
    public bool ReloadOsuAfterSave { get; init; } = true;
    /// <summary>Keep sounds from different sample indexes or addition sets on one object instead of moving the object to the new ones.</summary>
    public bool KeepMixedIndexes { get; init; }
    public ExportStyle ExportStyle { get; init; } = ExportStyle.GreenLines;

    /// <summary>How the sound reaches the speakers: WASAPI shared (the default), WASAPI exclusive, ASIO, DirectSound or MME.</summary>
    public OutputKind AudioOutput { get; init; } = OutputKind.WasapiShared;

    /// <summary>The device for <see cref="AudioOutput"/> (an id from AudioEngine.Devices); null is the system default.</summary>
    public string? OutputDevice { get; init; }

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

    /// <summary>How editing snaps: Grid, Grid relative, or Objects.</summary>
    public SnapMode SnapMode { get; init; } = SnapMode.Grid;

    /// <summary>The strength Q quantizes with, in %.</summary>
    public double QuantizeStrength { get; init; } = 100;

    /// <summary>View → Colour by Volume instead of by lane.</summary>

    /// <summary>Audition drawn notes and notes moved between lanes.</summary>
    public bool AuditionOnPlacement { get; init; } = true;

    /// <summary>Volume for newly drawn hits; null follows the map.</summary>
    public int? InsertVolume { get; init; }

    public bool ProtectHitnormals { get; init; }

    /// <summary>The inspector on the left: shown, its width, its cards top to bottom and the folded ones.</summary>
    public bool InspectorOpen { get; init; } = true;

    public bool BrowserOpen { get; init; } = true;
    public bool ClassicBrowser { get; init; }
    public double BrowserWidth { get; init; } = 224;
    public bool HistoryOpen { get; init; }
    public bool SoundSetsOpen { get; init; }

    /// <summary>The map preview window (View → Map preview): shown, and where it was last.</summary>
    public bool PlayfieldOpen { get; init; }
    public double? PlayfieldX { get; init; }
    public double? PlayfieldY { get; init; }
    public double? PlayfieldWidth { get; init; }
    public double? PlayfieldHeight { get; init; }

    public double InspectorWidth { get; init; } = 208;

    public List<string> InspectorCards { get; init; } = [];

    public List<string> InspectorFolded { get; init; } = ["soundSets", "recent"];
    /// <summary>Keysound mode's inspector cards (Tracks, Instrument, MIDI input): order, folded ones, and whether it shows.</summary>
    public List<string> KeysoundCards { get; init; } = [];
    public List<string> KeysoundFolded { get; init; } = [];
    public bool KeysoundInspectorOpen { get; init; } = true;

    /// <summary>Global volume in % (20–100): the song, hitsounds and previews together.</summary>
    public double GlobalVolume { get; init; } = 20;

    /// <summary>The first-launch tutorial has been shown (File → Tutorial shows it again).</summary>
    public bool TutorialSeen { get; init; }

    /// <summary>The first-run question "DAW-like keys or osu! editor controls?" was answered (or the app was in use before it existed).</summary>
    public bool KeyStyleAsked { get; init; }

    /// <summary>When the installed app last looked for an update on its own (once a day at most).</summary>
    public DateTime? LastUpdateCheck { get; init; }

    /// <summary>
    /// Song offset (ms) for every map, added to each map's own Offset: maps are timed against osu!'s audio clock, which
    /// runs about 17 ms ahead of the raw audio played here (measured on a stable map; a lazer map needed 8 ms more).
    /// </summary>
    public double GlobalSongOffset { get; init; } = -17;

    /// <summary>The lane list's ‹: names hidden, only the icons and buttons shown.</summary>
    public bool LaneNamesCollapsed { get; init; }
    public double? LaneListWidth { get; init; }
    public bool SelectionBarCollapsed { get; init; }
    /// <summary>The old light/dark switch: decides the colour scheme until one is picked in Preferences, Colours.</summary>
    public bool DarkMode { get; init; } = true;

    /// <summary>Preferences, Colours: the scheme and the ticked colour rows.</summary>
    public UserColors Colors { get; init; } = new();

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
        catch (JsonException)
        {
            // Kept as preferences.json.unreadable: the next save would otherwise replace every key change and setting with defaults.
            SafeFile.KeepUnreadable(path);
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
