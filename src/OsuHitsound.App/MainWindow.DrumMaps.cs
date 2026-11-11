using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    internal string DrumMapStorageRoot { get; set; } = DrumMapStore.DefaultRoot();
    private ArrangeModel? drumLoadedModel;
    private DrumMapCollection? drumSaved;
    private string drumStoreHash = "missing";
    private bool drumReadFailed, syncingDrumToggle, drumSetupBusy;

    private void SetupDrumMaps()
    {
        DrumMapToggle.IsCheckedChanged += (_, _) =>
        {
            if (syncingDrumToggle || model is null || drumReadFailed) return;
            model.SetDrumMapMode(DrumMapToggle.IsChecked == true);
            Lanes.ScrollY = 0; Lanes.Focus(); SyncScrollBars(); ScheduleProjectSave();
        };
        DrumMapSetupItem.Click += async (_, _) => await ShowDrumMapSetup();
        Lanes.DrumEntrySetupRequested += async id => await EditActiveDrumMap(id);
    }

    private void RefreshDrumMapToggle()
    {
        syncingDrumToggle = true;
        try
        {
            DrumMapToggle.IsEnabled = model is not null && !drumReadFailed;
            DrumMapToggle.IsChecked = model?.DrumMapMode == true;
            ToolTip.SetTip(DrumMapToggle, model?.DrumMapMode == true
                ? $"Drum map: {model.ActiveDrumMap?.Name}. Click for Sample view; right-click for Drum Map Setup."
                : "Switch to flat Drum-map view. Right-click for Drum Map Setup.");
        }
        finally { syncingDrumToggle = false; }
    }

    private void RestoreDrumMaps()
    {
        if (model is null || folder is null || mapPath is null || ReferenceEquals(model, drumLoadedModel)) return;
        drumLoadedModel = model; drumReadFailed = false;
        try
        {
            var loaded = DrumMapStore.Load(DrumMapStorageRoot, Path.GetDirectoryName(mapPath)!);
            foreach (var key in loaded.Maps.Presets.SelectMany(p => p.Rows).Select(r => r.LaneKey).OfType<string>().Distinct())
                if (ProjectStore.LaneOf(key) is { } lane && !model.LaneRows.Any(r => r.Lane == lane) &&
                    (lane.Source == LaneSource.Skin || folder.Find(lane.Name) is not null)) model.AddLane(lane);
            var maps = loaded.Hash == "missing" ? DrumMapDefaults.Create() : loaded.Maps;
            model.RestoreDrumMaps(maps); drumStoreHash = loaded.Hash; drumSaved = maps;
            var attached = model;
            attached.DrumMapsChanged += () =>
            {
                if (!ReferenceEquals(model, attached)) return;
                RefreshDrumMapToggle(); Lanes.ScrollY = Lanes.ScrollY; SyncScrollBars(); ScheduleProjectSave();
                RefreshDrumVstRouting();
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        { drumReadFailed = true; flash = $"Drum maps were not loaded or overwritten: {ex.Message}"; }
        RefreshDrumMapToggle();
    }

    private void SaveDrumMaps()
    {
        if (model is null || mapPath is null || !ReferenceEquals(model, drumLoadedModel) || ReferenceEquals(drumSaved, model.DrumMaps)) return;
        if (drumReadFailed) throw new IOException("The existing drum maps could not be read and were not overwritten.");
        drumStoreHash = DrumMapStore.Save(DrumMapStorageRoot, Path.GetDirectoryName(mapPath)!, model.DrumMaps, drumStoreHash);
        drumSaved = model.DrumMaps;
    }

    private IReadOnlyList<(Lane Lane, Lane Slot)> DrumLaneChoices() => model?.LaneRows
        .Where(r => r.Lane.Source != LaneSource.StoryboardFile && !r.Lane.Missing &&
            (r.Lane.Sample is null || r.Lane.Sample?.Sound is "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap"))
        .Select(r => (r.Lane, model.SlotOf(r.Lane))).Distinct().ToList() ?? [];

    private void ShowDrumRowMenu(LaneRow row)
    {
        if (model is null) return;
        MenuItem Item(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); return item; }
        var menu = new ContextMenu();
        menu.Items.Add(Item("Rename instrument…", async () =>
        {
            var current = model;
            if (current?.ActiveDrumMap is not { } preset) return;
            string initial = row.InstrumentName ?? current.NameOf(row.Lane);
            if (await Dialogs.Prompt(this, "Instrument name", "Display name only; hits and sample files stay unchanged.", initial) is not { Length: > 0 } name || current != model) return;
            if (row.DrumEntryId is null && preset.Rows.Count >= 4096) { flash = "The drum map has reached its row limit."; UpdateStatus(); return; }
            var entry = row.DrumEntryId is { } id ? preset.Rows.Single(r => r.Id == id) :
                new DrumMapEntry(Guid.NewGuid(), initial, ProjectStore.KeyOf(row.Lane), ProjectStore.KeyOf(current.SlotOf(row.Lane)));
            if (name.Trim().Length is < 1 or > 128) return;
            var entries = preset.Rows.Any(r => r.Id == entry.Id) ? preset.Rows.Select(r => r.Id == entry.Id ? r with { Name = name.Trim() } : r).ToImmutableList() : preset.Rows.Add(entry with { Name = name.Trim() });
            current.SetDrumPreset(preset with { Rows = entries }, "Rename instrument");
        }));
        menu.Items.Add(Item("Edit instrument assignments…", async () => await EditActiveDrumMap(row.DrumEntryId)));
        menu.Items.Add(Item("New alias of this sample…", async () =>
        {
            var current = model;
            if (current?.ActiveDrumMap is not { } preset) return;
            if (await Dialogs.Prompt(this, "New instrument alias", "Both rows show the same hits and play the sample once.", "Instrument") is not { Length: > 0 } name || current != model) return;
            if (name.Trim().Length is < 1 or > 128 || preset.Rows.Count >= 4096) { flash = "Use an instrument name of 1–128 characters; the map allows up to 4096 rows."; UpdateStatus(); return; }
            current.SetDrumPreset(preset with { Rows = preset.Rows.Add(new(Guid.NewGuid(), name.Trim(), ProjectStore.KeyOf(row.Lane), ProjectStore.KeyOf(current.SlotOf(row.Lane)))) }, "Add instrument alias");
        }));
        void Shift(int delta)
        {
            if (model is not { } current) return;
            int index = current.Rows.ToList().IndexOf(row), to = index + delta;
            if (index >= 0 && to >= 0 && to < current.Rows.Count) current.MoveDrumRow(row, current.Rows[to], delta > 0);
        }
        menu.Items.Add(Item("Move up", () => Shift(-1))); menu.Items.Add(Item("Move down", () => Shift(1)));
        if (row.DrumEntryId is { } entryId) menu.Items.Add(Item("Remove instrument row (keep hits)", () =>
        {
            if (model?.ActiveDrumMap is { } preset) model.SetDrumPreset(preset with { Rows = preset.Rows.RemoveAll(r => r.Id == entryId) }, "Remove instrument row");
        }));
        menu.Open(Lanes);
    }

    private async Task EditActiveDrumMap(Guid? entryId)
    {
        if (drumSetupBusy || model is not { } current || current.ActiveDrumMap is not { } preset || drumReadFailed) return;
        drumSetupBusy = true; bool enabled = Root.IsEnabled; Root.IsEnabled = false;
        try
        {
            var edited = await Dialogs.EditDrumMap(this, preset, DrumLaneChoices(), focusEntry: entryId);
            if (edited is not null && current == model) await ApplyDrumMapEdit(edited, preset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException)
        { flash = $"Drum-map assignment: {ex.Message}"; }
        finally { Root.IsEnabled = enabled; drumSetupBusy = false; SaveProject(); UpdateStatus(); }
    }

    private async Task ShowDrumMapSetup()
    {
        if (drumSetupBusy || model is null || doc is null || drumReadFailed) return;
        drumSetupBusy = true;
        var current = model; var document = doc;
        bool enabled = Root.IsEnabled; Root.IsEnabled = false;
        try
        {
            while (model == current && doc == document)
            {
                var choice = await Dialogs.DrumMapSetup(this, current.DrumMaps);
                if (choice is null || current != model) break;
                var preset = current.DrumMaps.Presets.FirstOrDefault(p => p.Id == choice.Id);
                if (choice.Command == "use" && preset is not null)
                { current.ChangeDrumMaps(current.DrumMaps with { Active = preset.Id, Enabled = true }, "Choose drum map"); Lanes.ScrollY = 0; break; }
                if (choice.Command == "load") { await LoadDrumMapPreset(); continue; }
                if (choice.Command == "save" && preset is not null) { await SaveDrumMapPreset(preset); continue; }
                if (choice.Command == "remove" && preset is not null)
                {
                    if (!await Dialogs.Confirm(this, "Remove drum map", $"Remove {preset.Name}? All hits and sample files stay unchanged.", "Remove")) continue;
                    var remaining = current.DrumMaps.Presets.Remove(preset);
                    current.ChangeDrumMaps(current.DrumMaps with { Presets = remaining, Active = current.DrumMaps.Active == preset.Id ? remaining.FirstOrDefault()?.Id : current.DrumMaps.Active, Enabled = remaining.Count > 0 && current.DrumMapMode }, "Remove drum map"); continue;
                }
                if (choice.Command == "rename" && preset is not null)
                {
                    if (await Dialogs.Prompt(this, "Rename drum map", "Map name:", preset.Name) is { Length: > 0 } name && name.Trim().Length is > 0 and <= 128)
                        current.SetDrumPreset(preset with { Name = name.Trim() }, "Rename drum map");
                    continue;
                }
                if (choice.Command is "new" or "copy" or "edit")
                {
                    var original = choice.Command == "new" ? DrumMapPreset.Empty("New drum map") : preset;
                    if (original is null) continue;
                    if (choice.Command == "copy") original = original with { Id = Guid.NewGuid(), Name = original.Name + " copy", Rows = original.Rows.Select(r => r with { Id = Guid.NewGuid() }).ToImmutableList() };
                    var edited = await Dialogs.EditDrumMap(this, original, DrumLaneChoices());
                    if (edited is not null && model == current) await ApplyDrumMapEdit(edited, choice.Command == "edit" ? preset : null);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException)
        { flash = $"Drum Map Setup: {ex.Message}"; }
        finally { Root.IsEnabled = enabled; drumSetupBusy = false; RefreshDrumMapToggle(); SyncScrollBars(); SaveProject(); UpdateStatus(); }
    }

    private async Task ApplyDrumMapEdit(DrumMapPreset edited, DrumMapPreset? original)
    {
        if (model is not { } current || session is not { } editing) return;
        var state = editing.State;
        var changes = original?.Rows.Select(old => (Old: ProjectStore.LaneOf(old.LaneKey ?? ""),
            New: ProjectStore.LaneOf(edited.Rows.FirstOrDefault(r => r.Id == old.Id)?.LaneKey ?? "")))
            .Where(p => p.Old is not null && p.New is not null && p.Old != p.New).ToList() ?? [];
        var grouped = changes.GroupBy(c => c.Old!).ToList();
        bool canMove = grouped.All(g => g.Select(c => c.New).Distinct().Count() == 1);
        var mapping = grouped.ToDictionary(g => g.Key, g => g.First().New!);
        int hits = editing.State.Hits.Count(h => mapping.ContainsKey(h.Lane));
        (bool Move, DrumMapOverlap? Overlap)? choice = (false, null);
        if (hits > 0) choice = await Dialogs.DrumMapReassignment(this, hits, editing.DrumMapOverlaps(mapping), canMove);
        if (choice is null || model != current || !ReferenceEquals(state, editing.State)) return;
        var installed = InstallDrumMapDefaults(edited);
        if (choice.Value.Move)
        {
            var installedRows = installed.Rows.ToDictionary(r => r.Id);
            var destinations = edited.Rows.Where(r => r.LaneKey is not null).DistinctBy(r => r.LaneKey)
                .ToDictionary(r => r.LaneKey!, r => ProjectStore.LaneOf(installedRows[r.Id].LaneKey!)!);
            mapping = mapping.ToDictionary(p => p.Key, p => destinations[ProjectStore.KeyOf(p.Value)]);
        }
        current.SetDrumPreset(installed, "Reassign drum map", choice.Value.Move ? mapping : null, choice.Value.Overlap);
    }

    private async Task SaveDrumMapPreset(DrumMapPreset preset)
    {
        if (doc is null || model is null) return;
        var files = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save drum map with samples", SuggestedFileName = SafeFile.Name(preset.Name) + DrumMapStore.Extension,
            DefaultExtension = DrumMapStore.Extension.TrimStart('.'), FileTypeChoices = [new("Drum map") { Patterns = ["*" + DrumMapStore.Extension] }], ShowOverwritePrompt = true });
        if (files?.TryGetLocalPath() is not { } path) return;
        DrumMapStore.Export(path, CaptureDrumMapPreset(preset));
        flash = "Saved drum map with its chosen samples.";
    }

    private DrumMapPreset CaptureDrumMapPreset(DrumMapPreset preset)
    {
        if (doc is null) throw new InvalidOperationException("Open a map first.");
        var laneAudio = new LaneAudio(doc.Folder, skin.Value);
        var samples = preset.Rows.Where(r => r.LaneKey is not null).DistinctBy(r => r.LaneKey).Select(r =>
        {
            var lane = ProjectStore.LaneOf(r.LaneKey!)!;
            byte[] bytes = laneAudio.Read(lane);
            return new DrumMapAudio(r.LaneKey!, r.SlotKey ?? r.LaneKey!, bytes.ToArray());
        }).ToImmutableList();
        var portable = preset with { Audio = samples }; portable.Validate(); return portable;
    }

    private async Task LoadDrumMapPreset()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Load drum map", FileTypeFilter = [new("Drum map") { Patterns = ["*" + DrumMapStore.Extension] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await ImportDrumMapPreset(DrumMapStore.Import(path));
    }

    private async Task ImportDrumMapPreset(DrumMapPreset preset)
    {
        if (model is not { } current || doc is not { } document) return;
        preset.Validate();
        bool? useSamples = await Dialogs.DrumMapSamples(this, preset.Audio.Count > 0);
        if (useSamples is null || current != model) return;
        var available = DrumLaneChoices().ToList();
        var labels = new Dictionary<Lane, string>();
        var assignments = new Dictionary<string, Lane>();
        var prepared = new Dictionary<Lane, SoundSetMember>();
        if (useSamples == true)
        {
            var occupied = available.Select(l => l.Lane.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int index = int.MaxValue;
            foreach (var sampleAudio in preset.Audio.Where(a => preset.Rows.Any(r => r.LaneKey == a.LaneKey)))
            {
                if (SampleMixer.Decode(sampleAudio.Bytes) is null) throw new IOException("A preset sample could not be decoded.");
                var original = ProjectStore.LaneOf(sampleAudio.LaneKey)!;
                var slot = ProjectStore.LaneOf(sampleAudio.SlotKey)!;
                var sample = slot.Sample;
                string role = sample is { } sound ? HitsoundRules.SampleName(sound.Set, sound.Sound) : "normal-hitnormal";
                string extension = original.Source == LaneSource.Skin ? ".wav" : Path.GetExtension(original.Name).ToLowerInvariant();
                string name; do { name = role + index-- + extension; } while (occupied.Contains(name) || document.Folder.Find(name) is not null);
                var provisional = new Lane(LaneSource.BeatmapFile, name);
                prepared[provisional] = new(Guid.NewGuid(), sampleAudio.LaneKey, provisional, role, 100, sampleAudio.Bytes);
                assignments[sampleAudio.LaneKey] = provisional;
                available.Insert(0, (provisional, slot)); labels[provisional] = slot.Name + " (preset sample)";
            }
        }
        else
        {
            foreach (var row in preset.Rows.Where(r => r.LaneKey is not null))
            {
                var exact = available.FirstOrDefault(l => ProjectStore.KeyOf(l.Lane) == row.LaneKey);
                if (exact.Lane is not null) { assignments[row.LaneKey!] = exact.Lane; continue; }
                var matches = available.Where(l => ProjectStore.KeyOf(l.Slot) == row.SlotKey).ToList();
                if (matches.Count == 1) assignments[row.LaneKey!] = matches[0].Lane;
            }
        }
        foreach (var row in preset.Rows.Where(r => r.LaneKey is not null && !assignments.ContainsKey(r.LaneKey)))
        {
            var slot = ProjectStore.LaneOf(row.SlotKey ?? row.LaneKey!);
            if (slot?.Sample is not { Sound: "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap" }) continue;
            var key = ProjectStore.LaneOf(row.LaneKey!)!;
            assignments[row.LaneKey!] = key;
            if (available.All(l => l.Lane != key)) { available.Add((key, slot)); labels[key] = slot.Name + " (default sound)"; }
        }
        var imported = preset with { Id = Guid.NewGuid(), Audio = [], Rows = preset.Rows.Select(r => r with
            { Id = Guid.NewGuid(), LaneKey = r.LaneKey is { } key && assignments.TryGetValue(key, out var lane) ? ProjectStore.KeyOf(lane) : null }).ToImmutableList() };
        // No snapshot files or sample lanes are created until the user accepts the assignment review.
        var reviewed = await Dialogs.EditDrumMap(this, imported, available, labels);
        if (reviewed is null || current != model) return;
        var required = reviewed.Rows.Select(r => r.LaneKey is { } key ? ProjectStore.LaneOf(key) : null)
            .OfType<Lane>().Where(prepared.ContainsKey).Distinct().ToList();
        var installedLanes = new Dictionary<Lane, Lane>();
        foreach (var chunk in required.Chunk(128))
        {
            var members = chunk.Select(l => prepared[l]).ToImmutableList();
            var installed = SoundSetFiles.Install(Path.GetDirectoryName(document.Path)!, new(Guid.NewGuid(), preset.Name, members));
            foreach (var member in installed.Members)
            {
                var provisional = chunk.Single(l => prepared[l].Id == member.Id);
                installedLanes[provisional] = member.Lane; current.AddLane(member.Lane, available.Single(l => l.Lane == provisional).Slot); audio?.LoadLane(member.Lane, member.Audio);
            }
        }
        document.RescanFolder();
        reviewed = reviewed with { Rows = reviewed.Rows.Select(r => r.LaneKey is { } key && ProjectStore.LaneOf(key) is { } lane && installedLanes.TryGetValue(lane, out var installed)
            ? r with { LaneKey = ProjectStore.KeyOf(installed) } : r).ToImmutableList() };
        current.SetDrumPreset(InstallDrumMapDefaults(reviewed), "Load drum map");
    }
    // Called only after assignment/migration review was accepted. Unassigned instruments stay unassigned.
    private DrumMapPreset InstallDrumMapDefaults(DrumMapPreset preset)
    {
        if (model is not { } current || doc is not { } document) return preset;
        preset.Validate();
        var defaults = DefaultSkin.Bundled();
        var remapped = new Dictionary<string, Lane>();
        foreach (var row in preset.Rows.Where(r => r.LaneKey is not null).DistinctBy(r => r.LaneKey))
        {
            string key = row.LaneKey!;
            var lane = ProjectStore.LaneOf(key)!;
            if (current.LaneRows.Any(r => r.Lane == lane) &&
                (lane.Source == LaneSource.Skin || document.Folder.Find(lane.Name) is not null)) continue;
            var slot = ProjectStore.LaneOf(row.SlotKey ?? key)!;
            if (lane.Source == LaneSource.Skin || document.Folder.Find(lane.Name) is not null)
            { current.AddLane(lane, slot); continue; }
            if (slot.Sample is not (var set, var sound, _)) continue;
            string family = HitsoundRules.SampleName(set, sound);
            if (!defaults.TryGetValue(family, out var bytes)) throw new IOException($"No default sample for {family}.");
            var fresh = MapFolder.FromDirectory(Path.GetDirectoryName(document.Path)!);
            string name = SampleSlots.BackingFile(fresh, slot, ".wav");
            using (var stream = new FileStream(Path.Combine(Path.GetDirectoryName(document.Path)!, name), FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
            lane = new Lane(LaneSource.BeatmapFile, name);
            current.AddLane(lane, slot); audio?.LoadLane(lane, bytes); remapped[key] = lane;
            document.RescanFolder();
        }
        return preset with { Rows = preset.Rows.Select(r => r.LaneKey is { } key && remapped.TryGetValue(key, out var lane)
            ? r with { LaneKey = ProjectStore.KeyOf(lane) } : r).ToImmutableList() };
    }

}
