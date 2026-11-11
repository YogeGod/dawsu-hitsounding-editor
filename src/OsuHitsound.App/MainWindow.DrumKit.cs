using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// Map lanes from drum instrument: a drum map made from the loaded VST3's own note mapping. Each mapped drum and
/// articulation gets a named lane with its own drum-set sample index and MIDI note, and optionally its sound rendered
/// from the plug-in. The names come from dawsu!'s copy of a known kit's mapping, or else from the plug-in itself
/// (VST 3 pitch names, asked through a short-lived isolated host).
/// </summary>
public partial class MainWindow
{
    private const int KitRenderVelocity = 100;

    /// <summary>A kit render: each note's WAV; how many notes played nothing; whether the kit's own settings were used.</summary>
    private sealed record KitSounds(Dictionary<int, byte[]> Sounds, int Silent, bool Defaults);

    // A sampler such as Kontakt loads with nothing in it; its saved state is a few KB until an instrument is loaded
    // (an empty Kontakt 7 saves about 2.7 KB, one with a drum kit over 1 MB).
    private const int LoadedStateBytes = 64 * 1024;
    private Avalonia.Threading.DispatcherTimer? kitWatch;

    /// <summary>
    /// After a drum VST3 loads: a kit dawsu! knows is offered its lanes at once; any other plug-in is watched until an
    /// instrument is loaded in it (its state grows), and only then asked which drum map to use.
    /// </summary>
    internal async Task OfferKitMappingAfterLoad()
    {
        kitWatch?.Stop(); kitWatch = null;
        if (drumVst is null || drumVstName is null) return;
        if (DrumKitMapping.Known(drumVstName) is not null) { await MapLanesFromDrumKit(onLoad: true); return; }
        var watched = drumVst;
        kitWatch = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        kitWatch.Tick += async (_, _) =>
        {
            if (!ReferenceEquals(drumVst, watched)) { kitWatch?.Stop(); return; }
            if (model is null || audio?.IsPlaying == true || renderingDrums) return; // read the state only while stopped
            int size;
            // Like every state read: detached from the mixer meanwhile (an audition can play while stopped).
            audio?.SetDrumInstrument(null);
            try { size = watched.SaveState().Length; }
            catch (InvalidOperationException) { return; }
            finally { RefreshDrumVstRouting(); }
            if (size < LoadedStateBytes) return;
            kitWatch?.Stop(); // before asking, so the question comes once
            await MapLanesFromDrumKit(onLoad: true);
        };
        kitWatch.Start();
        flash = $"Load a drum kit in {drumVstName}: dawsu! asks which drum map to use once one is loaded.";
        UpdateStatus();
    }

    private void SetupDrumKitMapping()
    {
        DrumKitMapItem.Click += async (_, _) => await MapLanesFromDrumKit(onLoad: false);
        DrumKitMapPlaybackItem.Click += async (_, _) => await MapLanesFromDrumKit(onLoad: false);
    }

