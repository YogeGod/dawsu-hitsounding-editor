using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

public sealed record HsSavePlan(GeneratePlan Storage, HsProject Project, EditState State)
{
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed partial class MapDocument
{
    private HsProject? savedHs;
    public HsProject HsProject => (savedHs ?? new HsProject(false, Session.HsStartedEmpty, GenerateManifest.Hash(Opened), []))
        with { Enabled = Session.State.HsMode, StartedEmpty = Session.HsStartedEmpty };

    public void RestoreHsProject(HsProject? project)
    {
        string hash = GenerateManifest.Hash(Opened);
        savedHs = project?.FileHash == hash && project.Moments is not null ? project : null;
        Session.RestoreHsProject(project, hash);
    }

    public (HsSavePlan Plan, MapFolder Folder) PlanHsSave(string manifestRoot, IReadOnlyDictionary<string, byte[]>? skin = null,
        ExportMergeSettings? settings = null)
    {
        settings?.Validate();
        var state = Session.State;
        var projection = Session.ProjectHs(state, Opened);
        projection.File.SetBookmarks(state.Bookmarks);
        var folder = MapFolder.FromDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var first = Compiler.Apply(OsuFile.Parse(projection.File.ToBytes()), folder, projection.Wanted);
        var needsMix = first.Conflicts.Select(c => c.Moment).ToHashSet();
        var inputs = state.Hits.Where(h => h.Anchor is not null).ToLookup(h => h.Anchor!.Value);
        var layers = projection.Keys.ToDictionary(p => p.Value,
            p => MixPlanner.Normalize(inputs[p.Key].Select(h => (h.Lane, EditSession.VolumeOf(state, h))), includeMissing: true));
        foreach (var (key, sounds) in layers)
            if (sounds.Select(l => l.Volume).Distinct().Count() > 1 || sounds.Select(l => l.Lane).Distinct().Count() != sounds.Count)
                needsMix.Add(key);
        var fingerprints = new Dictionary<Lane, string>();
        string Fingerprint(Lane lane)
        {
            if (fingerprints.TryGetValue(lane, out var hash)) return hash;
            byte[]? bytes = lane.Source == LaneSource.Skin ? skin?.GetValueOrDefault(lane.Name)
                : folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null;
            return fingerprints[lane] = bytes is null ? "unknown" : GenerateManifest.Hash(bytes);
        }
        var combinations = projection.Wanted.ToDictionary(p => p.Key,
            p => new MergeCombination(layers[p.Key], []));
        var candidates = combinations.Where(p => needsMix.Contains(p.Key) && p.Value.Layers.Count > 1)
            .Select(p => p.Key).ToList();
        foreach (var key in candidates)
            combinations[key] = combinations[key] with { Fingerprints = layers[key].Select(l => l.Lane).Distinct().ToDictionary(l => l, Fingerprint) };
        var destinations = new Dictionary<string, string>();
        if (settings is not null)
            foreach (var key in candidates)
            {
                destinations[MixFile.SignatureOf(layers[key])] = settings.Destination(combinations[key])
                    ?? throw new InvalidOperationException("Choose a destination for every combination before saving.");
                needsMix.Add(key);
            }
        // An object intentionally kept while HS mode is off still needs an explicitly silent normal.
        foreach (var key in needsMix.Where(k => layers[k].Count == 0)) destinations[MixFile.SignatureOf(layers[key])] = "soft-hitnormal";
        var previous = GenerateManifest.Load(manifestRoot, Path);
        var mixer = new MixPlanner(folder, previous, System.IO.Path.GetFileName(Path), needsMix.Select(k => layers[k]), skin,
            liveLanes: state.Hits.Select(h => h.Lane).Concat(Map.Passive.Select(p => p.Lane)), destinations: destinations);
        var wishes = new Dictionary<MomentKey, WantedSound>(projection.Wanted);
        foreach (var key in needsMix)
            wishes[key] = mixer.MixFor(layers[key]).Wanted();
        var result = Compiler.Apply(projection.File, folder.With(mixer.All.Select(m => m.FileName)), wishes);
        var storage = new GeneratePlan(Path, projection.File, mixer.ToWrite, state.HsCircles.Count, result.Conflicts)
        {
            Reused = mixer.Reused, Removes = mixer.Removes, InputHashes = mixer.InputHashes, LastMixIndex = mixer.LastIndex,
            MergeCombinations = candidates.Select(k => combinations[k]).DistinctBy(c => c.Key).ToList(),
        };
        var saved = new HsProject(state.HsMode, Session.HsStartedEmpty, GenerateManifest.Hash(projection.File.ToBytes()),
            Session.MomentsFor(state).Select(m => new HsMoment(projection.Keys[m.Key].ObjectIndex, m.Key.Node,
                m.Moment.Time - Map.Settings.TimeOffset,
                inputs[m.Key].Select(h => ProjectStore.KeyOf(h.Lane)).ToList(),
                state.Volumes.GetValueOrDefault(m.Key, 100), state.HsCircles.ContainsKey(m.Key))
                { LayerVolumes = inputs[m.Key].Select(h => EditSession.VolumeOf(state, h)).ToList() }).ToList());
        return (new HsSavePlan(storage, saved, state) { Notes = result.Notes }, folder);
    }

    public GenerateResult WriteHsSave(HsSavePlan plan, Func<MixFile, byte[]?> render, string backupRoot, string manifestRoot)
    {
        if (!ReferenceEquals(Session.State, plan.State)) throw new IOException("The edits changed while Save was being prepared. Save again.");
        if (plan.Storage.Conflicts.Count > 0) throw new IOException("Some sounds still cannot be written; nothing was saved.");
        var written = GenerateWriter.Write(plan.Storage, render, backupRoot, Opened);
        savedHs = plan.Project;
        // The map is already saved. Losing the optional reuse manifest must not report the map as unsaved.
        try { written.Manifest.Save(manifestRoot, Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return written;
    }
}
