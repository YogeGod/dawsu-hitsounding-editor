using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed record MapCompilation(CompileResult Result, OsuFile File, IReadOnlyDictionary<MomentKey, MomentKey> Keys)
{
    public void Deconstruct(out CompileResult result, out OsuFile file) => (result, file) = (Result, File);
}

/// <summary>
/// One opened map and what the editor does with it, without any UI: its bytes as opened (Save and the background
/// compile always start again from these), its folder, the decompiled map and the edit session. Save writes only the
/// .osu; Generate plans from a fresh look at the folder.
/// </summary>
public sealed partial class MapDocument
{
    private readonly TimingPointIndex timing;

    private MapDocument(string path, byte[] opened, OsuFile file, MapFolder folder)
    {
        Path = path;
        Opened = opened;
        File = file;
        Folder = folder;
        Session = new EditSession(file, folder, Decompiler.Decompile(file, folder));
        timing = new TimingPointIndex(file.TimingPoints);
    }

    /// <summary>The .osu's full path.</summary>
    public string Path { get; }

    /// <summary>The .osu's bytes as opened.</summary>
    public byte[] Opened { get; }

    /// <summary>The parsed map as opened; the session's moments refer to its objects. Never written to.</summary>
    public OsuFile File { get; }

    /// <summary>The mapset folder's files: listed when opened, and again by <see cref="RescanFolder"/>.</summary>
    public MapFolder Folder { get; private set; }

    /// <summary>Lists the folder again (sample files added while the map is open) and returns the files that are new.</summary>
    public IReadOnlyList<string> RescanFolder()
    {
        var before = Folder.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Folder = MapFolder.FromDirectory(System.IO.Path.GetDirectoryName(Path)!);
        Session.UseFolder(Folder);
        return [.. Folder.Files.Where(f => !before.Contains(f))];
    }

    public DecompiledMap Map => Session.Map;

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

    /// <summary>Auditions a default row using the map's sample index at the timeline playhead; file rows keep their exact sound.</summary>
    public Lane LanePreviewAt(Lane lane, double time)
    {
        if (lane.Source != LaneSource.Skin || lane.Sample is not { } sample) return lane;
        try
        {
            int index = timing.SamplePointAt(time - Map.Settings.TimeOffset)?.SampleIndex ?? 0;
            return Decompiler.ResolveLane(Folder, sample.Set, sample.Sound, index);
        }
        catch (FormatException)
        {
            return lane;
        }
    }

    /// <summary>Compiles an edit state on a fresh copy of the map as opened (safe off the UI thread: the state is immutable).</summary>
    public MapCompilation Compile(EditState state, ExportMergeSettings? settings = null, IReadOnlyDictionary<string, byte[]>? skin = null)
    {
        var projection = Session.ProjectHs(state, Opened);
        var wanted = new Dictionary<MomentKey, WantedSound>(projection.Wanted);
        PairingPlanner? pairing = null;
        var preparationProblems = new List<Conflict>();
        if (settings?.PairSamples == true)
        {
            var direct = Compiler.Apply(OsuFile.Parse(projection.File.ToBytes()), Folder, projection.Wanted);
            var needsPreparation = direct.Conflicts.Select(c => c.Moment).ToHashSet();
            pairing = new(Folder, skin, settings);
            var hits = state.Hits.Where(h => h.Anchor is not null).ToLookup(h => h.Anchor!.Value);
            foreach (var (key, projected) in projection.Keys)
                try
                {
                    var layers = hits[key].Select(h => (h.Lane, Volume: EditSession.VolumeOf(state, h))).ToList();
                    bool encoded = !needsPreparation.Contains(projected) && layers.Select(l => l.Volume).Distinct().Count() <= 1 && layers.Select(l => l.Lane).Distinct().Count() == layers.Count;
                    wanted[projected] = pairing.For(layers, hits[key].FirstOrDefault()?.Time ?? double.NaN, projected, encoded,
                        exactGainLanes: hits[key].Where(h => h.SetPlacement != Guid.Empty).Select(h => h.Lane).ToHashSet());
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException)
                { preparationProblems.Add(new(hits[key].FirstOrDefault()?.Time ?? 0, projected, ex.Message)); }
        }
        var result = Compiler.Apply(projection.File, pairing is null ? Folder : Folder.With(pairing.Files.Select(m => m.FileName)), wanted, settings?.Style ?? ExportStyle.PerObject);
        var reverse = projection.Keys.ToDictionary(p => p.Value, p => p.Key);
        var conflicts = preparationProblems.Concat(pairing?.Conflicts ?? []).Concat(result.Conflicts).DistinctBy(c => c.Moment).Select(c => c with { Moment = reverse[c.Moment] }).ToList();
        var conflicted = conflicts.Select(c => c.Moment).ToHashSet();
        foreach (var issue in Session.SoundSetInputProblems(Folder, state))
            conflicts.Add(new(issue.Hit.Time, issue.Hit.Anchor ?? new(-1, 0), issue.Error));
        foreach (var group in state.Hits.Where(h => h.Anchor is not null).GroupBy(h => h.Anchor!.Value))
            if (pairing is null && !conflicted.Contains(group.Key) && (group.Select(h => EditSession.VolumeOf(state, h)).Distinct().Count() > 1
                || group.Select(h => h.Lane).Distinct().Count() != group.Count()))
                conflicts.Add(new(group.First().Time, group.Key, "These layers need a mix to preserve their individual volumes or repeated sounds."));
        return new(result with { Conflicts = conflicts.OrderBy(c => c.Time).ToList() }, projection.File, projection.Keys);
    }

    /// <summary>Saves the current edits and bookmarks; see <see cref="MapSaver.Save"/>.</summary>
    /// <exception cref="IOException">The file changed on disk since it was opened, or couldn't be written.</exception>
    public SaveResult Save(string backupRoot)
    {
        if (Session.HasHsObjects) throw new InvalidOperationException("Use the HS save plan to preserve generated circles and mix incompatible sounds.");
        var state = Session.State;
        return MapSaver.Save(Path, Opened, Folder, Session.Wanted(state), backupRoot, state.Bookmarks,
            alsoEdit: copy => EditSession.ApplySlides(state, copy.HitObjects));
    }

    /// <summary>
    /// Plans the [Hitsounds] diff from a fresh look at the folder (another difficulty may have changed since the map was
    /// opened), with what the last Generate for that diff recorded. Returns the folder it planned against.
    /// Slider body/tick audio is excluded unless explicitly requested; heads, repeats and tails remain included.
    /// </summary>
    /// <exception cref="IOException">A difficulty or storyboard in the folder couldn't be read.</exception>
    public (GeneratePlan Plan, MapFolder Folder) PlanGenerate(string manifestRoot, IReadOnlyDictionary<string, byte[]>? skin = null, ExportMergeSettings? mergeSettings = null, bool includeSliderAudio = false)
    {
        var current = MapFolder.FromDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (Session.SoundSetInputProblems(current).FirstOrDefault().Error is { } problem) throw new IOException(problem);
        var previous = GenerateManifest.Load(manifestRoot, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, HitsoundDiff.FileNameFor(File, Path)));
        return (HitsoundDiff.Plan(OsuFile.Parse(Opened), Path, current, Session, previous, skin, mergeSettings, includeSliderAudio), current);
    }
}
