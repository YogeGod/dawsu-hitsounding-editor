using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>Lane management, the per-map project file, and Generate mode.</summary>
public partial class MainWindow
{
    private DispatcherTimer? projectTimer;

    private void WireMapFeatures()
    {
        SetupDrumMaps();
        Lanes.LaneLayoutChanged += ScheduleProjectSave;
        Lanes.LaneMenuRequested += ShowLaneMenu;
        Lanes.SliderMenuRequested += ShowSliderMenu;
        // Standard families add a numbered default immediately, like Electron V6.39.
        Lanes.AddLaneRequested += async lane => await AddExtraLane(lane);
        Lanes.RemoveLaneRequested += async () => await DeleteActiveLane();
        Lanes.CollapseNamesRequested += ToggleLaneNames;
        // Samples dropped into the folder (File → Open map folder) show up when the editor is back in front.
        Activated += (_, _) => RefreshFolder();
        AddLaneItem.Click += async (_, _) => await AddExtraLane();
        GenerateItem.Click += async (_, _) => await Generate();
        CopyHitsoundsItem.Click += async (_, _) => await CopyHitsounds();
        LaneFilterBox.TextChanged += (_, _) =>
        {
            model?.SetFilter(LaneFilterBox.Text ?? "");
            SyncScrollBars();
        };
        LaneFilterBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            LaneFilterBox.Text = "";
            Lanes.Focus();
            e.Handled = true;
        };
        Lanes.LocatorsChanged += ScheduleProjectSave;
        Closed += (_, _) => SaveProject();
    }

    private void ShowFilter()
    {
        LaneFilterBox.Focus();
    }

    // ---------------------------------------------------------------- lanes

    private void ShowLaneMenu(LaneRow row)
    {
        if (model is null || session is null) return;
        if (model.DrumMapMode) { ShowDrumRowMenu(row); return; }
        var lane = row.Lane;
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Rename…", async () =>
                {
                    if (await Dialogs.Prompt(this, "Rename lane", $"Display name for {lane.Name} (blank restores it). The file and what it saves as don't change.",
                        model.NameOf(lane)) is { } name)
                    {
                        model.RenameLane(lane, name);
                        ScheduleProjectSave();
                    }
                }),
                Item("Move up", () => { model.MoveLane(lane, -1); ScheduleProjectSave(); }),
                Item("Move down", () => { model.MoveLane(lane, 1); ScheduleProjectSave(); }),
                new Separator(),
                Item("Delete lane (removes its hits)", () => DeleteLane(lane)),
                Item("Add lane", async () => await AddExtraLane(lane)),
            },
        };
        menu.Open(Lanes);
    }

    /// <summary>
    /// The right-click menu on sliders in the object strip: what their bodies play, as osu!'s editor sets it on a whole
    /// slider. The slide and ticks use the sample set; the slide whistle uses the additions.
    /// </summary>
    private void ShowSliderMenu(IReadOnlyList<int> sliders)
    {
        if (model is null || session is null || sliders.Count == 0) return;
        var now = sliders.Select(session.SlideOf).ToList();
        string Name(SampleSet set) => set == SampleSet.None ? "Auto" : set.ToString();
        void Apply(Func<SlideSound, SlideSound> change, string label)
        {
            session.SetSlides(sliders, change, label);
            // A slide sound the map didn't use before needs its audio (loading one already loaded does nothing).
            foreach (var lane in session.Map.Passive.Select(p => p.Lane).Distinct())
                audio?.LoadLane(lane, lane.Source == LaneSource.Skin ? skin.Value.GetValueOrDefault(lane.Name)
                    : folder?.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null);
            Lanes.InvalidateVisual();
        }

        MenuItem SetMenu(string header, Func<SlideSound, SampleSet> read, Func<SlideSound, SampleSet, SlideSound> write) => new()
        {
            Header = header,
            ItemsSource = new[] { SampleSet.None, SampleSet.Normal, SampleSet.Soft, SampleSet.Drum }.Select(set =>
            {
                var item = new MenuItem { Header = Name(set), ToggleType = MenuItemToggleType.Radio, IsChecked = now.All(s => read(s) == set) };
                item.Click += (_, _) => Apply(s => write(s, set), $"{header}: {Name(set)}");
                return item;
            }).ToList(),
        };

        var whistle = new MenuItem { Header = "Whistle on slide", ToggleType = MenuItemToggleType.CheckBox, IsChecked = now.All(s => s.Whistle) };
        bool whistleOn = !now.All(s => s.Whistle);
        whistle.Click += (_, _) => Apply(s => s with { Whistle = whistleOn }, whistleOn ? "Slide whistle on" : "Slide whistle off");
        var title = new MenuItem { Header = sliders.Count == 1 ? "Slider body" : $"{sliders.Count} slider bodies", IsEnabled = false };
        new ContextMenu
        {
            ItemsSource = new object[]
            {
                title,
                SetMenu("Slide sample set (also the ticks)", s => s.Normal, (s, set) => s with { Normal = set }),
                SetMenu("Slide additions (the whistle)", s => s.Addition, (s, set) => s with { Addition = set }),
                whistle,
            },
        }.Open(Lanes);
    }

    /// <summary>Deletes a lane: its hits go (Undo brings them and the lane back); the sample file stays in the mapset.</summary>
    private void DeleteLane(Lane lane)
    {
        if (model is null || session is null) return;
        session.Remove(session.State.Hits.Where(h => h.Lane == lane).ToList(), "Delete lane");
        model.HideLane(lane);
        ScheduleProjectSave();
    }

    /// <summary>The − above the lane list: deletes the active lane, asking first when it has hits.</summary>
    private async Task DeleteActiveLane()
    {
        if (model is null || session is null) return;
        if (model.ActiveLane is not { } lane)
        {
            flash = "Click a lane first: − deletes the highlighted lane.";
            UpdateStatus();
            return;
        }

        int hits = session.State.Hits.Count(h => h.Lane == lane);
        if (hits > 0 && !await Dialogs.Confirm(this, "Delete lane",
                $"Delete {model.NameOf(lane)} and its {hits} hit{(hits == 1 ? "" : "s")}? Undo brings them back; the sample file stays in the mapset.", "Delete"))
            return;
        DeleteLane(lane);
    }

    /// <summary>The ‹ above the lane list: hides or shows the lane names (remembered in the preferences).</summary>
    internal void ToggleLaneNamesForSnapshot() => ToggleLaneNames();

    private void ToggleLaneNames()
    {
        ArrangeView.NamesCollapsed = !ArrangeView.NamesCollapsed;
        prefs = prefs with { LaneNamesCollapsed = ArrangeView.NamesCollapsed };
        SavePrefs();
        ApplyTheme(); // the list's width changes everything to its right
    }

    /// <summary>Lists the mapset's audio files that have no lane yet; the chosen one gets a lane (rule 5: mapset files only).</summary>
    private Lane? lastAddedLane;

    /// <param name="after">The lane the new one follows (right-clicked); otherwise the lane added last.</param>
    private async Task AddLaneDialog(Lane? after = null)
    {
        if (model is null || folder is null) return;
        var shown = model.LaneRows.Select(r => r.Lane).ToHashSet();
        var candidates = folder.Files
            .Where(f => HitsoundRules.SampleExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && !f.Contains('/'))
            .Where(f => !shown.Contains(new Lane(LaneSource.BeatmapFile, f)) && !string.Equals(f, model.Map.Settings.AudioFilename, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            flash = "Every audio file in the mapset already has a lane. Put a new sample file in the mapset folder first.";
            UpdateStatus();
            return;
        }

        var suggested = ArrangeModel.SuggestNewLane(candidates, after ?? lastAddedLane ?? model.LaneRows.LastOrDefault(r => r.Lane.Source == LaneSource.BeatmapFile)?.Lane);
        if (await Dialogs.Choose(this, "Add lane", "Audio files in the mapset folder without a lane:", candidates, suggested) is not { } file) return;
        AddLane(lastAddedLane = new Lane(LaneSource.BeatmapFile, file));
        ScheduleProjectSave();
    }

    private void AddLane(Lane lane)
    {
        if (model is null || folder is null) return;
        audio?.LoadLane(lane, folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null);
        model.AddLane(lane);
        SyncScrollBars();
    }

    private async Task AddExtraLane(Lane? lane = null)
    {
        lane ??= model?.ActiveLane;
        if (lane is not null && model?.SlotOf(lane).Sample is not null) await AddSampleTo(lane);
        else await AddLaneDialog(lane);
    }

    /// <summary>V6.39: add the next numbered layer using its family's bundled default, without a picker.</summary>
    private async Task AddSampleTo(Lane lane)
    {
        if (doc is null || model is null || session?.IsGestureActive == true) return;
        if (model.SlotOf(lane).Sample is not (var set, var sound, _))
        { await AddLaneDialog(lane); return; }
        string family = HitsoundRules.SampleName(set, sound);
        if (!DefaultSkin.Bundled().TryGetValue(family, out var bytes))
        { flash = $"No bundled default for {family}. Choose a sample through Active lane."; UpdateStatus(); return; }
        AddSampleLayer(lane, bytes);
    }

    private void AddSampleLayer(Lane lane, byte[] bytes)
    {
        if (doc is null || model is null || session?.IsGestureActive == true || model.SlotOf(lane).Sample is null) return;
        try
        {
            var mapDir = Path.GetDirectoryName(doc.Path)!;
            var fresh = MapFolder.FromDirectory(mapDir);
            var slot = model.NextSlot(lane, fresh);
            var name = SampleSlots.BackingFile(fresh, slot, ".wav");
            using (var stream = new FileStream(Path.Combine(mapDir, name), FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
            doc.RescanFolder();
            var addedLane = new Lane(LaneSource.BeatmapFile, name);
            model.AddSlotLane(addedLane, slot);
            audio?.LoadLane(addedLane, bytes);
            ScheduleProjectSave(); SyncScrollBars();
            flash = $"Added {slot.Name} with its default sound. Choose another sound through Active lane.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { flash = $"Couldn't add the sample layer: {ex.Message}"; }
        UpdateStatus();
    }

    /// <summary>Lists the mapset folder again; new sample files named after osu!'s samples get a lane at once.</summary>
    private void RefreshFolder()
    {
        if (doc is null || model is null) return;
        IReadOnlyList<string> added;
        try
        {
            added = doc.RescanFolder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // the folder is busy or gone: keep the old list
        }

        foreach (var file in added.Where(f => !f.Contains('/') && HitsoundRules.SampleExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            var lane = new Lane(LaneSource.BeatmapFile, file);
            if (lane.Sample is not null) AddLane(lane);
        }

        if (added.Count > 0) ScheduleProjectSave();
    }

    /// <summary>File → Open map folder: the mapset folder in Explorer, to drop samples into.</summary>
    private void OpenMapFolder()
    {
        if (doc is null) return;
        try
        {
            // Explorer itself, with the folder as a quoted argument: the path is never run or opened as a file.
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(Path.GetDirectoryName(doc.Path)!);
            System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            flash = $"Couldn't open the folder: {ex.Message}";
            UpdateStatus();
        }
    }

    private bool reverting;

    private async Task RevertToLastSaved()
    {
        if (reverting || doc is not { } current || !Loading.IsCompleted) return;
        reverting = true;
        RevertItem.IsEnabled = false;
        try
        {
            if (!await Dialogs.Confirm(this, "Revert to last saved",
                "Reload this difficulty from the saved .osu? Unsaved map edits, including hits with no object, will be discarded. This clears undo history. Lane layout and playback settings are kept.",
                "Revert")) return;
            if (!ReferenceEquals(doc, current) || !Loading.IsCompleted) return;

            double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom, scroll = Lanes.ScrollY;
            double position = audio?.Position ?? Lanes.Playhead;
            var viewState = model?.CaptureViewState();
            var reload = OpenAsync(current.Path, restoreUnanchored: false);
            Loading = reload;
            if (!await reload) return;

            if (viewState is not null) model?.RestoreViewState(viewState);

            (Lanes.Axis.Zoom, Lanes.Axis.Left) = (zoom, left);
            Lanes.ScrollY = scroll;
            Seek(Math.Clamp(position, 0, Math.Max(0, Lanes.Axis.End)));
            SyncScrollBars();
            SaveProject(); // remove discarded unanchored hits from the autosaved editor project too
            flash = "Reverted to the last saved .osu. Undo history cleared.";
            UpdateStatus();
        }
        finally
        {
            reverting = false;
            RevertItem.IsEnabled = doc is not null;
        }
    }

    // ---------------------------------------------------------------- project file

    private void ScheduleProjectSave()
    {
        projectTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            projectTimer!.Stop();
            SaveProject();
        });
        projectTimer.Stop();
        projectTimer.Start();
    }

    private bool SaveProject()
    {
        projectTimer?.Stop();
        if (model is null || session is null || mapPath is null) return true;
        var data = CaptureProject();
        // Keep view preferences, but don't resurrect discarded edits from a previous autosave.
        if (discardProjectOnClose && closeProjectBaseline is { } baseline)
            data = data with { Unanchored = baseline.Unanchored, HsMode = baseline.HsMode, SoundSets = baseline.SoundSets, BookmarkLabels = baseline.BookmarkLabels };
        try
        {
            ProjectStore.Save(ProjectStore.DefaultRoot(), mapPath, data);
            SaveDrumMaps();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            flash = $"Couldn't save the editor project: {ex.Message}";
            UpdateStatus();
            return false;
        }
    }

    private ProjectData CaptureProject()
    {
        var model = this.model ?? throw new InvalidOperationException("No map is open.");
        var session = this.session ?? throw new InvalidOperationException("No edit session is open.");
        var (names, order, added, hidden) = model.LaneLayout();
        return new ProjectData
        {
            BookmarkLabels = session.State.BookmarkLabels.ToDictionary(),
            Unanchored = [.. session.Unanchored.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time) { Volume = session.State.LayerVolumes.TryGetValue(h, out int volume) ? volume : null })],
            LaneNames = names,
            LaneSlots = model.SlotLayout(),
            DrumRenders = model.DrumRenders.ToDictionary(),
            LaneOrder = order,
            ActiveLane = model.ActiveLane is { } active ? ProjectStore.KeyOf(active) : null,
            AddedLanes = added,
            HiddenLanes = hidden,
            CollapsedFamilies = [.. model.CollapsedFamilies],
            HsMode = doc!.HsProject,
            ExportSettings = exportSettings,
            SoundSets = session.CaptureSoundSets(doc!.HsProject.FileHash),
            LocatorLeft = Lanes.LocatorLeft,
            LocatorRight = Lanes.LocatorRight,
            Cycle = Lanes.Cycle,
            PreviewMuted = [.. model.PreviewMutedHits.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time))],
            SongOffset = mapSongOffset,
            TimeSignatures = [.. model.TimeSignatures],
        };
    }

    /// <summary>Puts back what the project file kept for this map: lanes, hits with no object, locators, mutes, offset.</summary>
    private void RestoreProject(bool restoreUnanchored = true, HsProject? hsOverride = null)
    {
        if (model is null || session is null || mapPath is null || folder is null) return;
        var project = ProjectStore.Load(ProjectStore.DefaultRoot(), mapPath) ?? new ProjectData();
        session.RestoreBookmarkLabels(project.BookmarkLabels);
        exportSettings = project.ExportSettings ?? new ExportMergeSettings { PairSamples = true, Style = prefs.ExportStyle };
        try { exportSettings.Validate(); }
        catch (FormatException) { exportSettings = new ExportMergeSettings { PairSamples = true, Style = prefs.ExportStyle }; }
        exportSettings.PairSamples = true;
        RefreshOutputSettings();

        doc!.RestoreHsProject(hsOverride ?? project.HsMode);
        foreach (var lane in session.State.Hits.Select(h => h.Lane).Distinct())
        {
            if (!model.LaneRows.Any(r => r.Lane == lane)) model.AddLane(lane);
            if (lane.Source == LaneSource.BeatmapFile && folder.Find(lane.Name) is { } sample) audio?.LoadLane(lane, folder.ReadBytes(sample));
        }

        model.ApplyLaneLayout(project, lane => lane.Source != LaneSource.BeatmapFile || folder.Find(lane.Name) is not null);
        foreach (string family in project.CollapsedFamilies)
            if (model.LaneRows.FirstOrDefault(r => model.FamilyKey(r.Lane) == family) is { } row && !model.IsFamilyCollapsed(row.Lane)) model.ToggleFamily(row.Lane);
        foreach (var lane in project.AddedLanes.Select(ProjectStore.LaneOf).OfType<Lane>())
            if (folder.Find(lane.Name) is { } f) audio?.LoadLane(lane, folder.ReadBytes(f));

        // Hits with no object: only those that still have no object (the map may have changed since).
        if (restoreUnanchored)
        {
            var loose = project.Unanchored.Select(h => (Project: h,
                    Hit: ProjectStore.LaneOf(h.Lane) is { } lane ? session.HitAt(lane, h.Time) : null))
                .Where(p => p.Hit is not null && (p.Hit.Anchor is null || p.Hit.Anchor is { ObjectIndex: < 0 } key && !session.MomentByKey.ContainsKey(key)))
                .GroupBy(p => p.Hit).SelectMany(g => g.Select((p, i) => (p.Project, Hit: p.Hit! with { Voice = i }))).ToList();
            session.Restore(loose.Select(p => p.Hit!), loose.Where(p => p.Project.Volume is >= 0 and <= 100)
                .ToDictionary(p => p.Hit!, p => p.Project.Volume!.Value));
        }

        try { session.RestoreSoundSets(project.SoundSets, GenerateManifest.Hash(doc!.Opened)); }
        catch (FormatException ex) { flash = $"Sound-set links were not restored: {ex.Message}"; }

        foreach (var muted in project.PreviewMuted)
            if (session.State.Hits.FirstOrDefault(h => ProjectStore.KeyOf(h.Lane) == muted.Lane && Math.Abs(h.Time - muted.Time) < 0.5) is { } hit)
                model.TogglePreviewMute(hit);

        if (project.TimeSignatures.Count > 0) model.SetTimeSignatures(project.TimeSignatures);
        if (project.LocatorLeft is { } l && project.LocatorRight is { } r) Lanes.SetLocators(l, r);
        Lanes.Cycle = project.Cycle;
        mapSongOffset = double.IsFinite(project.SongOffset) ? Math.Clamp(project.SongOffset, -10000, 10000) : 0;
        ApplyOffsets();

        model.RebuildNotes();
        RestoreDrumMaps();
        SyncScrollBars();
    }

    // ---------------------------------------------------------------- Generate mode

    /// <summary>
    /// Writes the [Hitsounds] diff: plans it, shows what will be written, renders the mixes into the mapset (fresh
    /// names only), backs up an existing [Hitsounds] diff to the editor's app data, then writes the .osu.
    /// </summary>
    /// <summary>
    /// Map → Copy hitsounds (Mapping Tools' Hitsound Copier): from one difficulty into others of the mapset. The open map
    /// as the source copies what you see, unsaved edits included; as a target it must have none, and it's reloaded after.
    /// </summary>
    private async Task CopyHitsounds()
    {
        if (doc is not { } document || session is null || mapPath is null)
        {
            flash = "Open a map first: Copy hitsounds copies between the difficulties of its mapset.";
            UpdateStatus();
            return;
        }

        var dir = Path.GetDirectoryName(document.Path)!;
        var difficulties = document.Folder.Files
            .Where(f => !f.Contains('/') && f.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.Combine(dir, f))
            .Select(p => (Path: p, Name: HitsoundDiff.VersionOf(p) ?? Path.GetFileNameWithoutExtension(p)))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (await Dialogs.CopyHitsounds(this, difficulties, document.Path) is not var (sourcePath, targets, options)) return;

        bool openIsTarget = targets.Any(t => string.Equals(t, document.Path, StringComparison.OrdinalIgnoreCase));
        if (openIsTarget && session.IsEdited)
        {
            flash = "This map is one of the targets and has unsaved edits: save them (Ctrl+S) or undo them first.";
            UpdateStatus();
            return;
        }

        bool fromOpen = string.Equals(sourcePath, document.Path, StringComparison.OrdinalIgnoreCase);
        var state = session.State;
        var skinSounds = skin.Value;
        flash = "Copying hitsounds…";
        UpdateStatus();
        IReadOnlyList<HitsoundCopyTarget> results;
        bool wasEnabled = Root.IsEnabled;
        Root.IsEnabled = false;
        try
        {
            results = await Task.Run(() =>
            {
                // The open map's source is what you see, unsaved edits included.
                var (source, folder) = fromOpen
                    ? (document.Compile(state).File, document.Folder)
                    : (OsuFile.Read(sourcePath), MapFolder.FromDirectory(Path.GetDirectoryName(sourcePath)!));
                // New tick and slide samples are mixed from the source's files (and the default sounds), as Generate mixes.
                var decoded = new Dictionary<Lane, float[]?>();
                byte[]? Render(MixFile mix)
                {
                    float[]? Decode(Lane lane)
                    {
                        if (!decoded.TryGetValue(lane, out var stereo))
                        {
                            var bytes = lane.Source == LaneSource.Skin ? skinSounds.GetValueOrDefault(lane.Name) : folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null;
                            decoded[lane] = stereo = bytes is null ? null : SampleMixer.Decode(bytes);
                        }
                        return stereo;
                    }
                    return SampleMixer.Mix(mix, Decode);
                }
                var sourceSamples = new List<MixFile>();
                GeneratePlan? sourcePlan = null;
                if (fromOpen)
                {
                    (GeneratePlan Plan, MapFolder Folder) planned;
                    if (document.Session.Unanchored.Any()) planned = document.PlanGenerate(GenerateManifest.DefaultRoot(), skinSounds, exportSettings, includeSliderAudio: true);
                    else
                    {
                        var savedSource = document.PlanHsSave(GenerateManifest.DefaultRoot(), skinSounds, exportSettings);
                        planned = (savedSource.Plan.Storage, savedSource.Folder);
                    }
                    sourcePlan = planned.Plan;
                    if (sourcePlan.Conflicts.Count > 0) throw new IOException(sourcePlan.Conflicts[0].Reason);
                    if (!options.CopySampleSets && sourcePlan.Mixes.Count > 0)
                        throw new IOException("Enable Copy sample sets and indexes to preserve the unsaved source's paired or fused sounds.");
                    folder = planned.Folder;
                    foreach (var sample in sourcePlan.Mixes)
                    {
                        var rendered = Render(sample) ?? throw new IOException($"Could not render {sample.FileName}.");
                        sourceSamples.Add(sample with { CopyBytes = sample.CopyBytes ?? rendered });
                    }
                    var fresh = folder;
                    var assets = sourceSamples.ToDictionary(m => m.FileName, m => m.CopyBytes!, StringComparer.OrdinalIgnoreCase);
                    folder = new MapFolder(fresh.Files.Concat(assets.Keys), name => assets.TryGetValue(name, out var raw) ? raw : fresh.ReadBytes(name));
                    source = sourcePlan.Diff;
                }
                var copied = HitsoundCopier.CopyTo(source, folder, targets, options, MapSaver.DefaultBackupRoot(), Render, exportSettings, skinSounds, sourceSamples, sourcePlan?.InputFiles);
                if (sourcePlan is not null && copied.Any(r => r.Error is null))
                {
                    // Reuse these generated assets next time even though Copy did not save the source difficulty.
                    var records = sourceSamples.Where(m => File.Exists(Path.Combine(dir, m.FileName)) && File.ReadAllBytes(Path.Combine(dir, m.FileName)).AsSpan().SequenceEqual(m.CopyBytes))
                        .Select(m => new GeneratedMix(m.FileName, m.Signature, GenerateManifest.Hash(m.CopyBytes!)) { InputHash = sourcePlan.InputHashes.GetValueOrDefault(m.Signature) });
                    try { new GenerateManifest(sourcePlan.Reused.Concat(records).ToList()) { LastIndex = sourcePlan.LastMixIndex }.Save(GenerateManifest.DefaultRoot(), sourcePlan.OsuPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return copied;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            flash = $"Couldn't read the source: {ex.Message}";
            UpdateStatus();
            return;
        }
        finally { Root.IsEnabled = wasEnabled; }

        if (openIsTarget) await Open(mapPath); // show what was written
        int written = results.Count(r => r.Error is null), conflicts = results.Sum(r => r.Conflicts);
        var failed = results.Where(r => r.Error is not null).ToList();
        flash = $"Copied hitsounds into {written} of {results.Count} difficult{(results.Count == 1 ? "y" : "ies")} " +
                $"— {failed.Count} not written.\n" +
                $"{results.Sum(r => r.Matched)} hits matched, {results.Sum(r => r.Changed)} hit points changed." +
                (results.Sum(r => r.Muted) is > 0 and var mutedEnds ? $" {mutedEnds} slider ends muted." : "") +
                (results.Sum(r => r.OnTicksAndSlides) is > 0 and var onSliders ? $" {onSliders} hits put on slider ticks and slides ({results.Sum(r => r.Generated)} new samples)." : "") +
                (conflicts > 0 ? $" {conflicts} couldn't be saved as copied and kept their old sounds." : "") +
                (failed.Count > 0 ? $" Not written: {string.Join("; ", failed.Select(f => $"{Path.GetFileName(f.Path)} ({f.Error})"))}." : "") +
                " Backups are in the editor's backups folder.\n" +
                string.Join("\n", results.Select(r => $"{Path.GetFileName(r.Path)}: " +
                    (r.Error is not null ? $"Not written — {r.Error}" : $"{r.Changed} hit points changed; {r.Conflicts} conflicts; " +
                        (r.Backup is not null ? $"backup: {r.Backup}" : "no map backup created (unchanged or not written)."))));
        UpdateStatus();
    }

    private bool generating;
    private async Task Generate()
    {
        if (generating || savingHs || !Loading.IsCompleted) return;
        generating = true;
        bool wasEnabled = Root.IsEnabled;
        Root.IsEnabled = false;
        try { await GenerateCore(); }
        finally { Root.IsEnabled = wasEnabled; generating = false; }
    }

    private async Task GenerateCore()
    {
        if (doc is not { } document || session is null || mapPath is null) return;
        if (session.State.Hits.Count == 0)
        {
            flash = "Nothing to generate: there are no hits.";
            UpdateStatus();
            return;
        }

        // Plan from a fresh look at the mapset (another difficulty may have changed since the map was opened), off the UI thread.
        var path = mapPath;
        var generatingState = session.State;
        GeneratePlan plan;
        MapFolder fresh;
        try
        {
            flash = "Planning the hitsound diff…";
            UpdateStatus();
            (plan, fresh) = await Task.Run(() => document.PlanGenerate(GenerateManifest.DefaultRoot(), skin.Value, exportSettings));
        }
        catch (Exception ex)
        {
            // Planning only reads, so any failure here leaves everything as it was: say so instead of closing the editor.
            flash = $"Generate stopped, nothing was written: {ex.Message}";
            UpdateStatus();
            return;
        }

        if (string.Equals(Path.GetFullPath(plan.OsuPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            flash = "This map is the [Hitsounds] diff itself: open the difficulty you hitsound in, then Generate.";
            UpdateStatus();
            return;
        }

        ExportMergeSettings? mergeSettings = null;
        if (!exportSettings.PairSamples && plan.MergeCombinations.Count > 0)
        {
            var previewDecoded = new Dictionary<Lane, float[]?>();
            void Preview(MixFile mix)
            {
                if (audio is null) throw new IOException("Audio output is unavailable. Check Preferences → Audio output.");
                float[]? Decode(Lane lane)
                {
                    if (previewDecoded.TryGetValue(lane, out var cached)) return cached;
                    var bytes = lane.Source == LaneSource.Skin ? skin.Value.GetValueOrDefault(lane.Name)
                        : fresh.Find(lane.Name) is { } file ? fresh.ReadBytes(file) : null;
                    return previewDecoded[lane] = bytes is null ? null : SampleMixer.Decode(bytes);
                }
                audio.Pause();
                audio.PreviewMix(SampleMixer.Mix(mix, Decode));
            }
            try { mergeSettings = await Dialogs.MergePriorities(this, plan.MergeCombinations, Preview); }
            finally { if (previewDecoded.Count > 0) audio?.StopAudition(); }
            if (mergeSettings is null) return;
            try
            {
                (plan, fresh) = await Task.Run(() => document.PlanGenerate(GenerateManifest.DefaultRoot(), skin.Value, mergeSettings));
            }
            catch (Exception ex)
            {
                flash = $"Generate stopped, nothing was written: {ex.Message}";
                UpdateStatus();
                return;
            }
        }

        if (!ReferenceEquals(document, doc) || !ReferenceEquals(generatingState, document.Session.State))
        { flash = "The edits changed while Generate was being prepared. Generate again; nothing was written."; UpdateStatus(); return; }
        string Summary(GeneratePlan plan) =>
            $"Write {Path.GetFileName(plan.OsuPath)}{(File.Exists(plan.OsuPath) ? " (replacing the existing one; a backup is kept in the editor's app data)" : "")} " +
            $"with {plan.Circles} circle{(plan.Circles == 1 ? "" : "s")}.\n\n" +
            (plan.Mixes.Count > 0
                ? $"{plan.Mixes.Count} mixed sample{(plan.Mixes.Count == 1 ? "" : "s")} will be written to the mapset ({string.Join(", ", plan.Mixes.Take(4).Select(m => m.FileName))}{(plan.Mixes.Count > 4 ? ", …" : "")}). " +
                  "Mixes that include default skin sounds contain that audio (non-commercial licence).\n\n"
                : "No new mixed samples are needed.\n\n") +
            (plan.Reused.Count > 0 ? $"{plan.Reused.Count} mix(es) from the last Generate are used again as they are (same files, same indexes).\n\n" : "") +
            (plan.Removes.Count > 0
                ? $"{plan.Removes.Count} mix(es) from the last Generate that nothing uses any more will be removed ({string.Join(", ", plan.Removes.Take(4))}{(plan.Removes.Count > 4 ? ", …" : "")}).\n\n"
                : "") +
            (plan.Conflicts.Count > 0 ? $"{plan.Conflicts.Count} moment(s) still can't be written: {plan.Conflicts[0].Reason}\n\n" : "") +
            "Your opened map is not changed.";
        var confirmed = await Dialogs.ConfirmGenerate(this, (plan, fresh), Summary,
            includeSliderAudio => Task.Run(() => document.PlanGenerate(GenerateManifest.DefaultRoot(), skin.Value,
                mergeSettings ?? exportSettings, includeSliderAudio)), mergeSettings);
        if (confirmed is null) return;
        (plan, fresh) = confirmed.Value;
        if (!ReferenceEquals(document, doc) || !ReferenceEquals(generatingState, document.Session.State))
        { flash = "The edits changed before Generate was confirmed. Generate again; nothing was written."; UpdateStatus(); return; }

        var skinSounds = skin.Value;
        try
        {
            string? note = null;
            await Task.Run(() =>
            {
                // Each sample is read and decoded once, however many mixes use it.
                var decoded = new Dictionary<Lane, float[]?>();
                float[]? Decoded(Lane lane)
                {
                    if (decoded.TryGetValue(lane, out var d)) return d;
                    var bytes = lane.Source == LaneSource.Skin ? skinSounds.GetValueOrDefault(lane.Name)
                        : fresh.Find(lane.Name) is { } f ? fresh.ReadBytes(f) : null;
                    return decoded[lane] = bytes is null ? null : SampleMixer.Decode(bytes);
                }

                byte[]? Render(MixFile mix) => SampleMixer.Mix(mix, Decoded);

                var written = GenerateWriter.Write(plan, Render, MapSaver.DefaultBackupRoot());
                try
                {
                    written.Manifest.Save(GenerateManifest.DefaultRoot(), plan.OsuPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The mapset is written; only reuse next time is lost (the next Generate takes fresh indexes).
                    note = $" (Couldn't record its mixes for next time: {ex.Message})";
                }
            });
            flash = $"Generated {Path.GetFileName(plan.OsuPath)}: {plan.Circles} circles, {plan.Mixes.Count} new and {plan.Reused.Count} reused mixed sample(s)." +
                    (plan.Conflicts.Count > 0 ? $" {plan.Conflicts.Count} moment(s) couldn't be written." : "") + note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            flash = $"Generate stopped, nothing was written: {ex.Message}";
            UpdateStatus();
            return;
        }
        catch (Exception ex)
        {
            // Unexpected: report it rather than close the editor (the old diff, if any, has a backup in the editor's app data).
            flash = $"Generate failed: {ex.Message}";
            UpdateStatus();
            return;
        }

        UpdateStatus();
        var next = await Dialogs.Choose(this, "Hitsound diff generated", $"Generated {Path.GetFileName(plan.OsuPath)}. What next?",
            ["Stay here", "Open generated difficulty", "Copy into other difficulties…"], "Stay here", accept: "Continue");
        if (next is "Open generated difficulty" or "Copy into other difficulties…" && await MayLeaveMap())
        {
            await Open(plan.OsuPath);
            if (next == "Copy into other difficulties…" && string.Equals(doc?.Path, plan.OsuPath, StringComparison.OrdinalIgnoreCase))
                await CopyHitsounds();
        }
    }
}
