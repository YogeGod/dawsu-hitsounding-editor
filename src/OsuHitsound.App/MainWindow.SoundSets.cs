using System.Collections.Immutable;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private readonly HashSet<double> soundSetStroke = [];
    private bool soundSetBusy;
    private void SetupSoundSets()
    {
        Lanes.Edits.ReadClipboardAudio = lane =>
        {
            try { return new LaneAudio(doc?.Folder ?? new MapFolder([]), skin.Value).TryRead(lane); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        };
        Lanes.Edits.ImportSetClipboard = ImportSoundSetClipboard;
        Inspector.SoundSets.ChoiceChanged += () => { Lanes.DrawSoundSetActive = Inspector.SoundSets.SelectedSet is not null; UpdateStatus(); };
        Inspector.SoundSets.Command += async command => await SoundSetCommand(command);
        Lanes.DrawSoundSetRequested = time =>
        {
            if (session is null || Inspector.SoundSets.SelectedSet is not { } id) return false;
            if (Inspector.SoundSets.Replace) { soundSetStroke.Add(time); return true; }
            try
            {
                var before = session.State;
                var hits = session.ApplySoundSet(id, time);
                Lanes.Edits.Select(hits);
                if (Lanes.AuditionOnPlacement && !ReferenceEquals(before, session.State)) PreviewSoundSet(id);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
            { flash = $"Could not place sound set: {ex.Message}"; UpdateStatus(); }
            return true;
        };
        Lanes.SoundSetStrokeFinished += async () =>
        {
            var times = soundSetStroke.Order().ToList(); soundSetStroke.Clear();
            if (times.Count > 0) await ApplySelectedSoundSet(times, true);
        };
    }

    private void PreviewSoundSet(Guid id)
    {
        try
        {
            if (audio is null) throw new IOException("Audio output is unavailable. Check Preferences.");
            var set = session!.State.SoundSets.Single(s => s.Id == id);
            audio.PreviewMix(SampleMixer.Mix(set.Members.Select(m =>
                (SampleMixer.Decode(m.Audio) ?? throw new IOException($"Could not decode {m.Lane.Name}."), m.Volume / 100f)).ToList()));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { flash = ex.Message; UpdateStatus(); }
    }

    private async Task ApplySelectedSoundSet(IReadOnlyList<double> times, bool replace)
    {
        if (session is null || soundSetBusy || Inspector.SoundSets.SelectedSet is not { } id || !session.State.SoundSets.Any(s => s.Id == id)) return;
        soundSetBusy = true;
        var editing = session;
        var state = editing.State;
        bool enabled = Root.IsEnabled; Root.IsEnabled = false;
        try
        {
            if (replace)
            {
                var set = state.SoundSets.Single(s => s.Id == id);
                var roles = set.Members.Select(m => OsuHitsound.Format.HitsoundRules.ParseSampleName(m.Slot)!.Value.Sound).ToHashSet();
                var removed = state.Hits.Where(h => times.Any(t => Math.Abs(t - h.Time) < .5) && roles.Contains(h.Lane.Sample?.Sound ?? "hitnormal")).ToList();
                string detail = string.Join("\n", removed.Take(20).Select(h => $"{h.Time:0.###} ms: {h.Lane.Name}"));
                if (!await Dialogs.Confirm(this, "Replace with sound set", $"Replace {removed.Count} sound(s) at {times.Count} position(s) with {set.Name}? Other sound types stay.\n\n{detail}" + (removed.Count > 20 ? "\n…" : ""), "Replace")) return;
            }
            if (editing != session || !ReferenceEquals(state, editing.State)) { flash = "The edits changed. Apply the set again."; return; }
            var placed = new List<EditHit>();
            editing.BeginGesture(replace ? "Replace with sound set" : "Apply sound set");
            try { foreach (double time in times) placed.AddRange(editing.ApplySoundSet(id, time, replace)); }
            catch { editing.CancelGesture(); throw; }
            finally { editing.EndGesture(); }
            Lanes.Edits.Select(placed);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { flash = $"Could not apply sound set: {ex.Message}"; }
        finally { Root.IsEnabled = enabled; soundSetBusy = false; UpdateStatus(); }
    }

    private SoundSet InstallSoundSet(SoundSet set)
    {
        foreach (var member in set.Members)
            if (SampleMixer.Decode(member.Audio) is not { Length: > 0 }) throw new IOException($"Could not decode {member.Lane.Name}.");
        var installed = SoundSetFiles.Install(Path.GetDirectoryName(doc!.Path)!, set, session!.State.SoundSets.FirstOrDefault(s => s.Id == set.Id));
        doc.RescanFolder();
        foreach (var member in installed.Members)
        {
            if (!model!.LaneRows.Any(r => r.Lane == member.Lane)) model.AddLane(member.Lane);
            audio?.LoadLane(member.Lane, member.Audio);
        }
        return installed;
    }

    private async Task SoundSetCommand(string command)
    {
        if (doc is null || session is null || model is null || soundSetBusy) return;
        if (command == "apply")
        { await ApplySelectedSoundSet(Lanes.Selection.Count > 0 ? Lanes.Selection.Select(h => h.Time).Distinct().ToList() : [Lanes.Edits.SnapTime(Lanes.Playhead)], Inspector.SoundSets.Replace); return; }
        if (command == "review") { await ReviewOutput(); return; }
        var panel = Inspector.SoundSets;
        var selected = panel.SelectedSet is { } sid ? session.State.SoundSets.FirstOrDefault(s => s.Id == sid) : null;
        try
        {
            if (command == "preview") { if (selected is not null) PreviewSoundSet(selected.Id); return; }
            if (command == "select" && panel.Placement is { } select) { Lanes.Edits.Select(session.State.Hits.Where(h => h.SetPlacement == select)); return; }
            if (command == "gain" && panel.Placement is { } gain) { session.SetSoundSetGain(gain, panel.Gain); return; }
            if (command == "member" && panel.Placement is { } placement && panel.Member is { } member) { session.SetSoundSetMember(placement, member, panel.MemberVolume); return; }
            if (command == "reset" && panel.Placement is { } reset) { session.ResetSoundSet(reset); return; }
            if (command == "detach" && panel.Placement is { } detach) { session.DetachSoundSet(detach); return; }
            soundSetBusy = true;
            var current = doc;
            SoundSet? prepared = null;
            double? capturedTime = null;
            if (command == "capture")
            {
                var hits = Lanes.Selection.ToList();
                if (hits.Count == 0 || hits.Select(h => h.Time).Distinct().Count() != 1) throw new IOException("Select sounds at one timestamp to save a set.");
                if (await Dialogs.Prompt(this, "Save sound set", "Name this combination of sounds:", "") is not { Length: > 0 } name) return;
                prepared = SoundSetFiles.Capture(name, hits.Select(h => (h.Lane, session.VolumeOf(h))), doc.Folder, skin.Value);
                capturedTime = hits[0].Time;
            }
            else if (command is "new" or "edit")
            {
                if (command == "edit" && selected is null) throw new IOException("Choose a saved set to edit.");
                var original = command == "edit" ? selected : null;
                var lanes = model.LaneRows.Select(r => r.Lane).Where(l => l.Source != LaneSource.StoryboardFile && (l.Sample is null || l.Sample?.Sound is "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap")).ToList();
                if (await Dialogs.EditSoundSet(this, lanes, original) is not { } choice) return;
                var members = choice.Members.Select(m => original?.Members.FirstOrDefault(o => o.Id == m.Id && o.Lane == m.Lane) is { } old ? old with { Volume = m.Volume }
                    : SoundSetFiles.Capture(choice.Name, [(m.Lane, m.Volume)], doc.Folder, skin.Value).Members[0] with { Id = m.Id }).ToImmutableList();
                prepared = new(original?.Id ?? Guid.NewGuid(), choice.Name, members) { LibraryId = original?.LibraryId };
            }
            else if (command == "librarySave")
            {
                if (selected is null) throw new IOException("Choose a saved set first.");
                if (!Directory.Exists(prefs.HitsoundFolder)) throw new IOException("Choose your hitsound folder in Preferences first.");
                Guid libraryId = selected.LibraryId ?? selected.Id;
                string directory = Path.Combine(prefs.HitsoundFolder!, "Sound sets");
                string? existing = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*" + SoundSetFiles.Extension)
                    .FirstOrDefault(p => Path.GetFileName(p).EndsWith(libraryId + SoundSetFiles.Extension, StringComparison.OrdinalIgnoreCase)) : null;
                string label = SafeFile.Name(selected.Name); if (label.Length > 80) label = label[..80];
                string libraryPath = existing ?? Path.Combine(directory, label + "-" + libraryId + SoundSetFiles.Extension);
                SoundSetFiles.Save(libraryPath, selected with { Id = libraryId, LibraryId = null });
                session.SaveSoundSet(selected with { LibraryId = libraryId });
                flash = $"Saved {Path.GetFileName(libraryPath)} with its audio to the personal library. Other maps are unchanged.";
            }
            else if (command is "import" or "update")
            {
                if (command == "update" && selected?.LibraryId is null) throw new IOException("Choose a set imported from or saved to the library first.");
                IStorageFolder? start = Directory.Exists(prefs.HitsoundFolder) ? await StorageProvider.TryGetFolderFromPathAsync(prefs.HitsoundFolder!) : null;
                var files = await StorageProvider.OpenFilePickerAsync(new() { Title = command == "update" ? "Update from library" : "Import sound set", SuggestedStartLocation = start,
                    FileTypeFilter = [new("Sound sets") { Patterns = ["*" + SoundSetFiles.Extension] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                if (ReferenceEquals(doc, current)) ImportSoundSetFile(path, command == "update");
            }
            if (prepared is not null && ReferenceEquals(doc, current))
            {
                var installed = InstallSoundSet(prepared);
                session.BeginGesture(capturedTime is null ? "Save sound set" : "Create linked sound set");
                try
                {
                    session.SaveSoundSet(installed);
                    if (capturedTime is { } time) Lanes.Edits.Select(session.ApplySoundSet(installed.Id, time, preserveExistingVolumes: false));
                }
                catch { session.CancelGesture(); throw; }
                finally { session.EndGesture(); }
                panel.Refresh(session, Lanes.Selection); panel.Choose(installed.Id);
                Lanes.DrawSoundSetActive = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException)
        { flash = $"Sound set: {ex.Message}"; }
        finally { soundSetBusy = false; UpdateStatus(); }
    }

    private void ImportSoundSetFile(string path, bool update)
    {
        var panel = Inspector.SoundSets;
        var selected = panel.SelectedSet is { } id ? session!.State.SoundSets.FirstOrDefault(s => s.Id == id) : null;
        var source = SoundSetFiles.Load(path);
        if (update && (selected?.LibraryId is null || selected.LibraryId != source.Id)) throw new IOException("This file belongs to a different library set.");
        var installed = InstallSoundSet(source with { Id = update ? selected!.Id : Guid.NewGuid(), LibraryId = source.Id });
        session!.SaveSoundSet(installed); panel.Refresh(session, Lanes.Selection); panel.Choose(installed.Id); Lanes.DrawSoundSetActive = true;
    }

    private IReadOnlyList<EditHit> ImportSoundSetClipboard(IReadOnlyList<SoundSet> definitions, IReadOnlyList<SoundSetPlacement> placements,
        IReadOnlyList<(EditHit Hit, int Volume)> hits, IReadOnlyDictionary<Lane, byte[]> clips, double offset)
    {
        if (doc is null || session is null) return [];
        try
        {
            var imported = definitions.ToDictionary(s => s.Id, s => InstallSoundSet(s with { Id = Guid.NewGuid() }));
            var laneMap = new Dictionary<Lane, Lane>();
            foreach (var lane in placements.SelectMany(p => p.Overrides.Values).Select(o => o.Lane).OfType<Lane>()
                .Concat(hits.Where(h => h.Hit.SetPlacement == Guid.Empty && h.Hit.Lane.Source == LaneSource.BeatmapFile).Select(h => h.Hit.Lane)).Distinct())
            {
                if (!clips.TryGetValue(lane, out var bytes)) throw new IOException($"The copied audio for {lane.Name} is unavailable. Copy it again.");
                string slot = lane.Sample is { } sample ? OsuHitsound.Format.HitsoundRules.SampleName(sample.Set, sample.Sound) : "normal-hitnormal";
                var extra = InstallSoundSet(new(Guid.NewGuid(), "Copied sound", [new(Guid.NewGuid(), ProjectStore.KeyOf(lane), lane, slot, 100, bytes)]));
                laneMap[lane] = extra.Members[0].Lane;
            }
            var mappedPlacements = placements.Select(p => p with { SetId = imported[p.SetId].Id,
                Overrides = p.Overrides.ToImmutableDictionary(o => o.Key, o => o.Value.Lane is { } lane ? o.Value with { Lane = laneMap[lane] } : o.Value) }).ToList();
            var mappedHits = hits.Select(h =>
            {
                var placement = placements.FirstOrDefault(p => p.Id == h.Hit.SetPlacement);
                var member = placement is null ? null : imported[placement.SetId].Members.FirstOrDefault(m => m.Id == h.Hit.SetMember);
                Lane lane = placement?.Overrides.GetValueOrDefault(h.Hit.SetMember)?.Lane is { } local ? laneMap[local] : member?.Lane ?? laneMap.GetValueOrDefault(h.Hit.Lane, h.Hit.Lane);
                return (h.Hit with { Lane = lane }, h.Volume);
            }).ToList();
            IReadOnlyList<EditHit> result;
            session.BeginGesture("Paste linked sound sets");
            try { foreach (var set in imported.Values) session.SaveSoundSet(set); result = session.PasteSoundSets(mappedHits, mappedPlacements, offset); }
            catch { session.CancelGesture(); throw; }
            finally { session.EndGesture(); }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException)
        { flash = $"Could not paste linked sound sets: {ex.Message}"; UpdateStatus(); return []; }
    }
}
