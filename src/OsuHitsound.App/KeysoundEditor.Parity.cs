using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>Where Play starts, as the Hitsound editor's Playback → Start playing from.</summary>
internal enum PlayStart { Cursor, LeftLocator, Selection, SelectionOrLeft }

/// <summary>
/// The parts the key editor shares with the Hitsound editor's look and behaviour: the ribbon's snap controls and tools,
/// the tool picker, the scrollbars with zoom ends, the overview strip, the velocity controller, the Selection and
/// Bookmarks cards, and the window-width steps. Keysound's own features live in the other partial files.
/// </summary>
public partial class KeysoundEditor
{
    private const double ControllerLane = 126, ScrollRow = 20, ControllerTitle = 34;
    private bool syncingScroll;
    private RibbonFit? ribbonFit;
    private List<double> bookmarkTimes = [];
    private bool fillingBookmarks;
    private bool narrowLayout, wideInspectorOpen = true, narrowInspectorOpen;

    /// <summary>Open (Ctrl+O) and Save project in the ribbon: the window runs them.</summary>
    public event Action? OpenRequested, SaveRequested;
    /// <summary>A setting the window remembers for all projects changed (insert velocity, audition, snap mode, triplets, controller).</summary>
    public event Action? ViewSettingsChanged;

    internal PlayStart StartFrom { get; set; }

    /// <summary>The settings remembered for all projects, as the Hitsound editor remembers its own.</summary>
    internal (int InsertVelocity, bool Audition, SnapMode SnapMode, bool Triplets, bool Controller) ViewSettings
    {
        get => (Roll.InsertVelocity, HearToggle.IsChecked == true, Roll.SnapMode, TripletSnapToggle.IsChecked == true, VelocityToggle.IsChecked == true);
        set
        {
            InsertVelocityBox.Value = Math.Clamp(value.InsertVelocity, 1, 127);
            HearToggle.IsChecked = value.Audition;
            SnapModeBox.SelectedIndex = (int)value.SnapMode;
            TripletSnapToggle.IsChecked = value.Triplets;
            VelocityToggle.IsChecked = value.Controller;
        }
    }

