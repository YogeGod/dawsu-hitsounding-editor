using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>The window's part of the editor project: locators, cycle, export settings and the song offset.</summary>
internal sealed record ProjectView(double? LocatorLeft, double? LocatorRight, bool Cycle, ExportMergeSettings ExportSettings, double SongOffset);

/// <summary>
/// The editor project of one open map (see CONTEXT.md): reads it into the edit session and the lane model, and writes
/// them back with the window's <see cref="ProjectView"/>. Saved values that are damaged or point at lanes that are gone
/// are skipped or put back in range. Audio and drum maps are not part of it.
/// </summary>
internal sealed class EditorProject(string root, MapDocument document, ArrangeModel model)
{
    /// <summary>The largest song offset (ms, either way) a map's project may set.</summary>
    public const double MaxSongOffset = 10000;

    // The project right after it was restored: what Save puts back when the map's unsaved edits are discarded.
    private ProjectData? opened;

    private EditSession Session => document.Session;

    /// <summary>
    /// Puts back what the project kept for this map and returns the window's part of it, with a warning when sound-set
    /// links couldn't be restored. With no project, or a damaged one, the defaults apply.
    /// </summary>
    /// <param name="defaultStyle">The export style for a project that has no (readable) export settings.</param>
    /// <param name="restoreUnanchored">False to leave out the saved hits with no object.</param>
    /// <param name="hsOverride">HS-mode state to use instead of the saved one.</param>
    public (ProjectView View, string? Warning) Restore(ExportStyle defaultStyle, bool restoreUnanchored = true, HsProject? hsOverride = null)
    {
        var project = ProjectStore.Load(root, document.Path) ?? new ProjectData();
        opened = project; // until the restore completes: discarding then keeps what the file had
        string? warning = null;
        Session.RestoreBookmarkLabels(project.BookmarkLabels);
        document.RestoreHsProject(hsOverride ?? project.HsMode);
        foreach (var lane in Session.State.Hits.Select(h => h.Lane).Distinct())
            if (!model.LaneRows.Any(r => r.Lane == lane)) model.AddLane(lane);

        model.ApplyLaneLayout(project, lane => lane.Source != LaneSource.BeatmapFile || document.Folder.Find(lane.Name) is not null);
        foreach (string family in project.CollapsedFamilies)
            if (model.LaneRows.FirstOrDefault(r => model.FamilyKey(r.Lane) == family) is { } row && !model.IsFamilyCollapsed(row.Lane)) model.ToggleFamily(row.Lane);

        // Hits with no object: only those that still have no object (the map may have changed since).
        if (restoreUnanchored)
        {
            var loose = project.Unanchored.Select(h => (Project: h,
                    Hit: ProjectStore.LaneOf(h.Lane) is { } lane ? Session.HitAt(lane, h.Time) : null))
                .Where(p => p.Hit is not null && (p.Hit.Anchor is null || p.Hit.Anchor is { ObjectIndex: < 0 } key && !Session.MomentByKey.ContainsKey(key)))
                .GroupBy(p => p.Hit).SelectMany(g => g.Select((p, i) => (p.Project, Hit: p.Hit! with { Voice = i }))).ToList();
            Session.Restore(loose.Select(p => p.Hit!), loose.Where(p => p.Project.Volume is >= 0 and <= 100)
                .ToDictionary(p => p.Hit!, p => p.Project.Volume!.Value));
        }

        try { Session.RestoreSoundSets(project.SoundSets, GenerateManifest.Hash(document.Opened)); }
        catch (FormatException ex) { warning = $"Sound-set links were not restored: {ex.Message}"; }

        foreach (var muted in project.PreviewMuted)
            if (Session.State.Hits.FirstOrDefault(h => ProjectStore.KeyOf(h.Lane) == muted.Lane && Math.Abs(h.Time - muted.Time) < 0.5) is { } hit)
                model.TogglePreviewMute(hit);
        if (project.TimeSignatures.Count > 0) model.SetTimeSignatures(project.TimeSignatures);
        model.RebuildNotes();

        bool locators = project.LocatorLeft is not null && project.LocatorRight is not null;
        var view = new ProjectView(locators ? project.LocatorLeft : null, locators ? project.LocatorRight : null, project.Cycle,
            ExportSettings(project.ExportSettings, defaultStyle),
            double.IsFinite(project.SongOffset) ? Math.Clamp(project.SongOffset, -MaxSongOffset, MaxSongOffset) : 0);
        opened = Capture(view);
        return (view, warning);
    }

    /// <summary>
    /// Writes the project. With <paramref name="discardUnsaved"/> (closing without saving the map), the view and lane
    /// layout are kept but the map's edits that only live here go back to how they were when it was opened.
    /// </summary>
    /// <exception cref="IOException">The project file couldn't be written.</exception>
    public void Save(ProjectView view, bool discardUnsaved = false)
    {
        var data = Capture(view);
        if (discardUnsaved && opened is { } baseline)
            data = data with { Unanchored = baseline.Unanchored, HsMode = baseline.HsMode, SoundSets = baseline.SoundSets, BookmarkLabels = baseline.BookmarkLabels };
        ProjectStore.Save(root, document.Path, data);
    }

    // Saved export settings always pair samples; unreadable ones are replaced by the defaults.
    private static ExportMergeSettings ExportSettings(ExportMergeSettings? saved, ExportStyle defaultStyle)
    {
        var settings = saved ?? new ExportMergeSettings { Style = defaultStyle };
        try { settings.Validate(); }
        catch (FormatException) { settings = new ExportMergeSettings { Style = defaultStyle }; }
        settings.PairSamples = true;
        return settings;
    }

    private ProjectData Capture(ProjectView view)
    {
        var (names, order, added, hidden) = model.LaneLayout();
        return new ProjectData
        {
            BookmarkLabels = Session.State.BookmarkLabels.ToDictionary(),
            Unanchored = [.. Session.Unanchored.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time) { Volume = Session.State.LayerVolumes.TryGetValue(h, out int volume) ? volume : null })],
            LaneNames = names,
            LaneSlots = model.SlotLayout(),
            DrumRenders = model.DrumRenders.ToDictionary(),
            LaneOrder = order,
            ActiveLane = model.ActiveLane is { } active ? ProjectStore.KeyOf(active) : null,
            AddedLanes = added,
            HiddenLanes = hidden,
            CollapsedFamilies = [.. model.CollapsedFamilies],
            HsMode = document.HsProject,
            ExportSettings = view.ExportSettings,
            SoundSets = Session.CaptureSoundSets(document.HsProject.FileHash),
            LocatorLeft = view.LocatorLeft,
            LocatorRight = view.LocatorRight,
            Cycle = view.Cycle,
            PreviewMuted = [.. model.PreviewMutedHits.Select(h => new ProjectHit(ProjectStore.KeyOf(h.Lane), h.Time))],
            SongOffset = view.SongOffset,
            TimeSignatures = [.. model.TimeSignatures],
        };
    }
}
