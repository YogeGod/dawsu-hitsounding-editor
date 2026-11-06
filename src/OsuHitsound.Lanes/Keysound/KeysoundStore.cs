using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>What <c>keysound.dawsuks</c> holds: the editable tracks and notes, and which reference snapshot they belong to.</summary>
public sealed record KeysoundProjectFile
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;

    /// <summary>The reference .osu inside the project's reference folder, relative to it.</summary>
    public string Reference { get; init; } = "";

    /// <summary>Where the reference was copied from, and its hash then: for a later explicit "Update reference".</summary>
    public string? SourcePath { get; init; }
    public string? SourceHash { get; init; }

    public ImmutableList<KeyTrack> Tracks { get; init; } = [];
    public Guid? SelectedTrack { get; init; }
    public List<Guid> Muted { get; init; } = [];
    public List<Guid> Soloed { get; init; } = [];
    /// <summary>The song's listening offset for this project in ms (as Hitsound mode keeps one per map); not part of the map.</summary>
    public double SongOffset { get; init; }
}

/// <summary>
/// A gameplay object of the reference as the piano roll shows it, with its sync-target nodes: <see cref="Combo"/> is its
/// number (0 for spinners) and <see cref="Colour"/> counts new combos, for the combo colour.
/// </summary>
public sealed record KeyObject(double Start, double End, HitObjectKind Kind, int Combo, int Colour, IReadOnlyList<(double Time, HitPosition Position)> Nodes);

/// <summary>An open Keysound project: its folder, the reference snapshot (read-only) and the editable session.</summary>
public sealed class KeysoundDocument
{
    public required string ProjectPath { get; init; }
    public string Folder => Path.GetDirectoryName(ProjectPath)!;
    public required KeysoundProjectFile File { get; init; }
    public required OsuFile Reference { get; init; }
    public required MapSettings Settings { get; init; }
    public required TempoMap Tempo { get; init; }
    public required IReadOnlyList<KeyObject> Objects { get; init; }
    /// <summary>Every position a note can sync to (circles, slider heads, repeats and tails, spinner ends), by time.</summary>
    public IReadOnlyList<KeySyncTarget> Targets { get; init; } = [];
    private IReadOnlyDictionary<(int Object, int Node), KeySyncTarget>? targetIndex;
    public IReadOnlyDictionary<(int Object, int Node), KeySyncTarget> TargetIndex => targetIndex ??= KeySync.Index(Targets);
    /// <summary>The reference's [Colours] Combo1… as (r, g, b); empty when it sets none.</summary>
    public required IReadOnlyList<(byte R, byte G, byte B)> ComboColours { get; init; }
    /// <summary>The copied song audio, or null when the reference's audio file was missing (Generate will refuse).</summary>
    public string? SongPath { get; init; }
    public required KeysoundSession Session { get; init; }
    public List<string> Warnings { get; init; } = [];
    public Guid? SelectedTrack { get; set; }
    /// <summary>The project's song offset in ms (saved with the project).</summary>
    public double SongOffset { get; set; }

    public string Name => Path.GetFileNameWithoutExtension(File.Reference);
}

