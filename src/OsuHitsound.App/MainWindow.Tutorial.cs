using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>The tutorial: shown once on the first launch, and from File → Tutorial (<see cref="TutorialOverlay"/>).</summary>
public partial class MainWindow
{
    private void SetupTutorial()
    {
        TutorialItem.Click += (_, _) => StartTutorial();
        Tutorial.Finished += () =>
        {
            if (prefs.TutorialSeen) return;
            prefs = prefs with { TutorialSeen = true };
            SavePrefs();
        };
        // The first launch only: once the window is laid out, so every spotlight lands where its control is.
        if (usePreferences && !prefs.TutorialSeen) Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(StartTutorial, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void StartTutorial() => Tutorial.Start(TutorialSteps());

    internal void ShowTutorialStepForSnapshot(int step)
    {
        if (!Tutorial.IsRunning) StartTutorial();
        Tutorial.ShowStep(step);
    }

    internal void CloseTutorialForSnapshot() => Tutorial.Close();

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
            "This short tour points at every part of the screen and says what it's for. Use Next and Back (or the arrow keys); Esc ends the tour. You can see it again any time from File → Tutorial.",
            () => null),
        new("Open a map", "Opens a .osu from your Songs folder (Ctrl+O). Each sample file in the mapset becomes a lane you can draw in. File → Recent maps and File → Import .osz… are in the File menu.",
            () => On(OpenButton)),
        new("Save .osu", "Writes your hitsounds back into the map (Ctrl+S). Only the lines you changed are rewritten, and a backup of the old file is kept outside the mapset.",
            () => On(SaveButton)),
        new("Generate", "Makes a separate hitsound difficulty from your hits, adding circles where a sound has no object, ready for Mapping Tools' Hitsound Copier.",
            () => On(GenerateButton)),
        new("Menus", "Every command lives here too: File (open, import .osz, new hitsound diff, this tutorial), Edit (quantize, repeat, range, scale by volume), Playback, Map and View.",
            () => On(HeaderMenu)),
        new("The open map", "The name of the .osu you are editing.", () => On(FileTitle)),
        new("Preferences", "Your Songs folder, audio output and buffer, and the key commands (including importing your Cubase keys).",
            () => On(PreferencesButton)),
        new("Keys / F1", "A searchable list of every shortcut and mouse action. Press F1 at any time.", () => On(HelpButton)),
        new("Undo and redo", "Steps back and forward through your edits (Ctrl+Z, Ctrl+Y). The Recent edits card in the inspector lists them by name.",
            () => Around(UndoButton, RedoButton)),
        new("Select tool (1)", "Click a hit to select it, Ctrl/Shift+click to add, drag a box around several, and drag selected hits to move them.",
            () => On(SelectTool)),
        new("Range tool (2)", "Drag a time range across the lanes you want, then Edit → Edit range… copies, moves or deletes everything inside it.",
            () => On(RangeTool)),
        new("Draw tool (3)", "Click or drag along a lane to place hits. Starting on a hit erases instead. Holding Alt draws with any tool.",
            () => On(DrawTool)),
        new("Erase tool (4)", "Drag across hits to remove them. Right-click also deletes a hit with any tool.", () => On(EraseTool)),
        new("Mute tool (5)", "Click a hit to silence it while you listen. This is only for listening: osu! can't store a muted sound, so it's never saved.",
            () => On(MuteTool)),
        new("Ramp tool (6)", "Drag in the volume controller at the bottom to draw a smooth volume ramp.", () => On(RampTool)),
        new("Snap", "Turns snapping on or off (J). When on, hits land on the beat grid.", () => On(SnapToggle)),
        new("Snap mode and beat division", "Grid puts dragged hits on grid lines; Grid relative moves them by whole steps and keeps their offset. The divisor (1/4, 1/8…) sets how fine the grid is (Shift+1–9).",
            () => Around(SnapModeBox, SnapBox)),
        new("View toggles", "Show or hide the inspector, each lane's file index (Info) and the volume controller.",
            () => Around(InspectorToggle, LaneDetailsToggle, VolumeToggle)),
        new("Colour", "Colour notes by lane, or by volume: quiet notes dark, loud notes bright, so odd volumes stand out.", () => On(ColourBox)),
        new("Zoom and Fit", "Zoom in and out of time (also Ctrl+wheel). Fit shows the whole map.", () => Around(ZoomSlider, FitButton)),
        new("Selection bar", "What is selected: how many hits, where they start, and which circle or slider end they play on. Type a volume and press Apply to change the selected circles (Relative adds to each, Set all gives them that value).",
            () => On(InfoBarLeft)),
        new("Song", "The song's own volume, mute, and an offset in ms if it sounds early or late. None of this is saved into the map.",
            () => On(SongControls)),
        new("Inspector", "Cards for the selection (shift time, set volume), the map's bookmarks, and Recent edits — click an edit to go back to it. Fold a card by its title; drag ⠿ to reorder.",
            () => On(Inspector)),
        new("Sample lanes", "Every sample you can use, grouped by sampleset. Type in the filter to find one, and press + to add a lane for another sample file.",
            () => InLanes(Lanes.LaneListHeaderRect)),
        new("A lane's buttons", "The icon shows the sound (normal, whistle, finish, clap). + adds a lane, ● plays the sample, M mutes the lane and S solos it while you listen.",
            () => InLanes(Lanes.FirstLaneButtonsRect())),
        new("The grid", "Your hitsounds: one row per sample, in time from left to right. Red-striped columns are sounds that can't be saved as drawn yet.",
            () => InLanes(Lanes.GridRect)),
        new("Volume controller", "One bar per circle or slider end: drag a bar to change its volume, Alt-drag to paint, Shift-drag for a ramp. osu! has one volume per object, so it's shared by all lanes on it.",
            () => On(BottomPanel)),
        new("Transport", "Go to the start, stop, play (Space) and Cycle between the L and R markers (L). Ctrl+click the ruler sets L, Alt+click sets R.",
            () => On(TransportButtons)),
        new("Position", "Where the playhead is: time, bar.beat.tick, tempo, and the time signature with the snap divisor.", () => On(Lcd)),
        new("Global volume", "Turns the song, hitsounds and previews up or down together. It never goes below 20 %.", () => On(GlobalVolumePanel)),
        new("That's everything", "Open a map and start drawing. File → Tutorial shows this tour again whenever you like.", () => null),
    ];
}
