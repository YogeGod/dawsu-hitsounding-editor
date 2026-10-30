using Avalonia.Controls;
using Avalonia.Input;
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
        Lanes.LaneMenuRequested += ShowLaneMenu;
        AddLaneItem.Click += async (_, _) => await AddLaneDialog();
        GenerateItem.Click += async (_, _) => await Generate();
        LaneFilterBox.TextChanged += (_, _) =>
        {
            model?.SetFilter(LaneFilterBox.Text ?? "");
            SyncScrollBars();
        };
        LaneFilterBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            LaneFilterBox.Text = "";
            LaneFilterBox.IsVisible = false;
            Lanes.Focus();
            e.Handled = true;
        };
        Lanes.LocatorsChanged += ScheduleProjectSave;
        Closed += (_, _) => SaveProject();
    }

    private void ShowFilter()
    {
        LaneFilterBox.IsVisible = true;
        LaneFilterBox.Focus();
    }

    // ---------------------------------------------------------------- lanes

    private void ShowLaneMenu(LaneRow row)
    {
        if (model is null || session is null) return;
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
                Item("Delete lane (removes its hits)", () =>
                {
                    session.Remove(session.State.Hits.Where(h => h.Lane == lane).ToList());
                    model.HideLane(lane);
                    ScheduleProjectSave();
                }),
                Item("Add lane…", async () => await AddLaneDialog()),
            },
        };
        menu.Open(Lanes);
    }

    /// <summary>Lists the mapset's audio files that have no lane yet; the chosen one gets a lane (rule 5: mapset files only).</summary>
    private async Task AddLaneDialog()
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

        if (await Dialogs.Choose(this, "Add lane", "Audio files in the mapset folder without a lane:", candidates) is not { } file) return;
        AddLane(new Lane(LaneSource.BeatmapFile, file));
        ScheduleProjectSave();
    }

    private void AddLane(Lane lane)
    {
        if (model is null || folder is null) return;
        audio?.LoadLane(lane, folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null);
        model.AddLane(lane);
        SyncScrollBars();
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

    private void SaveProject()
    {
        projectTimer?.Stop();
        if (model is null || session is null || mapPath is null) return;
        var (names, order, added, hidden) = model.LaneLayout();
        var data = new ProjectData
        {
            Unanchored = [.. session.Unanchored.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time))],
            LaneNames = names,
            LaneOrder = order,
            AddedLanes = added,
            HiddenLanes = hidden,
            LocatorLeft = Lanes.LocatorLeft,
            LocatorRight = Lanes.LocatorRight,
            Cycle = Lanes.Cycle,
            PreviewMuted = [.. model.PreviewMutedHits.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time))],
            SongOffset = audio?.SongOffset ?? 0,
        };
        try
        {
            ProjectStore.Save(ProjectStore.DefaultRoot(), mapPath, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            flash = $"Couldn't save the editor project: {ex.Message}";
            UpdateStatus();
        }
    }

    /// <summary>Puts back what the project file kept for this map: lanes, hits with no object, locators, mutes, offset.</summary>
    private void RestoreProject()
    {
        if (model is null || session is null || mapPath is null || folder is null) return;
        if (ProjectStore.Load(ProjectStore.DefaultRoot(), mapPath) is not { } project) return;

        model.ApplyLaneLayout(project, lane => lane.Source != LaneSource.BeatmapFile || folder.Find(lane.Name) is not null);
        foreach (var lane in project.AddedLanes.Select(ProjectStore.LaneOf).OfType<Lane>())
            if (folder.Find(lane.Name) is { } f) audio?.LoadLane(lane, folder.ReadBytes(f));

        // Hits with no object: only those that still have no object (the map may have changed since).
        session.Restore(project.Unanchored
            .Select(h => ProjectStore.LaneOf(h.Lane) is { } lane ? session.HitAt(lane, h.Time) : null)
            .OfType<EditHit>()
            .Where(h => h.Anchor is null));

        foreach (var muted in project.PreviewMuted)
            if (session.State.Hits.FirstOrDefault(h => ProjectStore.KeyOf(h.Lane) == muted.Lane && Math.Abs(h.Time - muted.Time) < 0.5) is { } hit)
                model.TogglePreviewMute(hit);

        if (project.LocatorLeft is { } l && project.LocatorRight is { } r) Lanes.SetLocators(l, r);
        Lanes.Cycle = project.Cycle;
        if (audio is not null && project.SongOffset != 0)
        {
            audio.SongOffset = project.SongOffset;
            Lanes.SongOffset = audio.SongOffset;
            SongOffsetBox.Text = audio.SongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        SyncScrollBars();
    }

    // ---------------------------------------------------------------- Generate mode

    /// <summary>
    /// Writes the [Hitsounds] diff: plans it, shows what will be written, renders the mixes into the mapset (fresh
    /// names only), backs up an existing [Hitsounds] diff to the editor's app data, then writes the .osu.
    /// </summary>
    private async Task Generate()
    {
        if (session is null || mapPath is null || originalBytes is null) return;
        if (session.State.Hits.Count == 0)
        {
            flash = "Nothing to generate: there are no hits.";
            UpdateStatus();
            return;
        }

        // Plan from a fresh look at the mapset (another difficulty may have changed since the map was opened), off the UI thread.
        var (opened, path, editing) = (originalBytes, mapPath, session);
        GeneratePlan plan;
        MapFolder fresh;
        try
        {
            flash = "Planning the hitsound diff…";
            UpdateStatus();
            (plan, fresh) = await Task.Run(() =>
            {
                var current = MapFolder.FromDirectory(Path.GetDirectoryName(path)!);
                var source = OsuFile.Parse(opened);
                var previous = GenerateManifest.Load(GenerateManifest.DefaultRoot(), Path.Combine(Path.GetDirectoryName(path)!, HitsoundDiff.FileNameFor(source, path)));
                return (HitsoundDiff.Plan(source, path, current, editing, previous), current);
            });
        }
        catch (IOException ex)
        {
            flash = $"Generate stopped: {ex.Message}";
            UpdateStatus();
            return;
        }

        if (string.Equals(Path.GetFullPath(plan.OsuPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            flash = "This map is the [Hitsounds] diff itself: open the difficulty you hitsound in, then Generate.";
            UpdateStatus();
            return;
        }

        bool exists = File.Exists(plan.OsuPath);
        string summary =
            $"Write {Path.GetFileName(plan.OsuPath)}{(exists ? " (replacing the existing one; a backup is kept in the editor's app data)" : "")} " +
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
        if (!await Dialogs.Confirm(this, "Generate hitsound diff", summary, "Generate")) return;

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

                byte[]? Render(MixFile mix)
                {
                    var layers = mix.Layers.Select(l => (Stereo: Decoded(l.Lane), Gain: l.Volume / 100f)).Where(l => l.Stereo is not null)
                        .Select(l => (l.Stereo!, l.Gain)).ToList();
                    return layers.Count == 0 ? null : SampleMixer.Mix(layers);
                }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            flash = $"Generate stopped, nothing was written: {ex.Message}";
        }

        UpdateStatus();
    }
}