    private void SetupParity()
    {
        // Snap: the Hitsound editor's spacings, modes and Alt+wheel cycle.
        SnapBox.ItemsSource = MainWindow.SnapOptions.Select(s => s.ToString()).ToList();
        SnapBox.SelectedIndex = Array.IndexOf(MainWindow.SnapOptions, new SnapSpacing(1, 4));
        SnapBox.SelectionChanged += (_, _) =>
        {
            Roll.Spacing = MainWindow.SnapOptions[Math.Max(0, SnapBox.SelectedIndex)];
            Roll.InvalidateVisual();
            UpdateTransport();
        };
        Roll.DivisorWheel += step => SnapBox.SelectedIndex = Array.IndexOf(MainWindow.SnapOptions,
            MainWindow.NextWheelSnap(Roll.Spacing, step, TripletSnapToggle.IsChecked == true));
        SnapModeBox.ItemsSource = new[] { "Grid", "Grid relative", "Objects" };
        SnapModeBox.SelectedIndex = 0;
        SnapModeBox.SelectionChanged += (_, _) =>
        {
            Roll.SnapMode = SnapModeBox.SelectedIndex is >= 0 and <= 2 ? (SnapMode)SnapModeBox.SelectedIndex : SnapMode.Grid;
            ViewSettingsChanged?.Invoke();
        };
        TripletSnapToggle.IsCheckedChanged += (_, _) => ViewSettingsChanged?.Invoke();
        HearToggle.IsCheckedChanged += (_, _) => ViewSettingsChanged?.Invoke();
        InsertVelocityBox.ValueChanged += (_, _) => ViewSettingsChanged?.Invoke();

        // The tools the Hitsound editor has too, and its right-click picker.
        MuteTool.Click += (_, _) => SetTool(KeyTool.Mute);
        RampTool.Click += (_, _) => SetTool(KeyTool.Ramp);
        Roll.MuteChanged += () => { RefreshPlayback(); Roll.InvalidateVisual(); };
        Roll.ToolPickerRequested += ShowToolPicker;

        OpenButton.Click += (_, _) => OpenRequested?.Invoke();
        SaveButton.Click += (_, _) => SaveRequested?.Invoke();
        ImportMidiButton.Click += async (_, _) => await RunCommand("importMidi");
        SelectionQuantizeButton.Click += async (_, _) => await RunCommand("quantizeDialog");
        SelectionDuplicateButton.Click += async (_, _) => await RunCommand("duplicate");
        SelectionDeleteButton.Click += async (_, _) => await RunCommand("delete");

        // Scrollbars with Cubase-style zoom ends, as the Hitsound editor's.
        TimeZoomInButton.Click += (_, _) => Roll.ZoomTime(1.25);
        TimeZoomOutButton.Click += (_, _) => Roll.ZoomTime(0.8);
        RowZoomInButton.Click += (_, _) => Roll.ZoomRows(1.15);
        RowZoomOutButton.Click += (_, _) => Roll.ZoomRows(1 / 1.15);
        RowHeightSlider.Minimum = PianoRollView.MinRow;
        RowHeightSlider.Maximum = PianoRollView.MaxRow;
        RowHeightSlider.ValueChanged += (_, e) => { if (!syncingScroll) Roll.SetRowHeight(e.NewValue); };
        HScroll.Scroll += (_, _) => { if (!syncingScroll) Roll.Axis.Left = HScroll.Value; };
        VScroll.Scroll += (_, _) => { if (!syncingScroll) Roll.TopPitch = KeyNote.MaxPitch + 1 - VScroll.Value; };
        Roll.ScrollChanged += () =>
        {
            SyncScrollBars();
            Overview.InvalidateViewport();
        };

        // The velocity controller, as the Hitsound editor's volume controller.
        VelocityToggle.IsCheckedChanged += (_, _) => { ShowController(VelocityToggle.IsChecked == true); ViewSettingsChanged?.Invoke(); };
        RampShapeBox.ItemsSource = new[] { "Linear", "Ease in", "Ease out", "S-curve" };
        RampShapeBox.SelectedIndex = 0;
        RampShapeBox.SelectionChanged += (_, _) => Roll.RampShape = (RampShape)Math.Max(0, RampShapeBox.SelectedIndex);
        VelocitySelectButton.Click += (_, _) => SelectVelocityRange();
        foreach (var box in new[] { VelocityMinBox, VelocityMaxBox })
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SelectVelocityRange(); e.Handled = true; } };
        ShowController(true);

        // Bookmarks card.
        AddBookmarkButton.Click += (_, _) => AddBookmark();
        RemoveBookmarkButton.Click += (_, _) =>
        {
            if (BookmarkList.SelectedIndex is int i and >= 0 && i < bookmarkTimes.Count) doc?.Session.RemoveBookmarkNear(bookmarkTimes[i], 1);
        };
        BookmarkList.SelectionChanged += (_, _) =>
        {
            bool has = BookmarkList.SelectedIndex is int i && i >= 0 && i < bookmarkTimes.Count;
            BookmarkNameBox.IsVisible = BookmarkLabelButton.IsVisible = has;
            if (!has) return;
            BookmarkNameBox.Text = doc?.Session.NearestBookmark(bookmarkTimes[BookmarkList.SelectedIndex], 1)?.Label ?? "";
            if (!fillingBookmarks) Seek(bookmarkTimes[BookmarkList.SelectedIndex]);
        };
        BookmarkLabelButton.Click += (_, _) => RenameSelectedBookmark();
        BookmarkNameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            RenameSelectedBookmark();
            e.Handled = true;
        };
    }

    private void ShowToolPicker()
    {
        (string, string, KeyTool)[] tools =
        [
            ("MousePointer2", "Select", KeyTool.Select), ("Hand", "Grab", KeyTool.Grab), ("Drumstick", "Draw", KeyTool.Draw),
            ("Eraser", "Erase", KeyTool.Erase), ("VolumeX", "Mute", KeyTool.Mute), ("TrendingUp", "Ramp", KeyTool.Ramp),
            ("Scissors", "Split", KeyTool.Split), ("Link2", "Glue", KeyTool.Glue),
        ];
        ToolPickerFlyout = ToolPicker.Show(Roll, tools.Select(t => (t.Item1, t.Item2, Roll.Tool == t.Item3, (Action)(() => SetTool(t.Item3)))));
    }

    /// <summary>The open (or last) tool picker; the headless checks read it.</summary>
    internal Flyout? ToolPickerFlyout { get; private set; }

    /// <summary>The scrollbars and zoom ends show the roll's view (time, key rows).</summary>
    private void SyncScrollBars()
    {
        syncingScroll = true;
        var axis = Roll.Axis;
        HScroll.Minimum = -2000;
        HScroll.Maximum = Math.Max(axis.End, 0);
        HScroll.ViewportSize = axis.VisibleDuration;
        HScroll.Value = Math.Clamp(axis.Left, HScroll.Minimum, HScroll.Maximum);
        double visible = Roll.RowHeight > 0 ? Roll.GridHeight / Roll.RowHeight : 1;
        VScroll.Minimum = 0;
        VScroll.Maximum = Math.Max(0, KeyNote.MaxPitch + 2 - visible);
        VScroll.ViewportSize = visible;
        VScroll.Value = Math.Clamp(KeyNote.MaxPitch + 1 - Roll.TopPitch, VScroll.Minimum, VScroll.Maximum);
        RowHeightSlider.Value = Roll.RowHeight;
        syncingScroll = false;
        UpdateZoomEnds();
    }

    private void UpdateZoomEnds()
    {
        showingZoom = true;
        ZoomSlider.Value = TimeAxis.SliderFromZoom(Roll.Axis.Zoom);
        showingZoom = false;
    }

    /// <summary>The velocity controller under the roll, shown or hidden (the ribbon's Controller).</summary>
    private void ShowController(bool on)
    {
        Roll.VelocityLaneHeight = on ? ControllerLane : 0;
        Roll.BottomReserve = ScrollRow + (on ? ControllerTitle : 0);
        ControllerHeader.IsVisible = on;
        VelocitySpace.Height = on ? ControllerLane : 0;
        TypingPanel.Margin = new Thickness(0, 0, 0, Roll.BottomReserve + Roll.VelocityLaneHeight);
        Roll.Axis.Width = Math.Max(0, Roll.Bounds.Width - PianoRollView.KeyboardWidth);
        Roll.InvalidateVisual();
        SyncScrollBars();
    }

    /// <summary>The controller's Select: the selected track's notes whose velocity is in the typed range.</summary>
    private void SelectVelocityRange()
    {
        int min = int.TryParse(VelocityMinBox.Text?.Trim(), out var a) ? a : KeyNote.MinVelocity;
        int max = int.TryParse(VelocityMaxBox.Text?.Trim(), out var b) ? b : KeyNote.MaxVelocity;
        if (min > max) (min, max) = (max, min);
        int count = Roll.SelectWhere(n => n.Velocity >= min && n.Velocity <= max);
        SetStatus(count == 0 ? $"No notes with velocity {min}–{max} on this track." : $"Selected {count} note{(count == 1 ? "" : "s")} with velocity {min}–{max}.");
        Roll.Focus();
    }

    /// <summary>The overview strip shows every track's notes, one band per track, and the visible range.</summary>
    private void AttachOverview()
    {
        if (doc is not { } project)
        {
            Overview.Attach(null, null, 1, Roll.Axis, 1);
            return;
        }

        Overview.Attach(() => project.Session.State, scale =>
        {
            var tracks = project.Session.State.Tracks;
            return tracks.SelectMany((t, band) => t.Notes.Select(n => ((int)(project.Tempo.TimeAt(n.Start) * scale), band)));
        }, Math.Max(1, project.Session.State.Tracks.Count), Roll.Axis, Roll.Axis.End);
    }

    /// <summary>The controller's title row names the track the velocity lane shows.</summary>
    private void UpdateControllerText()
    {
        var track = doc?.SelectedTrack is { } id ? doc.Session.State.Track(id) : null;
        int selected = Roll.Selected.Count;
        ActiveTrackText.Text = track is null ? "No track" : selected > 0 ? $"{track.Name} · {selected} selected" : track.Name;
    }

    // ---- Bookmarks ----

    private void AddBookmark() => doc?.Session.AddBookmark(Roll.Playhead);

    private void RenameSelectedBookmark()
    {
        if (BookmarkList.SelectedIndex is int i && i >= 0 && i < bookmarkTimes.Count)
            doc?.Session.RenameBookmark(bookmarkTimes[i], BookmarkNameBox.Text?.Trim() ?? "");
    }

    /// <summary>[ and ]: the playhead to the previous or next bookmark.</summary>
    private void JumpToBookmark(int direction)
    {
        if (doc is null) return;
        var times = doc.Session.State.Bookmarks.Select(b => b.Time).ToList();
        double now = Roll.Playhead;
        double? target = direction < 0 ? times.Where(t => t < now - 1).Cast<double?>().LastOrDefault() : times.Where(t => t > now + 1).Cast<double?>().FirstOrDefault();
        if (target is { } t) Seek(t);
    }

    /// <summary>The Bookmarks card, as the Hitsound one: "label · bar.beat   time", the selected one kept selected.</summary>
    private void RefreshBookmarks()
    {
        var marks = doc?.Session.State.Bookmarks ?? [];
        var items = marks.Select(Label).ToList();
        if (marks.Select(b => b.Time).SequenceEqual(bookmarkTimes) && items.SequenceEqual(BookmarkList.Items.OfType<string>())) return;
        double? selected = BookmarkList.SelectedIndex is int i && i >= 0 && i < bookmarkTimes.Count ? bookmarkTimes[i] : null;
        fillingBookmarks = true;
        bookmarkTimes = [.. marks.Select(b => b.Time)];
        BookmarkList.ItemsSource = items;
        BookmarkList.SelectedIndex = selected is { } t ? bookmarkTimes.IndexOf(t) : -1;
        fillingBookmarks = false;

        string Label(KeyBookmark b)
        {
            var (bar, beat) = doc!.Tempo.BarBeatAt(b.Time);
            string time = TimeSpan.FromMilliseconds(Math.Max(0, b.Time)).ToString(@"m\:ss\.fff");
            return (b.Label.Length > 0 ? b.Label + " · " : "") + $"{bar}.{beat}   {time}";
        }
    }

    // ---- Window width ----

    /// <summary>
    /// The window's width changed: the same steps as the Hitsound editor's (ribbon labels, transport spacing, speed
    /// buttons in two rows, the clock's columns and the L/R button).
    /// </summary>
    public void FitWidth(double width)
    {
        (ribbonFit ??= new RibbonFit(ControlBar, ControlBarContent, RibbonGroups, SaveButton)).Update(width);
        // Below 1100 px the inspector closes, as the Hitsound one does; each width keeps its own open or closed choice.
        bool narrow = width < 1100;
        if (narrow != narrowLayout)
        {
            if (narrow) wideInspectorOpen = InspectorToggle.IsChecked == true;
            else narrowInspectorOpen = InspectorToggle.IsChecked == true;
            narrowLayout = narrow;
            InspectorToggle.IsChecked = narrow ? narrowInspectorOpen : wideInspectorOpen;
        }

        MixControls.Width = width < 1000 ? 268 : 308;
        bool tightTransport = width < 1100;
        LocatorsButton.IsVisible = width >= MainWindow.LocatorsMinWidth;
        TransportLeft.Spacing = TransportButtons.Spacing = PlaybackOptionsPanel.Spacing = tightTransport ? 2 : 4;
        if (LcdColumns.ColumnDefinitions[0].Width.Value != (width < 1000 ? 84 : 96))
            LcdColumns.ColumnDefinitions = new ColumnDefinitions(width < 1000 ? "84,66,44,46" : "96,80,48,70");
        SongControls.ColumnDefinitions[4].Width = new GridLength(width < 1000 ? 0 : 32);
        bool compactSpeed = width < 1400;
        SpeedPanel.ItemWidth = tightTransport ? 30 : compactSpeed ? 38 : 42;
        SpeedPanel.ItemHeight = compactSpeed ? 24 : 28;
        SpeedPanel.Width = SpeedPanel.ItemWidth * (compactSpeed ? 3 : 6);
        SpeedPanel.Height = compactSpeed ? 48 : 28;
        foreach (var button in SpeedPanel.Children.OfType<Avalonia.Controls.Primitives.ToggleButton>())
        {
            button.Width = SpeedPanel.ItemWidth - 2;
            button.Height = button.MinHeight = compactSpeed ? 22 : 28;
            button.FontSize = tightTransport ? 9 : 11;
        }
    }
}
