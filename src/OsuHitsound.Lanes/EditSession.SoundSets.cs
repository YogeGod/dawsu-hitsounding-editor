using System.Collections.Immutable;

namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    public void SaveSoundSet(SoundSet set)
    {
        set.Validate();
        if (State.SoundSets.Count >= 256 && State.SoundSets.All(s => s.Id != set.Id)) throw new IOException("A map can contain up to 256 saved sound sets.");
        if (State.SoundSets.Where(s => s.Id != set.Id).Sum(s => s.Members.Sum(m => (long)m.Audio.Length)) + set.Members.Sum(m => (long)m.Audio.Length) > 134217728)
            throw new IOException("This map's saved sound sets exceed 128 MB of audio.");
        Change("Save sound set", s => RenderSoundSets(s with { SoundSets = s.SoundSets.RemoveAll(p => p.Id == set.Id).Add(set) }));
    }

    private bool SameSetSound(Lane lane, SoundSetMember member, EditState? context = null)
    {
        if (lane == member.Lane) return true;
        var known = (context ?? State).SoundSets.SelectMany(s => s.Members).FirstOrDefault(m => m.Lane == lane);
        if (known is not null) return known.Slot == member.Slot && known.Hash == member.Hash;
        if (lane.Source == LaneSource.Skin) return lane.Sample is { } skinSample && Format.HitsoundRules.SampleName(skinSample.Set, skinSample.Sound) == member.Slot && ProjectStore.KeyOf(lane) == member.OriginalKey;
        if (lane.Sample is { } sample ? Format.HitsoundRules.SampleName(sample.Set, sample.Sound) != member.Slot : member.Slot != "normal-hitnormal") return false;
        return sampleFolder.Find(lane.Name) is { } file && GenerateManifest.Hash(sampleFolder.ReadBytes(file)) == member.Hash;
    }

    public IReadOnlyList<EditHit> ApplySoundSet(Guid setId, double time, bool replace = false, bool preserveExistingVolumes = true)
    {
        var set = State.SoundSets.Single(s => s.Id == setId);
        if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        var existingPlacement = State.SoundPlacements.FirstOrDefault(p => p.SetId == setId && Math.Abs(p.Time - time) < .5);
        if (existingPlacement is null && State.SoundPlacements.Count >= 100000) throw new InvalidOperationException("This map has reached the sound-set placement limit.");
        if (existingPlacement is not null && !replace && (State.Hits.Any(h => h.SetPlacement == existingPlacement.Id) || existingPlacement.Overrides.Values.Any(o => o.Shared)))
            return State.Hits.Where(h => h.SetPlacement == existingPlacement.Id).ToList();
        Guid id = existingPlacement?.Id ?? Guid.NewGuid();
        Change(replace ? "Replace with sound set" : "Add sound set", s =>
        {
            var placement = new SoundSetPlacement(id, setId, time);
            var hits = s.Hits;
            var layers = s.LayerVolumes;
            if (replace)
            {
                var roles = set.Members.Select(m => Format.HitsoundRules.ParseSampleName(m.Slot)!.Value.Sound).ToHashSet();
                hits = hits.RemoveAll(h => Math.Abs(h.Time - time) < .5 && roles.Contains(h.Lane.Sample?.Sound ?? "hitnormal"));
            }
            foreach (var member in set.Members)
            {
                var present = hits.FirstOrDefault(h => h.SetPlacement != id && Math.Abs(h.Time - time) < .5 && SameSetSound(h.Lane, member));
                if (present is not null && present.SetPlacement != Guid.Empty)
                { placement = placement with { Overrides = placement.Overrides.SetItem(member.Id, new() { Shared = true }) }; continue; }
                var hit = present is null ? HitAt(member.Lane, time) : present;
                int volume = present is null ? member.Volume : VolumeOf(s, present);
                if (present is not null)
                {
                    hits = hits.Remove(present); layers = layers.Remove(present);
                    if (preserveExistingVolumes || volume != member.Volume) placement = placement with { Overrides = placement.Overrides.SetItem(member.Id, new() { Volume = volume }) };
                }
                hit = hit with { Lane = member.Lane, SetPlacement = id, SetMember = member.Id };
                hits = hits.Add(hit); layers = layers.SetItem(hit, volume);
            }
            var next = s with { Hits = hits, LayerVolumes = layers, SoundPlacements = s.SoundPlacements.RemoveAll(p => p.Id == id).Add(placement) };
            return ReconcileSoundSets(s, next);
        });
        return State.Hits.Where(h => h.SetPlacement == id).ToList();
    }

    public void SetSoundSetGain(Guid placementId, int gain) => Change("Sound set volume", s => RenderSoundSets(s with
    { SoundPlacements = s.SoundPlacements.Select(p => p.Id == placementId ? p with { Gain = Math.Clamp(gain, 0, 100) } : p).ToImmutableList() }));

    public void SetSoundSetMember(Guid placementId, Guid memberId, int volume)
    {
        if (State.SoundPlacements.FirstOrDefault(p => p.Id == placementId)?.Overrides.GetValueOrDefault(memberId)?.Shared == true)
            throw new InvalidOperationException("This sound follows the first placed set. Select that set to edit it.");
        Change("Sound set member volume", s => RenderSoundSets(s with
        { SoundPlacements = s.SoundPlacements.Select(p => p.Id == placementId ? p with { Overrides = p.Overrides.SetItem(memberId, p.Overrides.GetValueOrDefault(memberId, new()) with { Volume = Math.Clamp(volume, 0, 100) }) } : p).ToImmutableList() }));
    }

    public void ResetSoundSet(Guid placementId) => Change("Reset sound set", state =>
    {
        var placement = state.SoundPlacements.FirstOrDefault(p => p.Id == placementId);
        if (placement is null) return state;
        var moved = state.Hits.Where(h => h.SetPlacement == placementId).ToDictionary(h => h, h => HitAt(h.Lane, placement.Time) with
        { Voice = h.Voice, SetPlacement = h.SetPlacement, SetMember = h.SetMember });
        return RenderSoundSets(state with { Hits = state.Hits.Select(h => moved.GetValueOrDefault(h, h)).ToImmutableList(),
            LayerVolumes = state.LayerVolumes.ToImmutableDictionary(v => moved.GetValueOrDefault(v.Key, v.Key), v => v.Value),
            SoundPlacements = state.SoundPlacements.Select(p => p.Id == placementId ? p with { Gain = 100, Overrides = p.Overrides.Where(o => o.Value.Shared).ToImmutableDictionary() } : p).ToImmutableList() });
    });

    public void DetachSoundSet(Guid placementId) => Change("Make sound set independent", s =>
    {
        var changed = s.Hits.Where(h => h.SetPlacement == placementId).ToDictionary(h => h, h => h with { SetPlacement = Guid.Empty, SetMember = Guid.Empty });
        return s with { Hits = s.Hits.Select(h => changed.GetValueOrDefault(h, h)).ToImmutableList(),
            LayerVolumes = s.LayerVolumes.ToImmutableDictionary(v => changed.GetValueOrDefault(v.Key, v.Key), v => v.Value),
            SoundPlacements = s.SoundPlacements.RemoveAll(p => p.Id == placementId) };
    });

    public IReadOnlyList<EditHit> PasteSoundSets(IReadOnlyList<(EditHit Hit, int Volume)> source, IReadOnlyList<SoundSetPlacement> placements, double offset)
    {
        var copies = placements.Where(p => State.SoundSets.Any(s => s.Id == p.SetId)).ToDictionary(p => p.Id, p => p with { Id = Guid.NewGuid(), Time = p.Time + offset, Overrides = p.Overrides.Where(o => !o.Value.Shared).ToImmutableDictionary() });
        IReadOnlyList<EditHit> placed = [];
        Change("Paste sound sets", state =>
        {
            var next = PlaceLayers(state, source.Select(p => (HitAt(p.Hit.Lane, p.Hit.Time + offset) with
                { Voice = p.Hit.Voice, SetPlacement = copies.GetValueOrDefault(p.Hit.SetPlacement)?.Id ?? Guid.Empty, SetMember = copies.ContainsKey(p.Hit.SetPlacement) ? p.Hit.SetMember : Guid.Empty },
                Volume: copies.ContainsKey(p.Hit.SetPlacement) || state.HsMode ? (int?)p.Volume : null)), [], out placed);
            return RenderSoundSets(next with { SoundPlacements = next.SoundPlacements.AddRange(copies.Values) });
        });
        return placed.Where(h => h.SetPlacement == Guid.Empty).Concat(State.Hits.Where(h => copies.Values.Any(p => p.Id == h.SetPlacement))).Distinct().ToList();
    }

    private EditState RenderSoundSets(EditState state)
    {
        var hits = state.Hits.ToBuilder(); var layers = state.LayerVolumes.ToBuilder();
        var placements = state.SoundPlacements.ToBuilder();
        for (int i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var set = state.SoundSets.FirstOrDefault(s => s.Id == placement.SetId);
            if (set is null) continue;
            var overrides = placement.Overrides.Where(o => set.Members.Any(m => m.Id == o.Key)).ToImmutableDictionary();
            var old = hits.Where(h => h.SetPlacement == placement.Id).ToList();
            foreach (var hit in old) { hits.Remove(hit); layers.Remove(hit); }
            foreach (var member in set.Members)
            {
                var local = overrides.GetValueOrDefault(member.Id, new());
                if (local.Removed || local.Shared) continue;
                var before = old.FirstOrDefault(h => h.SetMember == member.Id);
                double time = before?.Time ?? placement.Time;
                var present = local.Lane is not null ? null : hits.FirstOrDefault(h => h.SetPlacement != placement.Id && Math.Abs(h.Time - time) < .5 && SameSetSound(h.Lane, member, state));
                if (present is not null && present.SetPlacement != Guid.Empty)
                { overrides = overrides.SetItem(member.Id, local with { Shared = true }); continue; }
                if (present is not null)
                {
                    int level = layers.TryGetValue(present, out int v) ? v : VolumeOf(state, present);
                    local = local with { Volume = (int)Math.Round(level * 100.0 / Math.Max(1, placement.Gain)) };
                    overrides = overrides.SetItem(member.Id, local); hits.Remove(present); layers.Remove(present);
                }
                var hit = HitAt(local.Lane ?? member.Lane, before?.Time ?? placement.Time) with { SetPlacement = placement.Id, SetMember = member.Id, Voice = before?.Voice ?? 0 };
                hits.Add(hit); layers[hit] = Math.Clamp((int)Math.Round((local.Volume ?? member.Volume) * placement.Gain / 100.0), 0, 100);
            }
            placements[i] = placement with { Overrides = overrides };
        }
        return state with { Hits = hits.ToImmutable(), LayerVolumes = layers.ToImmutable(), SoundPlacements = placements.ToImmutable() };
    }

    private EditState ReconcileSoundSets(EditState before, EditState next)
    {
        if (before.SoundPlacements.Count == 0) return next;
        var placements = next.SoundPlacements.ToBuilder();
        bool movedShared = false;
        for (int i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            if (before.SoundPlacements.FirstOrDefault(p => p.Id == placement.Id) is not { } prior) continue;
            var owned = before.Hits.Where(h => h.SetPlacement == placement.Id).ToList();
            var current = next.Hits.Where(h => h.SetPlacement == placement.Id).ToList();
            var overrides = placement.Overrides;
            foreach (var old in owned)
            {
                var hit = current.FirstOrDefault(h => h.SetMember == old.SetMember);
                var local = overrides.GetValueOrDefault(old.SetMember, new());
                if (hit is null) local = local with { Removed = true };
                else
                {
                    if (hit.Lane != old.Lane) local = local with { Lane = hit.Lane };
                    if (VolumeOf(before, old) != VolumeOf(next, hit)) local = local with { Volume = (int)Math.Clamp(Math.Round(VolumeOf(next, hit) * 100.0 / Math.Max(1, placement.Gain)), 0, 10000) };
                }
                if (local != overrides.GetValueOrDefault(old.SetMember, new())) overrides = overrides.SetItem(old.SetMember, local);
            }
            if (owned.Count > 0 && owned.Count == current.Count && owned.All(h => current.Count(c => c.SetMember == h.SetMember) == 1))
            {
                var shifts = owned.Select(h => current.Single(c => c.SetMember == h.SetMember).Time - h.Time).Distinct().ToList();
                if (shifts.Count == 1)
                {
                    placement = placement with { Time = prior.Time + shifts[0] };
                    if (shifts[0] != 0 && overrides.Values.Any(o => o.Shared))
                    { overrides = overrides.Where(o => !o.Value.Shared).ToImmutableDictionary(); movedShared = true; }
                }
            }
            placements[i] = placement with { Overrides = overrides };
        }
        var result = next with { SoundPlacements = placements.ToImmutable() };
        return movedShared ? RenderSoundSets(result) : result;
    }

    public SoundSetProject CaptureSoundSets(string fileHash)
    {
        var positions = new Dictionary<(Lane, double), int>();
        var bindings = new List<SoundSetBinding>();
        foreach (var hit in State.Hits)
        {
            var key = (hit.Lane, hit.Time); int ordinal = positions.GetValueOrDefault(key); positions[key] = ordinal + 1;
            if (hit.SetPlacement != Guid.Empty) bindings.Add(new(ProjectStore.KeyOf(hit.Lane), hit.Time, ordinal, hit.SetPlacement, hit.SetMember));
        }
        return new(fileHash, State.SoundSets.ToList(), State.SoundPlacements.ToList(), bindings);
    }

    public IReadOnlyList<(EditHit Hit, string Error)> SoundSetInputProblems(MapFolder folder, EditState? input = null)
    {
        var state = input ?? State;
        var definitions = state.SoundSets.ToDictionary(s => s.Id);
        var placements = state.SoundPlacements.ToDictionary(p => p.Id);
        var checkedFiles = new Dictionary<Lane, string?>();
        var errors = new List<(EditHit, string)>();
        foreach (var hit in state.Hits.Where(h => h.SetPlacement != Guid.Empty))
        {
            if (!placements.TryGetValue(hit.SetPlacement, out var placement) || !definitions.TryGetValue(placement.SetId, out var set)) continue;
            var member = set.Members.FirstOrDefault(m => m.Id == hit.SetMember);
            if (member is null || hit.Lane != member.Lane) continue;
            if (!checkedFiles.TryGetValue(hit.Lane, out var error))
            {
                try { error = folder.Find(hit.Lane.Name) is { } file && GenerateManifest.Hash(folder.ReadBytes(file)) == member.Hash ? null : $"Saved set audio {hit.Lane.Name} is missing or changed. Edit the set to restore its saved audio or choose a replacement."; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = $"Could not read saved set audio {hit.Lane.Name}: {ex.Message}"; }
                checkedFiles[hit.Lane] = error;
            }
            if (error is not null) errors.Add((hit, error));
        }
        return errors;
    }

    public void RestoreSoundSets(SoundSetProject? project, string fileHash)
    {
        if (project is null) return;
        if (project.Sets is null || project.Sets.Count > 256 || project.Placements is null || project.Bindings is null || project.Placements.Count > 100000 || project.Bindings.Count > 200000)
            throw new FormatException("Invalid sound-set project.");
        foreach (var set in project.Sets) { if (set is null) throw new FormatException("Invalid saved set."); set.Validate(); }
        if (project.Sets.Sum(s => s.Members.Sum(m => (long)m.Audio.Length)) > 134217728) throw new FormatException("Saved sound sets exceed the project audio limit.");
        var sets = project.Sets.DistinctBy(s => s.Id).ToImmutableList();
        State = State with { SoundSets = sets };
        if (project.FileHash != fileHash) return;
        var valid = project.Placements.Where(p => p is not null && p.Id != Guid.Empty && sets.Any(s => s.Id == p.SetId) && double.IsFinite(p.Time) && p.Time >= 0 && p.Gain is >= 0 and <= 100 && p.Overrides is not null &&
            p.Overrides.Count <= 128 && p.Overrides.All(o => o.Value is not null && (o.Value.Volume is null or >= 0 and <= 10000) && (o.Value.Lane is null || o.Value.Lane.Name is { Length: > 0 and <= 1024 } && Enum.IsDefined(o.Value.Lane.Source) && MapFolder.IsInside(o.Value.Lane.Name)))).DistinctBy(p => p.Id).ToImmutableList();
        var changes = new Dictionary<EditHit, EditHit>();
        var candidates = State.Hits.ToLookup(h => (ProjectStore.KeyOf(h.Lane), h.Time));
        var assigned = new HashSet<(Guid, Guid)>();
        foreach (var binding in project.Bindings.Where(b => b is not null && double.IsFinite(b.Time) && b.Ordinal >= 0))
        {
            if (!valid.Any(p => p.Id == binding.Placement && sets.Single(s => s.Id == p.SetId).Members.Any(m => m.Id == binding.Member))) continue;
            var hit = candidates[(binding.Lane, binding.Time)].Skip(binding.Ordinal).FirstOrDefault();
            if (hit is not null && !changes.ContainsKey(hit) && assigned.Add((binding.Placement, binding.Member))) changes[hit] = hit with { SetPlacement = binding.Placement, SetMember = binding.Member };
        }
        var retained = valid.Where(p => changes.Values.Any(h => h.SetPlacement == p.Id) ||
            sets.Single(s => s.Id == p.SetId).Members.All(m => p.Overrides.GetValueOrDefault(m.Id)?.Shared == true) &&
            changes.Values.Any(h => Math.Abs(h.Time - p.Time) < .5)).Select(p =>
        {
            var local = p.Overrides;
            foreach (var member in sets.Single(s => s.Id == p.SetId).Members)
                if (!changes.Values.Any(h => h.SetPlacement == p.Id && h.SetMember == member.Id) && local.GetValueOrDefault(member.Id)?.Shared != true)
                    local = local.SetItem(member.Id, local.GetValueOrDefault(member.Id, new()) with { Removed = true });
            return p with { Overrides = local };
        }).ToImmutableList();
        State = State with { SoundPlacements = retained, Hits = State.Hits.Select(h => changes.GetValueOrDefault(h, h)).ToImmutableList(),
            LayerVolumes = State.LayerVolumes.ToImmutableDictionary(p => changes.GetValueOrDefault(p.Key, p.Key), p => p.Value) };
        announced = State;
    }
}
