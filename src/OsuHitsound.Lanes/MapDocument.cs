using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// One opened map and what the editor does with it, without any UI: its bytes as opened (Save and the background
/// compile always start again from these), its folder, the decompiled map and the edit session. Save writes only the
/// .osu; Generate plans from a fresh look at the folder.
/// </summary>
public sealed class MapDocument
{
    private MapDocument(string path, byte[] opened, OsuFile file, MapFolder folder)
    {
        Path = path;
        Opened = opened;
        File = file;
        Folder = folder;
        Map = Decompiler.Decompile(file, folder);
        Session = new EditSession(file, folder, Map);
    }

    /// <summary>The .osu's full path.</summary>
    public string Path { get; }

    /// <summary>The .osu's bytes as opened.</summary>
    public byte[] Opened { get; }

    /// <summary>The parsed map as opened; the session's moments refer to its objects. Never written to.</summary>
    public OsuFile File { get; }

    public MapFolder Folder { get; }

    public DecompiledMap Map { get; }

    public EditSession Session { get; }

    /// <summary>Reads and decompiles a map and lists its folder (slow for big mapsets: call off the UI thread).</summary>
    public static MapDocument Load(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var bytes = System.IO.File.ReadAllBytes(full);
        return new MapDocument(full, bytes, OsuFile.Parse(bytes), MapFolder.FromDirectory(System.IO.Path.GetDirectoryName(full)!));
    }

    /// <summary>A document over given bytes and folder, for tests.</summary>
    public static MapDocument FromBytes(string path, byte[] bytes, MapFolder folder) => new(path, bytes, OsuFile.Parse(bytes), folder);

    /// <summary>Compiles an edit state on a fresh copy of the map as opened (safe off the UI thread: the state is immutable).</summary>
    public (CompileResult Result, OsuFile File) Compile(EditState state)
    {
        var copy = OsuFile.Parse(Opened);
        return (Compiler.Apply(copy, Folder, Session.Wanted(state)), copy);
    }

    /// <summary>Saves the current edits and bookmarks; see <see cref="MapSaver.Save"/>.</summary>
    /// <exception cref="IOException">The file changed on disk since it was opened, or couldn't be written.</exception>
    public SaveResult Save(string backupRoot) =>
        MapSaver.Save(Path, Opened, Folder, Session.Wanted(), backupRoot, Session.State.Bookmarks);

    /// <summary>
    /// Plans the [Hitsounds] diff from a fresh look at the folder (another difficulty may have changed since the map was
    /// opened), with what the last Generate for that diff recorded. Returns the folder it planned against.
    /// </summary>
    /// <exception cref="IOException">A difficulty or storyboard in the folder couldn't be read.</exception>
    public (GeneratePlan Plan, MapFolder Folder) PlanGenerate(string manifestRoot)
    {
        var current = MapFolder.FromDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var previous = GenerateManifest.Load(manifestRoot, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, HitsoundDiff.FileNameFor(File, Path)));
        return (HitsoundDiff.Plan(OsuFile.Parse(Opened), Path, current, Session, previous), current);
    }
}