/// <summary>
/// Creates, opens and saves Keysound project folders: <c>keysound.dawsuks</c> beside a <c>reference</c> folder holding
/// copies of the reference .osu, its song audio and the visual assets its events and storyboard use. The source mapset is
/// only read, never written.
/// </summary>
public static class KeysoundStore
{
    public const string ProjectFileName = "keysound.dawsuks", Extension = ".dawsuks", ReferenceFolder = "reference";
    private const long MaxProjectBytes = 256L * 1024 * 1024, MaxAssetBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxTracks = 100_000, MaxNotes = 10_000_000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "dawsu! Keysound");

    /// <summary>A new folder under <paramref name="root"/> named after the difficulty, numbered when the name is taken.</summary>
    public static string SuggestFolder(string root, string osuPath)
    {
        var name = SafeFile.Name(Path.GetFileNameWithoutExtension(osuPath)).Trim().TrimEnd('.');
        if (name.Length > 80) name = name[..80].TrimEnd();
        if (name.Length == 0) name = "Keysound project";
        return SafeFile.FreeFolder(root, name);
    }

    /// <summary>
    /// Makes a project in <paramref name="projectFolder"/> (missing or empty) from a reference difficulty, with one track
    /// using the bundled piano. Missing visual assets and a missing song are reported in <paramref name="warnings"/>.
    /// Returns the project file's path. On failure the half-made folder is removed.
    /// </summary>
    public static string Create(string osuPath, string projectFolder, ICollection<string> warnings)
    {
        osuPath = Path.GetFullPath(osuPath);
        projectFolder = Path.GetFullPath(projectFolder);
        if (System.IO.File.Exists(projectFolder) || Directory.Exists(projectFolder) && Directory.EnumerateFileSystemEntries(projectFolder).Any())
            throw new IOException("Choose a new or empty folder for the Keysound project.");
        var mapset = Path.GetDirectoryName(osuPath) ?? throw new ArgumentException("The beatmap has no folder.", nameof(osuPath));
        // A copy inside the mapset would show up in osu! as another difficulty, and would copy itself.
        if (SafeFile.IsInside(projectFolder, mapset))
            throw new IOException("Keep the Keysound project outside the beatmap's folder.");
        var source = MapFolder.FromDirectory(mapset);
        var bytes = System.IO.File.ReadAllBytes(osuPath);
        var file = OsuFile.Parse(bytes);
        var settings = MapSettings.Read(file);

        var wanted = new List<(string Path, string What)> { (Path.GetFileName(osuPath), "beatmap") };
        if (settings.AudioFilename.Length > 0) wanted.Add((settings.AudioFilename, "song audio"));
        wanted.AddRange(Storyboard.VisualPaths(file).Select(p => (p, "image or video")));
        foreach (var osb in source.FilesWithExtension(".osb"))
        {
            wanted.Add((osb, "storyboard"));
            try { wanted.AddRange(Storyboard.VisualPaths(OsuFile.Parse(source.ReadBytes(osb))).Select(p => (p, "storyboard image"))); }
            catch (Exception ex) when (ex is FormatException or IOException) { warnings.Add($"Couldn't read {osb}: {ex.Message}"); }
        }

        bool created = !Directory.Exists(projectFolder);
        var referenceRoot = Path.Combine(projectFolder, ReferenceFolder);
        try
        {
            Directory.CreateDirectory(referenceRoot);
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, what) in wanted)
            {
                if (source.Find(path) is not { } actual)
                {
                    warnings.Add(what == "song audio"
                        ? $"The song audio \"{path}\" is missing. You can edit, but Generate needs the song."
                        : $"The {what} \"{path}\" is missing and wasn't copied.");
                    continue;
                }

                if (!copied.Add(actual)) continue;
                CopyInto(Path.Combine(mapset, actual), referenceRoot, actual, warnings);
            }

            var project = new KeysoundProjectFile
            {
                Reference = Path.GetFileName(osuPath),
                SourcePath = osuPath,
                SourceHash = GenerateManifest.Hash(bytes),
                Tracks = [KeyTrack.Create("Piano")],
            };
            var projectPath = Path.Combine(projectFolder, ProjectFileName);
            Write(projectPath, project);
            return projectPath;
        }
        catch
        {
            TryDelete(created ? projectFolder : referenceRoot);
            throw;
        }
    }

    private static void CopyInto(string from, string root, string relative, ICollection<string> warnings)
    {
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!SafeFile.IsInside(target, root))
            throw new IOException($"\"{relative}\" would be copied outside the project.");
        var info = new FileInfo(from);
        if (info.Length > MaxAssetBytes)
        {
            warnings.Add($"\"{relative}\" is larger than 2 GB and wasn't copied.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        System.IO.File.Copy(from, target, overwrite: false);
    }

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Opens a project file. Throws <see cref="FormatException"/> for a damaged project or a missing reference.</summary>
    public static KeysoundDocument Open(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (new FileInfo(projectPath).Length > MaxProjectBytes) throw new FormatException("The Keysound project is too large.");
        KeysoundProjectFile project;
        try
        {
            project = JsonSerializer.Deserialize<KeysoundProjectFile>(System.IO.File.ReadAllBytes(projectPath), Json)
                ?? throw new FormatException("The Keysound project is empty.");
        }
        catch (JsonException ex) { throw new FormatException($"The Keysound project can't be read: {ex.Message}", ex); }

        Validate(project);
        var folder = Path.GetDirectoryName(projectPath)!;
        var reference = MapFolder.FromDirectory(Path.Combine(folder, ReferenceFolder));
        if (reference.Find(project.Reference) is not { } osuName)
            throw new FormatException($"The project's reference beatmap \"{project.Reference}\" is missing from its reference folder.");
        var file = OsuFile.Parse(reference.ReadBytes(osuName));
        var settings = MapSettings.Read(file);
        var warnings = new List<string>();
        var song = settings.AudioFilename.Length > 0 ? reference.Find(settings.AudioFilename) : null;
        if (song is null) warnings.Add("The reference's song audio is missing from the project. You can edit, but Generate needs the song.");

        var session = new KeysoundSession(new KeysoundState(project.Tracks));
        var ids = project.Tracks.Select(t => t.Id).ToHashSet();
        session.Muted.UnionWith(project.Muted.Where(ids.Contains));
        session.Soloed.UnionWith(project.Soloed.Where(ids.Contains));
        var (objects, targets) = BuildObjects(file, reference, settings.TimeOffset, warnings);
        return new KeysoundDocument
        {
            ProjectPath = projectPath,
            File = project,
            Reference = file,
            Settings = settings,
            Tempo = TempoMap.FromBeatmap(file, settings.TimeOffset),
            Objects = objects,
            Targets = targets,
            ComboColours = ComboColours(file),
            SongPath = song is null ? null : Path.Combine(folder, ReferenceFolder, song),
            Session = session,
            Warnings = warnings,
            SelectedTrack = project.SelectedTrack is { } sel && ids.Contains(sel) ? sel : project.Tracks.FirstOrDefault()?.Id,
            SongOffset = double.IsFinite(project.SongOffset) ? Math.Clamp(project.SongOffset, -10000, 10000) : 0,
        };
    }

    /// <summary>Writes the session's tracks into the project file and marks the session saved.</summary>
    public static void Save(KeysoundDocument document)
    {
        var session = document.Session;
        var state = session.State;
        Write(document.ProjectPath, document.File with
        {
            Tracks = state.Tracks,
            SelectedTrack = document.SelectedTrack,
            Muted = [.. session.Muted],
            Soloed = [.. session.Soloed],
            SongOffset = document.SongOffset,
        });
        session.MarkSaved(state);
    }

    private static void Write(string path, KeysoundProjectFile project)
    {
        Validate(project);
        SafeFile.WriteAllBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, Json)));
    }

    public static void Validate(KeysoundProjectFile project)
    {
        if (project.Version != KeysoundProjectFile.CurrentVersion)
            throw new FormatException($"This Keysound project is version {project.Version}; this dawsu! reads version {KeysoundProjectFile.CurrentVersion}.");
        if (string.IsNullOrWhiteSpace(project.Reference) || project.Reference.Length > 1024 || !MapFolder.IsInside(project.Reference)
            || !project.Reference.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The project's reference beatmap path is invalid.");
        var tracks = project.Tracks ?? throw new FormatException("The project has no track list.");
        if (tracks.Count > MaxTracks || tracks.Sum(t => (long)(t?.Notes?.Count ?? 0)) > MaxNotes)
            throw new FormatException("The project has too many tracks or notes.");
        if (project.Muted is null || project.Soloed is null) throw new FormatException("The project's mute or solo list is invalid.");
        var trackIds = new HashSet<Guid>();
        var noteIds = new HashSet<Guid>();
        foreach (var t in tracks)
        {
            if (t is null || t.Id == Guid.Empty || !trackIds.Add(t.Id) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 256
                || t.Instrument is not { Kind.Length: > 0 and <= 64 } i || i.Path?.Length > 4096 || i.State?.Length > 96 * 1024 * 1024
                || i.ClassId is { } classId && (classId.Length != 32 || !classId.All(char.IsAsciiHexDigit)) || i.Name?.Length > 256 || i.IsVst3 && string.IsNullOrWhiteSpace(i.Path) || t.Notes is null)
                throw new FormatException("A track in the project is invalid.");
            foreach (var n in t.Notes)
                if (n is null || !n.IsValid || !noteIds.Add(n.Id)) throw new FormatException($"A note on track \"{t.Name}\" is invalid.");
            if (t.Controllers is null || t.Controllers.Count > MaxNotes || t.Controllers.Any(c => c is null || !c.IsValid)
                || !(t.VolumeDb >= KeyTrack.MinVolumeDb && t.VolumeDb <= KeyTrack.MaxVolumeDb))
                throw new FormatException($"A controller or the volume of track \"{t.Name}\" is invalid.");
        }
    }

    /// <summary>The reference's gameplay objects in play time, numbered by combo, with their circles, slider nodes and spinner ends.</summary>
    private static (List<KeyObject> Objects, List<KeySyncTarget> Targets) BuildObjects(OsuFile file, MapFolder folder, double offset, ICollection<string> warnings)
    {
        DecompiledMap map;
        try { map = Decompiler.Decompile(file, folder); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            warnings.Add($"Couldn't read the reference's objects: {ex.Message}");
            return ([], []);
        }

        var index = new Dictionary<HitObject, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < file.HitObjects.Count; i++) index[file.HitObjects[i]] = i;
        var targets = map.Moments.Where(m => index.ContainsKey(m.Object))
            .Select(m => new KeySyncTarget(index[m.Object], m.Node, m.Time + offset, m.Position))
            .OrderBy(t => t.Time).ThenBy(t => t.ObjectIndex).ThenBy(t => t.Node).ToList();

        var nodes = map.Moments.GroupBy(m => m.Object, ReferenceEqualityComparer.Instance)
            .ToDictionary(g => g.Key, g => g.Select(m => (m.Time + offset, m.Position)).OrderBy(n => n.Item1).ToList(), ReferenceEqualityComparer.Instance);
        var objects = new List<KeyObject>();
        int combo = 0, colour = -1;
        bool afterSpinner = true;
        foreach (var o in file.HitObjects)
        {
            bool spinner = o.Kind == HitObjectKind.Spinner;
            if ((o.Type & 4) != 0 || afterSpinner)
            {
                combo = 1;
                colour++;
            }
            else combo++;
            afterSpinner = spinner;
            if (!nodes.TryGetValue(o, out var n) || n.Count == 0) continue;
            double start = spinner ? o.Time + offset : n[0].Item1;
            objects.Add(new KeyObject(start, n[^1].Item1, o.Kind, spinner ? 0 : combo, Math.Max(colour, 0), n));
        }

        objects.Sort((a, b) => a.Start.CompareTo(b.Start));
        return (objects, targets);
    }

    private static List<(byte R, byte G, byte B)> ComboColours(OsuFile file) =>
    [
        .. file.Lines.Where(l => l.Section == "Colours" && l.Text.TrimStart().StartsWith("Combo", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Text.Split(':', 2))
            .Where(kv => kv.Length == 2 && int.TryParse(kv[0].Trim()[5..], out _))
            .OrderBy(kv => int.Parse(kv[0].Trim()[5..]))
            .Select(kv => kv[1].Split(','))
            .Where(c => c.Length >= 3 && c.Take(3).All(x => byte.TryParse(x.Trim(), out _)))
            .Select(c => (byte.Parse(c[0].Trim()), byte.Parse(c[1].Trim()), byte.Parse(c[2].Trim()))),
    ];
}
