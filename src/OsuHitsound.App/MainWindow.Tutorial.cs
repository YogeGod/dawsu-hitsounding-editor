using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>The tutorial: shown once on the first launch, and from File → Full interface tour (<see cref="TutorialOverlay"/>).</summary>
public partial class MainWindow
{
    private void SetupTutorial()
    {
        TutorialItem.Click += (_, _) => StartTutorial();
        QuickStartItem.Click += (_, _) => StartQuickStart();
        Tutorial.Finished += () =>
        {
            if (prefs.TutorialSeen) return;
            prefs = prefs with { TutorialSeen = true };
            SavePrefs();
        };
        // The first launch only: once the window is laid out, so every spotlight lands where its control is.
        if (usePreferences && !prefs.TutorialSeen) Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(StartQuickStart, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void StartTutorial() => Tutorial.Start(TutorialSteps());

    internal void StartQuickStart()
    {
        HideStartScreen(); // the steps point at the editor under it
        Tutorial.Start(QuickStartSteps(), interactive: true);
    }

    private IReadOnlyList<TutorialStep> QuickStartSteps() =>
    [
        new("Open a difficulty", "Open a .osu or drop a .osu/.osz here. The editor opens the selected file directly; Save .osu updates that difficulty. Set your Songs folder in Preferences to import .osz archives. You can use the editor during this quick start.", () => On(OpenButton)),
        new("Choose a lane", "Click the name of the lane you want to work on. Its sample appears in the inspector's Active lane card. Use the lane filter to find a sample; + adds a numbered layer with a default sound.", () => InLanes(Lanes.LaneListHeaderRect)),
        new("Assign a sample", "In the inspector's Active lane card, choose a sample or Browse sample…. This changes the active lane and its hits, not every selected lane. Use the preview button to hear the sample; Undo restores the previous assignment.", () => On(Inspector)),
        new("Place a hit", "Choose Draw (3), then click in that lane on the timeline. Snap keeps the hit on the beat grid. Select (1) moves hits; Ctrl+Z undoes your last edit.", () => On(DrawTool)),
        new("Listen", "Press Space to play, or use Play below. Audition previews newly drawn hits. Lane M/S and song volume affect listening only; they do not remove saved sounds.", () => On(PlayButton)),
        new("Choose your output", "Save .osu updates the open difficulty. Generate creates a separate [Hitsounds] difficulty, including circles for loose hits. Copy hitsounds applies a source difficulty to chosen difficulties. Use the operation-details icon beside the map title to review results.", () => On(SaveButton)),
        new("Keep working", "The map title shows which difficulty is open. Hover it for its path. The operation-details icon beside it shows results and warnings; File → Open map folder shows your files. File → Full interface tour explains the remaining controls; F1 lists shortcuts. Finish closes this guide.", () => On(FileTitle)),
    ];

    internal void ShowTutorialStepForSnapshot(int step)
    {
        if (!Tutorial.IsRunning) StartTutorial();
        Tutorial.ShowStep(step);
    }

    internal void CloseTutorialForSnapshot() => Tutorial.Close();

    /// <summary>Where a step's spotlight goes, in window coordinates: the README's screenshots are cropped to it.</summary>
    internal Rect? TutorialSpotForSnapshot(int step) => TutorialSteps()[step].Target();

    // A control's place on the overlay, or null when it isn't shown (the step is then centred).
    private Rect? On(Control? c) =>
        c is { IsEffectivelyVisible: true } && c.TranslatePoint(default, Tutorial) is { } p ? new Rect(p, c.Bounds.Size) : null;

    // A rectangle inside the lane view, on the overlay.
    private Rect? InLanes(Rect? r) =>
        r is { } rect && Lanes.TranslatePoint(rect.Position, Tutorial) is { } p ? new Rect(p, rect.Size) : null;

    // Several controls together (a group of buttons).
    private Rect? Around(params Control[] controls)
    {
        Rect? all = null;
        foreach (var c in controls)
            if (On(c) is { } r) all = all is { } a ? a.Union(r) : r;
        return all;
    }

    private IReadOnlyList<TutorialStep> TutorialSteps() =>
    [
        new("Welcome to the osu! hitsound editor",
            "This short tour points at every part of the screen and says what it's for. Use Next and Back (or the arrow keys); Esc ends the tour. You can see it again any time from File → Full interface tour.",
            () => null),
        new("Open a map", "Opens a .osu from your Songs folder (Ctrl+O), or drag a .osu or .osz onto the window. Each sample file in the mapset becomes a lane you can draw in. The File menu also has Recent maps, Import .osz… and Open map folder (Ctrl+Shift+O): sample files you put in that folder show up as lanes.",
            () => On(OpenButton)),
        new("Save .osu", "Writes your hitsounds into the open difficulty (Ctrl+S). Hover the map title for its path. Only changed lines are rewritten; a backup is kept outside the mapset. Generate creates a separate hitsound difficulty; Copy applies sounds to chosen difficulties.",
            () => On(SaveButton)),
        new("Generate", "Makes a separate hitsound difficulty from your hits, adding circles where a sound has no object. Map → Copy hitsounds… then copies it into the other difficulties, like Mapping Tools' Hitsound Copier.",
            () => On(HeaderMenu)),
        new("Menus", "Every command lives here too: File (open, import .osz, new hitsound diff, this tutorial), Edit (quantize, repeat, range, scale by volume), Playback, Map (generate, copy hitsounds into other difficulties) and View.",
            () => On(HeaderMenu)),
        new("The open map", "The name of the .osu you are editing.", () => On(FileTitle)),
        new("Preferences", "Your Songs folder, audio output and buffer, and the key commands.",
            () => On(PreferencesButton)),
        new("Keys / F1", "A searchable list of every shortcut and mouse action. Press F1 at any time.", () => On(HelpButton)),
        new("Undo and redo", "Steps back and forward through your edits (Ctrl+Z, Ctrl+Y). The optional History panel in the inspector lists them by name.",
            () => Around(UndoButton, RedoButton)),
        new("Select tool (1)", "Click a hit to select it, Ctrl/Shift+click to add, drag a box around several, and drag selected hits to move them.",
            () => On(SelectTool)),
        new("Grab tool (2)", "Drag the canvas to pan time and lane rows. Space still plays or stops. View → Range selection tool keeps range editing available.",
            () => On(GrabTool)),
        new("Draw tool (3)", "Click or drag along a lane to place hits. Starting on a hit erases instead. Holding Alt draws with any tool.",
            () => On(DrawTool)),
        new("Erase tool (4)", "Drag across hits to remove them. Right-click also deletes a hit with any tool.", () => On(EraseTool)),
        new("Mute tool (5)", "Click a hit to silence it while you listen. This is only for listening: osu! can't store a muted sound, so it's never saved.",
            () => On(MuteTool)),
        new("Ramp tool (6)", "Drag in the volume controller at the bottom to draw a smooth volume ramp.", () => On(RampTool)),
        new("Snap", "Turns snapping on or off (J). When on, hits land on the beat grid.", () => On(SnapToggle)),
        new("Snap mode and beat division", "Grid puts dragged hits on grid lines; Grid relative moves them by whole steps and keeps their offset. Choose 1/4, 1/8… or 2/1, 3/1, 4/1 for every 2, 3, 4 beats from each red timing point. Shift+1–9 still selects 1/n.",
            () => Around(SnapModeBox, SnapBox)),
        new("View toggles", "The toolbar icons show or hide the inspector, sample indexes, unused lanes, gameplay-object lines and the volume controller. Hover an icon for its name.",
            () => Around(InspectorToggle, ToolbarEnd)),
        new("Colour", "Hits and volume bars show volume from blue through purple to red, so differences stand out.", () => On(BottomPanel)),
        new("Zoom and Fit", "Use the toolbar to zoom in and out of time (also Ctrl+wheel). Fit shows the whole map.", () => On(ToolbarEnd)),
        new("Selection bar", "What is selected: how many hits, where they start, and which circle or slider end they play on. Type a volume and press the checkmark to apply it (Relative adds to each, Set all gives them that value). The up arrow collapses these details; the down arrow expands them.",
            () => On(InfoBarLeft)),
        new("Song", "The song's own volume, mute, and an offset in ms if it sounds early or late. None of this is saved into the map.",
            () => On(SongControls)),
        new("Inspector", "Active lane and Bookmarks stay on the left. Optional bookmark labels appear in the list and on flag hover. View shows History or Sound sets when you need them. Preferences chooses the Classic browser inside the inspector or the Folder tree sidebar. Selecting a file previews; the assignment button or a lane drop replaces its sample.",
            () => On(Inspector)),
        new("Sample lanes", "Every sample you can use in a reorderable lane list: hitnormal, whistle, finish and clap, each with the default sound first and the map's own files under it. Type in the filter to find one. Above it, + adds a lane for a sample file already in the mapset, − deletes the highlighted lane (the one you touched last), and ‹ hides or shows the names.",
            () => InLanes(Lanes.LaneListHeaderRect)),
        new("A lane's buttons", "The icon shows the sound (normal, whistle, finish, clap). + immediately adds the next numbered layer with the default family sound; choose another sample through Active lane, ● plays the sample, M mutes the lane and S solos it while you listen. The Song audio row has M and S too.",
            () => InLanes(Lanes.FirstLaneButtonsRect())),
        new("The grid", "Your hitsounds: one row per sample, in time from left to right. Red-striped columns are sounds that can't be saved as drawn yet.",
            () => InLanes(Lanes.GridRect)),
        new("Song waveform", "The song, coloured by frequency: blue for lows (kicks, bass), purple for mids (snares, vocals), yellow for highs (hi-hats, cymbals), so you can see where each drum hits. M and S on its left mute or solo the song.",
            () => InLanes(Lanes.WaveRect)),
        new("Volume controller", "One bar per circle or slider end: drag a bar to change its volume, Alt-drag to paint, Shift-drag for a ramp. osu! has one volume per object, so it's shared by all lanes on it.",
            () => On(BottomPanel)),
        new("Transport", "The L and R markers: L ◀ / R ◀ jump to them, and typing a time (ms) in their boxes moves them. Then go to the start, stop, play (Space) and Cycle between L and R (L). Ctrl+click the ruler sets L, Alt+click sets R.",
            () => On(TransportButtons)),
        new("Position", "Where the playhead is: time, bar.beat.tick, tempo, and the time signature with the snap divisor.", () => On(Lcd)),
        new("Playback speed", "Separate bottom-bar buttons set speed from 25 % to 150 % without changing pitch. Return-on-stop and follow-playhead stay beside them. L/R locators are directly visible in the toolbar.", () => On(PlaybackOptionsPanel)),
        new("Global volume", "Turns the song, hitsounds and previews up or down together. It never goes below 20 %.", () => On(GlobalVolumePanel)),
        new("That's everything", "Open a map and start drawing. File → Full interface tour shows this tour again whenever you like.", () => null),
    ];
}