    /// <summary>The kit's articulations: a known table, else the names the plug-in reports; null when there are none.</summary>
    private async Task<IReadOnlyList<DrumKitArticulation>?> DrumKitArticulations(string pluginPath, string pluginName)
    {
        if (DrumKitMapping.Known(pluginName) is { } known) return known;
        try
        {
            using var probe = await IsolatedVst3.StartAsync(pluginPath, timeout: TimeSpan.FromMinutes(2));
            var names = await probe.NoteNamesAsync();
            return names.Count > 0 ? DrumKitMapping.FromNoteNames(names) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Asks, then maps. On load it asks only when dawsu! knows the kit's names and the mapset has no drum map named
    /// after it yet, and stays quiet otherwise; from the menu it says why nothing can be mapped.
    /// </summary>
    internal async Task MapLanesFromDrumKit(bool onLoad)
    {
        void Say(string message) { if (!onLoad) { flash = message; UpdateStatus(); } }
        if (model is null || doc is null || session is null) { Say("Open a map first: the lanes are made in its mapset."); return; }
        if (drumVst is null || drumVstPath is null || drumVstName is null) { Say("Load a drum VST3 first (Playback → Drums VST…)."); return; }
        if (renderingDrums || generating || savingHs || drumReadFailed) { Say("Finish the current render or save first."); return; }
        string kitName = drumVstName.Length > 128 ? drumVstName[..128] : drumVstName;
        var targetModel = model;
        // Asked by itself only while the drum map is untouched: once rows are assigned (by this or by hand), loading
        // the kit again doesn't ask again. The menu item still maps on request.
        if (onLoad && targetModel.ActiveDrumMap is { } active && active.Rows.Any(r => r.LaneKey is not null)) return;
        // A kit dawsu! knows by name; else the names the plug-in reports (from the menu: it starts a short-lived host);
        // else, for Kontakt and the like, a map the user chooses: a GGD library's own EXPORT MAP file, or a built-in one.
        var kit = DrumKitMapping.Known(drumVstName) ?? (onLoad ? null : await DrumKitArticulations(drumVstPath, drumVstName));
        if (kit is null && ReferenceEquals(model, targetModel)) kit = await ChooseKitMap(drumVstName);
        if (kit is null) { Say($"No drum map was chosen for {drumVstName}, so nothing was changed."); return; }
        if (!ReferenceEquals(model, targetModel)) return;
        var withChokes = DrumKitMapping.Rows(kit, includeChokes: true);
        var without = DrumKitMapping.Rows(kit, includeChokes: false);
        if (without.Count == 0) { Say($"{drumVstName} has no mapped drum notes."); return; }

        // Fill: the active drum map's unassigned rows that the kit has an instrument for (the starter "Yoge Expanded
        // Kit", say) get their lanes, keeping their names. With nothing to fill, a new drum map named after the kit.
        var fillPreset = targetModel.ActiveDrumMap;
        var fills = UnassignedMatches(fillPreset, withChokes);
        bool fill = fills.Count > 0;
        var answer = fill
            ? await Dialogs.DrumKitMap(this, drumVstName, fills.Count, 0, (fillPreset!.Name, fillPreset.Rows.Count(r => r.LaneKey is null)))
            : await Dialogs.DrumKitMap(this, drumVstName, without.Count, withChokes.Count - without.Count);
        if (answer is not var (chokes, render) || !ReferenceEquals(model, targetModel) || drumVst is null) return;
        if (!fill && targetModel.DrumMaps.Presets.Count >= 64) { flash = "This mapset already has 64 drum maps. Remove one in Drum Map Setup… first."; UpdateStatus(); return; }
        var rows = fill ? fills.Select(f => f.Kit with { Name = f.Row.Name }).ToList() : chokes ? withChokes : without;

        KitSounds? sounds = null;
        string? renderProblem = null;
        if (render)
        {
            try { sounds = await RenderKitSounds(rows.Select(r => r.Note).Distinct().ToList()); }
            catch (OperationCanceledException) { flash = "Drum lane mapping was cancelled; nothing was changed."; UpdateStatus(); return; }
            // A kit that won't render still gets its named lanes, with default sounds, and says why.
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { renderProblem = ex.Message; }
            if (!ReferenceEquals(model, targetModel)) return;
        }
        try
        {
            if (fill)
            {
                // The map as it is now: rows assigned meanwhile (or a removed map) are left alone.
                var current = targetModel.DrumMaps.Presets.FirstOrDefault(p => p.Id == fillPreset!.Id)
                    ?? throw new IOException("The drum map was removed while its sounds were rendering.");
                var still = fills.Where(f => current.Rows.Any(r => r.Id == f.Row.Id && r.LaneKey is null)).ToList();
                var entries = AddKitLanes([.. still.Select(f => f.Kit with { Name = f.Row.Name })], sounds?.Sounds);
                var assigned = still.Select((f, n) => (f.Row.Id, Entry: entries[n])).ToDictionary(x => x.Id, x => x.Entry);
                var filled = current with { Rows = current.Rows.Select(r => assigned.TryGetValue(r.Id, out var e)
                    ? r with { LaneKey = e.LaneKey, SlotKey = e.SlotKey, MidiNote = e.MidiNote } : r).ToImmutableList() };
                targetModel.ChangeDrumMaps(targetModel.DrumMaps with
                {
                    Presets = targetModel.DrumMaps.Presets.Replace(current, filled), Active = filled.Id, Enabled = true,
                }, "Map lanes from drum instrument");
                flash = $"Assigned {assigned.Count} rows of {filled.Name} from {drumVstName}";
            }
            else
            {
                var preset = new DrumMapPreset(Guid.NewGuid(), kitName, [.. AddKitLanes(rows, sounds?.Sounds)]);
                targetModel.ChangeDrumMaps(targetModel.DrumMaps with { Presets = targetModel.DrumMaps.Presets.Add(preset), Active = preset.Id, Enabled = true }, "Map lanes from drum instrument");
                flash = $"Mapped {rows.Count} lanes from {drumVstName}";
            }
            Lanes.ScrollY = 0; SyncScrollBars(); RefreshFolder(); ScheduleProjectSave(); RefreshDrumVstRouting();
            if (renderProblem is not null) flash += $" with default sounds: the kit couldn't be rendered ({renderProblem}).";
            else if (sounds is null) flash += ". They play through the VST3 at each hit's velocity; Render drums… bakes them into WAVs for Save.";
            else
            {
                flash += sounds.Defaults
                    ? " with one-shot samples (velocity 100) from the kit's default settings (its current settings couldn't be copied yet)."
                    : " with one-shot samples (velocity 100). While the VST3 is loaded it still plays each hit at its own velocity.";
                if (sounds.Silent > 0) flash += $" {sounds.Silent} notes played nothing and keep default sounds.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { flash = $"Couldn't map the drum lanes: {ex.Message}"; }
        UpdateStatus();
    }

    /// <summary>
    /// Which mapping a plug-in that can't be read uses: GGD map exports found in the libraries' Documents folders (newest
    /// first), dawsu!'s built-in kits, or a map file to browse for. Null when cancelled or unreadable (said in the status).
    /// </summary>
    private async Task<IReadOnlyList<DrumKitArticulation>?> ChooseKitMap(string plugin)
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var choices = new Dictionary<string, Func<IReadOnlyList<DrumKitArticulation>>>();
        foreach (var (library, name, path) in GgdMapFile.Find(documents))
            choices.TryAdd($"{library}: {name} (GGD map export)", () => GgdMapFile.Read(path));
        foreach (var (name, kit) in DrumKitMapping.BuiltIn)
            choices.TryAdd($"Built-in: {name}", () => kit);
        const string browse = "Browse for a GGD map export (.nka)…";
        string? chosen = await Dialogs.Choose(this, "Choose the kit's drum map",
            $"{plugin} doesn't say what its drum notes are. Choose the kit's mapping: in a GGD library, EXPORT MAP on its Mappings page saves the current one.",
            [.. choices.Keys, browse], initial: choices.Keys.FirstOrDefault() ?? browse, accept: "Use map");
        if (chosen is null) return null;
        try
        {
            if (chosen != browse) return choices[chosen]();
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Choose a GGD map export",
                FileTypeFilter = [new("GGD map export") { Patterns = ["*.nka"] }],
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(documents),
            });
            return files.FirstOrDefault()?.TryGetLocalPath() is { } picked ? GgdMapFile.Read(picked) : null;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        { flash = $"Couldn't read that drum map: {ex.Message}"; UpdateStatus(); return null; }
    }

    /// <summary>A drum map's unassigned rows and the kit instrument each stands for (rows the kit can't play are left out).</summary>
    internal static List<(DrumMapEntry Row, DrumKitRow Kit)> UnassignedMatches(DrumMapPreset? preset, IReadOnlyList<DrumKitRow> kit) =>
        preset is null ? [] : [.. preset.Rows.Where(r => r.LaneKey is null)
            .Select(r => (Row: r, Kit: DrumKitMapping.Match(r.Name, kit))).Where(m => m.Kit is not null).Select(m => (m.Row, m.Kit!))];

    /// <summary>One new lane per row, in its drum-set family with a free index: the rendered sound, or the family default.</summary>
    private List<DrumMapEntry> AddKitLanes(IReadOnlyList<DrumKitRow> rows, IReadOnlyDictionary<int, byte[]>? sounds)
    {
        var current = model!; var document = doc!;
        string directory = Path.GetDirectoryName(document.Path)!;
        var defaults = DefaultSkin.Bundled();
        var entries = new List<DrumMapEntry>();
        var made = new List<Lane>();
        var fresh = MapFolder.FromDirectory(directory); // listed once; each new file is added to it as it's written
        try
        {
        foreach (var row in rows)
        {
            var family = new Lane(LaneSource.Skin, HitsoundRules.SampleName(SampleSet.Drum, row.Sound));
            var slot = current.NextSlot(family, fresh);
            byte[] bytes = sounds?.GetValueOrDefault(row.Note) ?? defaults.GetValueOrDefault(family.Name)
                ?? throw new IOException($"No default sound for {family.Name}.");
            string name = SampleSlots.BackingFile(fresh, slot, ".wav");
            using (var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
            fresh = fresh.With([name]);
            var lane = new Lane(LaneSource.BeatmapFile, name);
            current.AddLane(lane, slot);
            audio?.LoadLane(lane, bytes);
            made.Add(lane);
            entries.Add(new DrumMapEntry(Guid.NewGuid(), row.Name, ProjectStore.KeyOf(lane), ProjectStore.KeyOf(slot)) { MidiNote = row.Note });
        }
        }
        catch
        {
            // All or nothing: a failure part-way takes back the lanes and files already made.
            foreach (var lane in made)
            {
                current.HideLane(lane);
                try { File.Delete(Path.Combine(directory, lane.Name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            document.RescanFolder();
            throw;
        }
        document.RescanFolder();
        return entries;
    }

    /// <summary>
    /// Each note's one-shot from a fresh copy of the loaded kit (its current settings), as Render drums does. A progress
    /// window can cancel it (OperationCanceledException); a kit that fails or renders only silence throws
    /// InvalidOperationException.
    /// </summary>
    private async Task<KitSounds> RenderKitSounds(IReadOnlyList<int> notes)
    {
        if (drumVst is null || drumVstPath is null) throw new InvalidOperationException("No drum VST3 is loaded.");
        renderingDrums = true;
        bool defaults = false;
        int silent = 0;
        bool enabledBefore = Root.IsEnabled;
        Window? progress = null;
        string staging = Path.Combine(Path.GetTempPath(), "dawsu-drum-kit-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        void PreventClose(object? sender, WindowClosingEventArgs e) { e.Cancel = true; cancellation.Cancel(); }
        Closing += PreventClose;
        try
        {
            audio?.Pause(); audio?.SetDrumInstrument(null);
            var captured = drumVst.SaveState();
            Root.IsEnabled = false;
            var label = new TextBlock { Text = "Preparing the kit…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
            cancel.Click += (_, _) => { cancellation.Cancel(); label.Text = "Cancelling…"; cancel.IsEnabled = false; };
            progress = new Window { Title = "Rendering drum sounds", Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { label, cancel } }, Background = Background, RequestedThemeVariant = RequestedThemeVariant };
            progress.Closing += PreventClose;
            _ = progress.ShowDialog(this);
            Directory.CreateDirectory(staging);
            using var renderer = new Vst3Instrument(drumVstPath);
            // A kit that has only just loaded may not take its own state back yet; it then renders with its defaults,
            // which is what a just-loaded kit has anyway.
            try { renderer.RestoreState(captured); }
            catch (InvalidOperationException) { defaults = true; }
            await DrumVstRenderer.Warmup(renderer, cancellation.Token);
            var sounds = new Dictionary<int, byte[]>();
            foreach (int note in notes)
            {
                label.Text = $"Rendering {sounds.Count + silent + 1} / {notes.Count}\nMIDI note {note}";
                string output = Path.Combine(staging, $"{note}.wav");
                // A note the kit leaves silent keeps the family's default sound; the others still render.
                try { await DrumVstRenderer.Render(renderer, new DrumRenderKey(note, KitRenderVelocity), output, cancellation.Token, fadeAtLimit: true); }
                catch (SilentRenderException) { silent++; continue; }
                sounds[note] = await File.ReadAllBytesAsync(output, cancellation.Token);
            }
            if (sounds.Count == 0) throw new InvalidOperationException("every note rendered silent; check that the kit plays in its own window");
            return new(sounds, silent, defaults);
        }
        finally
        {
            Closing -= PreventClose;
            if (progress is not null) { progress.Closing -= PreventClose; progress.Close(); }
            Root.IsEnabled = enabledBefore;
            renderingDrums = false;
            RefreshDrumVstRouting();
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

}
