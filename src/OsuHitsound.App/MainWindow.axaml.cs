using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The main screen: opens a .osu, shows its lanes, plays it and edits its hitsounds.
/// Edits are checked by compiling in the background; Save (Ctrl+S) compiles the original file again, keeps a backup
/// outside the mapset and writes only the .osu.
/// </summary>
public partial class MainWindow : Window
{
    // Beat snap divisors, as in osu!'s editor plus 1/5, 1/7 and 1/9.
    private static readonly int[] Divisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];

    private AudioEngine? audio;
    private string? audioError;
    private Preferences prefs = new();
    private readonly bool usePreferences;
    private (MenuItem Item, string Id)[] menuCommands = [];
    private Lazy<IReadOnlyDictionary<string, byte[]>> skin;
    private readonly DispatcherTimer frameTimer;
    private ArrangeModel? model;
    private bool syncingScroll;
    private InspectorWindow? inspector;
    private double lastDrawnPlayhead = double.NaN;
    private double lastRowHeight = UiTheme.Current.Number("row.height");

    // The open map (path, bytes as opened, folder, decompiled map, edit session); the shortcuts read from it.
    private MapDocument? doc;
    private string? mapPath => doc?.Path;
    private byte[]? originalBytes => doc?.Opened;
    private MapFolder? folder => doc?.Folder;
    private EditSession? session => doc?.Session;
    private CancellationTokenSource? compileCancel;
    private (CompileResult Result, OsuFile File)? lastCompile;
    private bool closeConfirmed;
    private string? flash; // a one-off message (saved, not saved, close warning) shown until the next edit
    private object? drawnHits; // the hit list the notes were last built from

    public MainWindow() : this(null, startAudio: true)
    {
    }

    /// <param name="usePreferences">False for headless snapshots and tests: defaults, and nothing is saved.</param>
    public MainWindow(string? path, bool startAudio = true, bool showInspector = false, bool usePreferences = true)
    {
        InitializeComponent();

        (this.usePreferences, prefs) = (usePreferences, usePreferences ? Preferences.Load(Preferences.DefaultPath()) : new());
        skin = NewSkin();
        if (startAudio) StartAudio();
        ApplyWindowPlacement();

        Lanes.Axis.Changed += OnAxisChanged;
        Lanes.PreviewRequested += lane => audio?.Preview(lane);
        Lanes.SeekRequested += Seek;
        Lanes.HitsPreviewRequested += hits =>
        {
            foreach (var hit in hits) audio?.Preview(hit.Lane, (session?.VolumeOf(hit) ?? 100) / 100f);
        };
        Lanes.SelectionChanged += UpdateStatus;
        Lanes.SizeChanged += (_, _) => SyncScrollBars();
        Lanes.ScrollChanged += () => { if (!syncingScroll) SyncScrollBars(); };

        OpenButton.Click += async (_, _) => await PickMap();
        PlayButton.Click += (_, _) => TogglePlay();
        StopButton.Click += (_, _) => StopPressed();
        StartButton.Click += (_, _) => Seek(0);
        LoopButton.Click += (_, _) => ToggleCycle();
        Lanes.LocatorsChanged += UpdateLoop;
        LaneDetailsToggle.IsCheckedChanged += (_, _) => { Lanes.ShowIndexes = LaneDetailsToggle.IsChecked == true; Lanes.InvalidateVisual(); };
        VolumeToggle.IsCheckedChanged += (_, _) => ApplyTheme();
        VolumeTab.IsCheckedChanged += (_, _) =>
        {
            Volume.IsVisible = VolumeTab.IsChecked == true;
            VolumeControlScroller.IsVisible = Volume.IsVisible;
            SavedText.IsVisible = !Volume.IsVisible;
        };
        SizeChanged += (_, _) => UpdateVolumePanelHeight();
        SnapBox.ItemsSource = Divisors.Select(d => $"1/{d}").ToList();
        SnapBox.SelectedIndex = Array.IndexOf(Divisors, 4);
        SnapBox.SelectionChanged += (_, _) =>
        {
            Lanes.Divisor = Divisors[Math.Max(0, SnapBox.SelectedIndex)];
            Lanes.InvalidateVisual();
            UpdateLcd();
        };
        Lanes.Edits.SnapMode = prefs.SnapMode;
        Lanes.Edits.QuantizeStrength = prefs.QuantizeStrength;
        SnapModeBox.ItemsSource = new[] { "Grid", "Grid relative" };
        SnapModeBox.SelectedIndex = (int)prefs.SnapMode;
        SnapModeBox.SelectionChanged += (_, _) =>
        {
            Lanes.Edits.SnapMode = SnapModeBox.SelectedIndex == 1 ? SnapMode.GridRelative : SnapMode.Grid;
            UpdateStatus();
        };
        Lanes.DivisorWheel += step => SnapBox.SelectedIndex = Math.Clamp(SnapBox.SelectedIndex + step, 0, Divisors.Length - 1);
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
        RangeTool.Click += (_, _) => SetTool(Tool.Range);
        DrawTool.Click += (_, _) => SetTool(Tool.Draw);
        EraseTool.Click += (_, _) => SetTool(Tool.Erase);
        MuteTool.Click += (_, _) => SetTool(Tool.Mute);
        RampTool.Click += (_, _) => SetTool(Tool.Ramp);

        // Playback options.
        foreach (var (item, speed) in new[] { (Speed50Item, 0.5), (Speed75Item, 0.75), (Speed100Item, 1.0), (Speed125Item, 1.25), (Speed150Item, 1.5) })
            item.Click += (_, _) => { if (audio is not null) audio.Speed = speed; UpdateStatus(); };
        SongVolumeSlider.ValueChanged += (_, e) => { if (audio is not null) audio.SongVolume = (float)(e.NewValue / 100); };
        SongMuteButton.IsCheckedChanged += (_, _) => UpdateSongMute();
        SongOffsetBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplySongOffset(); Lanes.Focus(); e.Handled = true; } };
        SongOffsetBox.LostFocus += (_, _) => ApplySongOffset();

        // Volume panel.
        RampShapeBox.ItemsSource = new[] { "Linear", "Ease in", "Ease out", "S-curve" };
        RampShapeBox.SelectedIndex = 0;
        RampShapeBox.SelectionChanged += (_, _) => Volume.Shape = (RampShape)Math.Max(0, RampShapeBox.SelectedIndex);
        VolumeModeBox.ItemsSource = new[] { "Relative", "Set all" };
        VolumeModeBox.SelectedIndex = 1;
        VolumeApplyButton.Click += (_, _) => ApplyVolumeValue();
        VolumeSelectButton.Click += (_, _) => SelectByVolume();
        SaveButton.Click += (_, _) => Save();
        menuCommands =
        [
            (OpenItem, "open"), (ImportOszItem, "importOsz"), (NewDiffItem, "newDiff"), (SaveItem, "save"), (PreferencesItem, "preferences"), (HelpItem, "help"), (InspectorItem, "inspector"),
            (UndoItem, "undo"), (RedoItem, "redo"), (CutItem, "cut"), (CopyItem, "copy"), (PasteItem, "paste"), (DuplicateItem, "duplicate"),
            (DeleteItem, "delete"), (SelectAllItem, "selectAll"), (QuantizeItem, "quantizeDialog"), (RepeatItem, "repeat"), (EditRangeItem, "editRange"), (ScaleVolumeItem, "scaleByVolume"), (ZoomInItem, "zoomIn"), (ZoomOutItem, "zoomOut"), (FilterLanesItem, "filter"),
        ];
        foreach (var (item, id) in menuCommands) item.Click += (_, _) => Run(id);
        ApplyKeys();

        GenerateFileItem.Click += async (_, _) => await Generate();
        RecentItem.SubmenuOpened += (_, _) => FillRecent();
        ZoomSlider.ValueChanged += (_, e) => { if (!syncingScroll) Lanes.Axis.Zoom = ZoomFromSlider(e.NewValue); };
        CollapseAllItem.Click += (_, _) => CollapseAll();
        TimeSignaturesItem.Click += async (_, _) => await EditTimeSignatures();
        ColourLaneItem.Click += (_, _) => SetColourByVolume(false);
        ColourVolumeItem.Click += (_, _) => SetColourByVolume(true);
        SetColourByVolume(prefs.ColourByVolume);
        SetupInspector();
        HScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.Axis.Left = HScroll.Value; };
        VScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.ScrollY = VScroll.Value; SyncScrollBars(); };

        frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => OnFrame());
        frameTimer.Start();

        Closing += (_, e) =>
        {
            // Unsaved edits: the first close only warns; closing again discards them.
            if (session is { IsEdited: true } && !closeConfirmed)
            {
                e.Cancel = true;
                closeConfirmed = true;
                flash = "You have unsaved edits: Ctrl+S saves them, or close again to discard them.";
                UpdateStatus();
            }
        };
        Closed += (_, _) =>
        {
            loadNumber++; // a map still loading is dropped, not shown in a closed window
            compileCancel?.Cancel();
            frameTimer.Stop();
            UiTheme.Current.Changed -= OnThemeChanged;
            inspector?.Close();
            audio?.Dispose();
        };

        // Pick mode: while the inspector's Pick is on, a click selects the part under it instead of acting on it.
        AddHandler(PointerPressedEvent, OnPickPressed, RoutingStrategies.Tunnel);
        UiTheme.Current.Changed += OnThemeChanged;
        WireMapFeatures();
        ApplyTheme();
        if (showInspector) Opened += (_, _) => ToggleInspector();

        UpdateStatus();
        if (path is not null) Open(path);
    }

    private int loadNumber; // a newer Open makes an older one still loading drop its result

    /// <summary>The map loading now (completed when idle), for the headless snapshots.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Opens a map: reading and decoding run off the UI thread. Errors are shown in the status bar, never thrown.</summary>
    public Task Open(string path) => Loading = OpenAsync(path);

    private async Task OpenAsync(string path)
    {
        // The current map's project first: a save still waiting for its timer must not be lost or land on the new map.
        SaveProject();
        int number = ++loadNumber;
        flash = $"Loading {Path.GetFileName(path)}…";
        UpdateStatus();
        var skinSounds = skin;
        try
        {
            var opened = await Task.Run(() =>
            {
                _ = skinSounds.Value; // read the default sounds here too, the first time
                return MapDocument.Load(path);
            });
            if (number != loadNumber) return;
            var (full, file, mapFolder, map, editing) = (opened.Path, opened.File, opened.Folder, opened.Map, opened.Session);

            compileCancel?.Cancel();
            if (session is not null) session.Changed -= OnEdited;
            (doc, lastCompile, closeConfirmed, flash) = (opened, null, false, null);

            // Load every drawable lane's sample, not only the ones the map uses, so new hits sound at once.
            audio?.Load(map with { Lanes = editing.Lanes }, mapFolder, skin.Value);
            model = new ArrangeModel(editing, file, audio?.SilentLanes ?? new HashSet<Lane>());
            if (audio is not null) audio.IsAudible = model.IsAudible;
            editing.Changed += OnEdited;
            Remember(full);
            LoadWaveform();
            RestoreProject();
            model.PreviewMutesChanged += UpdateCues;
            model.PreviewMutesChanged += ScheduleProjectSave;
            model.Changed += UpdateSongMute; // soloing a lane silences the song

            Lanes.SetModel(model);
            Lanes.Axis.End = Math.Max(audio?.Length ?? 0, model.LastTime);
            Lanes.Axis.Left = map.Moments.Count > 0 ? map.Moments[0].Time - 500 : 0;
            Overview.Attach(model, Lanes.Axis, Lanes.Axis.End);
            Volume.Attach(model, Lanes.Axis);
            FileTitle.Text = $"{Path.GetFileName(full)} — Hitsounds";
            Title = $"{Path.GetFileNameWithoutExtension(full)} — osu! Hitsound Editor";
            UpdateLoop();
            SyncScrollBars();
            UpdateLcd();
            UpdateStatus();
            ScheduleCompile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            if (number != loadNumber) return;
            flash = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
            UpdateStatus();
        }
    }

    internal ArrangeView ArrangeArea => Lanes;

    /// <summary>Asks before a command switches to another map while this one has unsaved edits.</summary>
    private async Task<bool> MayLeaveMap() =>
        session is not { IsEdited: true }
        || await Dialogs.Confirm(this, "Unsaved edits", "This map has edits that aren't saved. Open the other map and discard them?", "Discard and open");

    /// <summary>File → Import .osz…: unpacks into a new folder in the Songs folder, then opens a difficulty from it.</summary>
    private async Task ImportOsz()
    {
        if (prefs.SongsFolder is not { } songs || !Directory.Exists(songs))
        {
            flash = "Set the osu! Songs folder in Preferences first: the .osz is unpacked there.";
            UpdateStatus();
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import an .osz",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("osu! beatmap archive") { Patterns = ["*.osz"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } osz) return;

        string folder;
        try
        {
            folder = await Task.Run(() => OszImport.Extract(osz, songs));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            flash = $"Couldn't import {Path.GetFileName(osz)}: {ex.Message}";
            UpdateStatus();
            return;
        }

        var diffs = OszImport.Difficulties(folder);
        string? pick = diffs.Count switch
        {
            0 => null,
            1 => diffs[0],
            _ => await Dialogs.Choose(this, "Open a difficulty", $"Imported into {folder}. Which difficulty?", [.. diffs.Select(Path.GetFileName).OfType<string>()]) is { } name
                ? Path.Combine(folder, name) : null,
        };
        if (pick is not null && await MayLeaveMap()) await Open(pick);
        flash = $"Imported into {folder}. Press F5 in osu!stable's song select to see it.";
        UpdateStatus();
    }

    /// <summary>File → New hitsound diff… (Ctrl+N): an empty difficulty with this map's red lines, then opens it.</summary>
    private async Task NewHitsoundDiff()
    {
        if (doc is null || mapPath is null)
        {
            flash = "Open a map first: the new difficulty takes its timing.";
            UpdateStatus();
            return;
        }

        if (await Dialogs.Prompt(this, "New hitsound diff", "Name of the new difficulty (red lines only, no objects). Not \"Hitsounds\": Generate writes that one.", "Timing") is not { } name) return;
        string path;
        try
        {
            path = HitsoundDiff.CreateEmpty(OsuFile.Read(mapPath), mapPath, name);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            flash = $"Couldn't make the difficulty: {ex.Message}";
            UpdateStatus();
            return;
        }

        if (await MayLeaveMap()) await Open(path);
        flash = $"Made {Path.GetFileName(path)}.";
        UpdateStatus();
    }

    private async Task PickMap()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a map",
            AllowMultiple = false,
            SuggestedStartLocation = prefs.SongsFolder is { } songs ? await StorageProvider.TryGetFolderFromPathAsync(songs) : null,
            FileTypeFilter = [new FilePickerFileType("osu! map") { Patterns = ["*.osu"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } local) await Open(local);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Typing in a text box (volume value, offset…) must not trigger editor shortcuts like 5 (Mute) or Backspace (delete hits).
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            base.OnKeyDown(e);
            return;
        }

        if (EditorCommands.Match(e) is not { } command)
        {
            base.OnKeyDown(e);
            return;
        }

        Run(command.Id);
        e.Handled = true;
    }

    /// <summary>Runs a command from <see cref="EditorCommands"/>, from a key press or a menu item.</summary>
    private void Run(string id)
    {
        switch (id)
        {
            case "open": _ = PickMap(); break;
            case "importOsz": _ = ImportOsz(); break;
            case "newDiff": _ = NewHitsoundDiff(); break;
            case "save": _ = Save(); break;
            case "preferences": _ = ShowPreferences(); break;
            case "help": ShowHelp(); break;
            case "inspector": ToggleInspector(); break;
            case "play": TogglePlay(); break;
            case "start": Seek(0); break;
            case "first": JumpToEnd(first: true); break;
            case "last": JumpToEnd(first: false); break;
            case ['s', 'n', 'a', 'p', var digit] when char.IsAsciiDigit(digit):
                SnapBox.SelectedIndex = Array.IndexOf(Divisors, digit - '0');
                break;
            case "stop": StopPressed(); break;
            case "cycle": ToggleCycle(); break;
            case "locatorsToSelection": LocatorsToSelection(); break;
            case "playLocatorRange": PlayLocatorRange(); break;
            case "jumpLeft": if (Lanes.LocatorLeft is { } l) Seek(l); break;
            case "jumpRight": if (Lanes.LocatorRight is { } r) Seek(r); break;
            case "setLeft": Lanes.SetLocators(Lanes.Playhead, Lanes.LocatorRight ?? Lanes.Playhead); break;
            case "setRight": Lanes.SetLocators(Lanes.LocatorLeft ?? Lanes.Playhead, Lanes.Playhead); break;
            case "toolSelect": SetTool(Tool.Select); break;
            case "toolRange": SetTool(Tool.Range); break;
            case "toolDraw": SetTool(Tool.Draw); break;
            case "toolErase": SetTool(Tool.Erase); break;
            case "toolMute": SetTool(Tool.Mute); break;
            case "toolRamp": SetTool(Tool.Ramp); break;
            case "snap":
                Lanes.SnapOn = !Lanes.SnapOn;
                Lanes.InvalidateVisual();
                UpdateStatus();
                break;
            case "undo": session?.Undo(); break;
            case "redo": session?.Redo(); break;
            case "copy": Lanes.Edits.Copy(); break;
            case "cut": Lanes.Edits.Cut(); break;
            case "paste": Lanes.Edits.Paste(Lanes.Playhead); break;
            case "duplicate": Lanes.Edits.Duplicate(); break;
            case "selectAll": Lanes.Edits.SelectAll(); break;
            case "deselect": Lanes.Edits.Deselect(); break;
            case "quantize": Quantize(Lanes.Edits.QuantizeStrength); break;
            case "quantizeDialog": _ = QuantizeDialog(); break;
            case "repeat": _ = Repeat(); break;
            case "editRange": _ = EditRange(); break;
            case "scaleByVolume": _ = ScaleByVolume(); break;
            case "delete": Lanes.Edits.DeleteSelection(); break;
            case "nudgeLeft": Lanes.Edits.Nudge(-1, fine: false, toGrid: false); break;
            case "nudgeRight": Lanes.Edits.Nudge(1, fine: false, toGrid: false); break;
            case "nudgeLeftFine": Lanes.Edits.Nudge(-1, fine: true, toGrid: false); break;
            case "nudgeRightFine": Lanes.Edits.Nudge(1, fine: true, toGrid: false); break;
            case "gridLeft": Lanes.Edits.Nudge(-1, fine: false, toGrid: true); break;
            case "gridRight": Lanes.Edits.Nudge(1, fine: false, toGrid: true); break;
            case "laneUp": Lanes.Edits.ShiftLanes(-1); break;
            case "laneDown": Lanes.Edits.ShiftLanes(1); break;
            case "zoomIn": Lanes.Axis.ZoomAt(1.25, Lanes.Axis.X(Lanes.Playhead)); break;
            case "zoomOut": Lanes.Axis.ZoomAt(0.8, Lanes.Axis.X(Lanes.Playhead)); break;
            case "filter": ShowFilter(); break;
            case "addBookmark": session?.AddBookmark(Lanes.Playhead); break;
            case "removeBookmark": session?.RemoveBookmarkNear(Lanes.Playhead, 8 / Lanes.Axis.Zoom); break;
            case "previousBookmark": JumpToBookmark(-1); break;
            case "nextBookmark": JumpToBookmark(1); break;
            default: throw new ArgumentException($"Unknown command {id}", nameof(id));
        }
    }

    private (string Id, long At)? lastJump;

    /// <summary>Z / V: to the first / last object; pressed again within 400 ms, to the start / end of the song.</summary>
    private void JumpToEnd(bool first)
    {
        if (session is null) return;
        string id = first ? "first" : "last";
        long now = Environment.TickCount64;
        bool twice = lastJump is { } j && j.Id == id && now - j.At <= 400;
        lastJump = twice ? null : (id, now);
        if (twice) Seek(first ? 0 : Lanes.Axis.End);
        else if (session.Moments.Count > 0) Seek(first ? session.Moments.Min(m => m.Moment.Time) : session.Moments.Max(m => m.Moment.Time)); // a slider end can come after the next object starts
    }

    private void SetTool(Tool tool)
    {
        Lanes.Tool = tool;
        SelectTool.IsChecked = tool == Tool.Select;
        RangeTool.IsChecked = tool == Tool.Range;
        DrawTool.IsChecked = tool == Tool.Draw;
        EraseTool.IsChecked = tool == Tool.Erase;
        MuteTool.IsChecked = tool == Tool.Mute;
        RampTool.IsChecked = tool == Tool.Ramp;
        Volume.RampTool = tool == Tool.Ramp;
        UpdateStatus();
    }

    /// <summary>What playback plays: every hit (less those muted for preview) and the map's ticks and storyboard samples.</summary>
    private void UpdateCues()
    {
        if (model is null || session is null || audio is null) return;
        audio.SetCues(session.State.Hits.Where(h => !model.IsPreviewMuted(h)).Select(h => HitScheduler.HitCue(h.Time, h.Lane, session.VolumeOf(h)))
            .Concat(HitScheduler.PassiveCues(session.Map)));
    }

    private void UpdateSongMute()
    {
        if (audio is not null) audio.SongMuted = SongMuteButton.IsChecked == true || (model?.AnySoloed ?? false);
    }

    private void ApplySongOffset()
    {
        if (audio is null) return;
        if (!OsuNumber.TryParseDouble(SongOffsetBox.Text ?? "", out double offset))
        {
            SongOffsetBox.Text = audio.SongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        audio.SongOffset = offset;
        Lanes.SongOffset = audio.SongOffset;
        ScheduleProjectSave();
        Lanes.InvalidateVisual();
        SongOffsetBox.Text = audio.SongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The value box: Relative moves every selected moment's volume by the value; Set all gives them the value.</summary>
    private void ApplyVolumeValue()
    {
        if (session is null || !int.TryParse(VolumeValueBox.Text, out int value)) return;
        var keys = Lanes.SelectedMoments;
        if (keys.Count == 0)
        {
            flash = "Select some hits first: the value applies to their moments.";
            UpdateStatus();
            return;
        }

        if (VolumeModeBox.SelectedIndex == 0) session.SetVolumes(VolumeRamp.Shift(session.State.Volumes, keys, value));
        else session.SetVolume(keys, Math.Clamp(value, 1, 100));
    }

    private void SelectByVolume()
    {
        if (session is null) return;
        int min = int.TryParse(VolumeMinBox.Text, out int a) ? a : 0, max = int.TryParse(VolumeMaxBox.Text, out int b) ? b : 100;
        var state = session.State;
        Lanes.Edits.SelectWhere(h => h.Anchor is { } k && state.Volumes[k] >= min && state.Volumes[k] <= max);
    }

    /// <summary>View → Time signatures…: edits the editor-only time signature changes (kept in the project, not the map).</summary>
    private async Task EditTimeSignatures()
    {
        if (model is null) return;
        if (await Dialogs.TimeSignatures(this, model.TimeSignatures) is not { } changes) return;
        model.SetTimeSignatures(changes);
        UpdateLcd();
        ScheduleProjectSave();
    }

    /// <summary>The inspector on the left: its saved layout, the show/hide toggle and what its cards do.</summary>
    private void SetupInspector()
    {
        Inspector.SetLayout(prefs.InspectorCards, prefs.InspectorFolded);
        InspectorToggle.IsChecked = prefs.InspectorOpen;
        inspectorWidth = Math.Clamp(prefs.InspectorWidth, 160, 600);
        void Show(bool open)
        {
            if (!open && Root.ColumnDefinitions[0].ActualWidth > 0) inspectorWidth = Root.ColumnDefinitions[0].ActualWidth;
            Root.ColumnDefinitions[0].Width = new GridLength(open ? inspectorWidth : 0);
            Root.ColumnDefinitions[1].Width = new GridLength(open ? 4 : 0);
            Inspector.IsVisible = InspectorSplitter.IsVisible = open;
        }

        Show(prefs.InspectorOpen);
        InspectorToggle.IsCheckedChanged += (_, _) =>
        {
            Show(InspectorToggle.IsChecked == true);
            InspectorPanelItem.IsChecked = InspectorToggle.IsChecked == true;
        };
        InspectorPanelItem.IsChecked = prefs.InspectorOpen;
        InspectorPanelItem.Click += (_, _) => InspectorToggle.IsChecked = InspectorToggle.IsChecked != true;
        Inspector.SeekRequested += Seek;
        Inspector.ShiftTimeRequested += ms => Lanes.Edits.Move(ms, h => h.Lane, "Shift time");
        Inspector.VolumeRequested += v => session?.SetVolume(Lanes.SelectedMoments, v);
        Inspector.AddBookmarkRequested += () => Run("addBookmark");
        Inspector.RemoveBookmarkRequested += t => session?.RemoveBookmarkNear(t, 1);
        Inspector.GoToStepRequested += p => session?.GoTo(p);
    }

    // The inspector's width while shown, kept here so hiding it before closing doesn't lose it.
    private double inspectorWidth = 250;

    /// <summary>The width to save: the panel's when shown, else the one it had.</summary>
    internal double InspectorWidthToSave => InspectorToggle.IsChecked == true && Root.ColumnDefinitions[0].ActualWidth > 0 ? Root.ColumnDefinitions[0].ActualWidth : inspectorWidth;

    private void RefreshInspector() => Inspector.Refresh(session, model, Lanes.Selection, FormatTime);

    /// <summary>View → Colour by: lane colours, or notes from dark (quiet) to bright (loud).</summary>
    private void SetColourByVolume(bool byVolume)
    {
        Lanes.ColourByVolume = byVolume;
        ColourLaneItem.IsChecked = !byVolume;
        ColourVolumeItem.IsChecked = byVolume;
        Lanes.InvalidateVisual();
    }

    /// <summary>Q: quantizes the selected loose hits; says so when there are none.</summary>
    private void Quantize(double strength)
    {
        if (session is null) return;
        if (Lanes.Edits.Quantize(strength) == 0)
        {
            flash = "Quantize moves selected hits that have no circle under them; none are selected.";
            UpdateStatus();
        }
    }

    /// <summary>Edit → Quantize…: asks for the strength, then quantizes.</summary>
    private async Task QuantizeDialog()
    {
        if (session is null) return;
        var text = await Dialogs.Prompt(this, "Quantize", "Strength (1–100 %): how far the selected loose hits move toward the grid",
            Lanes.Edits.QuantizeStrength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (text is not null && OsuHitsound.Format.OsuNumber.TryParseDouble(text, out double strength) && double.IsFinite(strength)) Quantize(strength);
    }

    /// <summary>Ctrl+R: asks how many copies and how many snap steps apart, then repeats the selection.</summary>
    private async Task Repeat()
    {
        if (session is null) return;
        if (Lanes.Edits.Selected.Count == 0)
        {
            flash = "Select some hits to repeat first.";
            UpdateStatus();
            return;
        }

        if (await Dialogs.Repeat(this, Lanes.Edits.DefaultRepeatGap(), Lanes.Divisor) is { } r) Lanes.Edits.Repeat(r.Copies, r.Gap);
    }

    /// <summary>Edit → Edit range: copies, moves or deletes the hits in the range marked with the Range tool.</summary>
    private async Task EditRange()
    {
        if (Lanes.Edits.Range is not { } range)
        {
            flash = "Choose the Range tool (2) and drag a time range first.";
            UpdateStatus();
            return;
        }

        if (await Dialogs.EditRange(this, range.End - range.Start) is { } choice) Lanes.Edits.EditRange(choice.Operation, choice.Offset);
    }

    /// <summary>Edit → Scale by volume: scales the volume of the circles and slider ends in a volume range.</summary>
    private async Task ScaleByVolume()
    {
        if (session is null) return;
        var edits = Lanes.Edits;
        if (await Dialogs.ScaleByVolume(this, edits.Selected.Count > 0, (scope, from, to) => edits.MomentsToScale(scope, from, to).Count) is { } c)
            edits.ScaleByVolume(c.Scope, c.From, c.To, c.Percent);
    }

    /// <summary>
    /// P: the locators span the range marked with the Range tool, else the selection (its first to last hit), else the
    /// visible range.
    /// </summary>
    private void LocatorsToSelection()
    {
        if (Lanes.Edits.Range is { } range)
            Lanes.SetLocators(range.Start, range.End);
        else if (Lanes.Selection.Count > 0)
            Lanes.SetLocators(Lanes.Selection.Min(h => h.Time), Lanes.Selection.Max(h => h.Time));
        else
            Lanes.SetLocators(Lanes.Axis.Left, Lanes.Axis.Left + Lanes.Axis.VisibleDuration);
    }


    private CancellationTokenSource? waveCancel;

    /// <summary>Decodes the song's waveform in the background; the lane fills in when it's ready.</summary>
    private void LoadWaveform()
    {
        waveCancel?.Cancel();
        Lanes.Wave = null;
        Lanes.SongOffset = audio?.SongOffset ?? 0;
        if (audio?.SongBytes is not { } song) return;
        var cancel = waveCancel = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            var wave = Waveform.Decode(song, cancel: cancel.Token);
            Dispatcher.UIThread.Post(() =>
            {
                if (cancel.IsCancellationRequested) return;
                Lanes.Wave = wave;
                Lanes.InvalidateVisual();
            });
        }, cancel.Token);
    }

    /// <summary>[ and ]: the previous or next bookmark from the playhead.</summary>
    private void JumpToBookmark(int direction)
    {
        if (session?.AdjacentBookmark(Lanes.Playhead, direction) is { } t) Seek(t);
    }

    private void ToggleCycle()
    {
        if (Lanes.LocatorLeft is null) LocatorsToSelection();
        Lanes.Cycle = !Lanes.Cycle;
    }

    /// <summary>Alt+P: locators to the selection, cycle on, and play from the left locator (ignoring "Start from").</summary>
    private void PlayLocatorRange()
    {
        LocatorsToSelection();
        if (audio is null || Lanes.LocatorLeft is not { } start) return;
        Lanes.Cycle = true;
        audio.Seek(start);
        playStartedAt = start;
        if (!audio.IsPlaying) audio.Play();
        PlayButton.Content = "⏸";
    }

    /// <summary>After any edit: redraw from the session, play what's wanted now, and check it in the background.</summary>
    private void OnEdited()
    {
        if (model is null || session is null) return;
        if (!ReferenceEquals(session.State.Hits, drawnHits))
        {
            // Volume-only edits (bar drags, ramps) skip this: the notes and the selection only depend on the hits.
            drawnHits = session.State.Hits;
            Lanes.Edits.Prune();
            model.RebuildNotes();
            if (model.HasHiddenLanes) model.RefreshRows(); // a deleted lane whose hits came back (undo) shows again
        }
        else Lanes.InvalidateVisual();

        UpdateCues();
        Title = $"{(session.IsEdited ? "• " : "")}{Path.GetFileNameWithoutExtension(mapPath)} — osu! Hitsound Editor";
        closeConfirmed = false;
        flash = null;
        UpdateStatus();
        ScheduleCompile();
        ScheduleProjectSave();
    }

    /// <summary>Compiles the wanted sounds on a copy of the original in the background, 150 ms after the last edit.</summary>
    private void ScheduleCompile()
    {
        if (session is null || originalBytes is null || folder is null) return;
        compileCancel?.Cancel();
        var cancel = compileCancel = new CancellationTokenSource();
        var state = session.State; // immutable: the wish list is built off the UI thread
        var (document, editing) = (doc!, session);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150, cancel.Token);
            var (result, copy) = document.Compile(state);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancel.IsCancellationRequested || !ReferenceEquals(editing, session) || model is null) return;
                lastCompile = (result, copy);
                model.SetConflicts(result.Conflicts.Select(c => c.Moment).ToHashSet());
                UpdateStatus();
            });
        }, cancel.Token).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Saves the edits: compiles a fresh copy of the map as opened, refuses if the file changed on disk since, keeps a
    /// timestamped backup in the editor's app-data folder (never in the mapset), writes the .osu and reloads it.
    /// </summary>
    private async Task Save()
    {
        if (session is null || mapPath is null || originalBytes is null || folder is null) return;
        try
        {
            int unanchored = session.Unanchored.Count();
            var saved = doc!.Save(MapSaver.DefaultBackupRoot());
            var result = saved.Compile;
            if (!saved.Written)
            {
                flash = "Nothing to save: the map already plays like this." + Problems(result.Conflicts.Count, unanchored);
            }
            else
            {
                // Reload so the editor starts again from what is now on disk (and its undo history starts fresh).
                double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom;
                await Open(mapPath); // awaited: the reload would otherwise clear this message and the view when it finishes
                (Lanes.Axis.Zoom, Lanes.Axis.Left) = (zoom, left);
                flash = string.Join(' ', [
                    $"Saved. {result.TimingPointsAdded} green line{(result.TimingPointsAdded == 1 ? "" : "s")} added, {result.TimingPointsChanged} changed."
                        + Problems(result.Conflicts.Count, unanchored),
                    .. result.Notes, $"Backup: {saved.Backup}"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            flash = $"Not saved: {ex.Message}";
        }

        UpdateStatus();

        static string Problems(int conflicts, int unanchored) =>
            (conflicts > 0 ? $" {conflicts} moment{(conflicts == 1 ? "" : "s")} kept their old sounds (conflicts)." : "")
            + (unanchored > 0 ? $" {unanchored} hit{(unanchored == 1 ? "" : "s")} with no object weren't saved." : "");
    }

    /// <summary>Opens the UI inspector beside the editor, owned by it so it stays in front; closes it if open.</summary>
    public void ToggleInspector()
    {
        if (inspector is not null)
        {
            inspector.Close();
            return;
        }

        inspector = new InspectorWindow(UiTheme.Current, UiTheme.DefaultPath());
        inspector.Closed += (_, _) => inspector = null;
        var screen = Screens.ScreenFromWindow(this)?.WorkingArea;
        if (screen is { } area)
        {
            inspector.WindowStartupLocation = WindowStartupLocation.Manual;
            inspector.Position = new Avalonia.PixelPoint(area.Right - (int)(inspector.Width * RenderScaling) - 8, area.Y + 40);
        }

        inspector.Show(this);
    }

    private void OnPickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (inspector is not { IsPicking: true }) return;
        inspector.Select(PartAt(e));
        e.Handled = true;
    }

    /// <summary>Which inspector part a click landed on: the named bars and controls, or the drawn part of the grid.</summary>
    private string PartAt(PointerPressedEventArgs e)
    {
        for (var v = e.Source as Avalonia.Visual; v is not null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
        {
            if (ReferenceEquals(v, Lanes)) return Lanes.PartAt(e.GetPosition(Lanes));
            if (ReferenceEquals(v, PlayButton)) return "playbutton";
            if (ReferenceEquals(v, Lcd)) return "lcd";
            if (v is Button or Avalonia.Controls.Primitives.ToggleButton && !ReferenceEquals(v, PlayButton)) return "button";
            if (ReferenceEquals(v, TitleBar)) return "titlebar";
            if (ReferenceEquals(v, ControlBar)) return "controlbar";
            if (ReferenceEquals(v, EditBar)) return "editbar";
            if (ReferenceEquals(v, StatusBar)) return "statusbar";
            if (ReferenceEquals(v, Overview)) return "overview";
            if (ReferenceEquals(v, BottomPanel)) return "volume";
        }

        return "arrange";
    }

    private void OnThemeChanged()
    {
        // A new row height in the theme replaces a Ctrl+Alt+wheel zoom.
        double rowHeight = UiTheme.Current.Number("row.height");
        if (rowHeight != lastRowHeight) Lanes.FollowThemeRowHeight();
        lastRowHeight = rowHeight;
        ApplyTheme();
    }

    /// <summary>Applies the theme to the window's bars and controls and redraws the drawn parts.</summary>
    private void ApplyTheme()
    {
        var t = UiTheme.Current;
        Avalonia.Media.IBrush B(string key) => Paint.Brush(t.Color(key));

        Resources["ChromeBg"] = B("control.bg");
        Resources["ChromeBorder"] = B("chrome.border");
        Resources["ControlHover"] = Paint.Brush(Paint.Brighten(t.Color("button.bg"), 0.12));
        Resources["ButtonBg"] = B("button.bg");
        Resources["ButtonText"] = B("button.text");
        Resources["ButtonActive"] = B("button.active");
        Resources["PanelActive"] = Paint.Brush(Paint.Brighten(t.Color("button.bg"), 0.08));
        Resources["ButtonFontSize"] = t.Number("button.fontSize");
        Resources["LcdBg"] = B("lcd.bg");
        Resources["LcdText"] = B("lcd.text");
        Resources["LcdCaption"] = B("lcd.caption");
        Resources["LcdFontSize"] = t.Number("lcd.fontSize");
        Resources["PlayIcon"] = B("play.icon");
        Resources["VolumeSide"] = B("volume.side");

        TitleBar.Background = B("title.bg");
        ControlBar.Background = B("control.bg");
        EditBar.Background = B("edit.bg");
        StatusBar.Background = B("status.bg");
        FileTitle.Foreground = B("title.text");
        FileTitle.FontSize = t.Number("title.fontSize");
        StatusLeft.Foreground = StatusRight.Foreground = B("status.text");
        StatusLeft.FontSize = StatusRight.FontSize = t.Number("status.fontSize");

        OpenButton.Content = t.Get("label.open");
        SaveButton.Content = t.Get("label.save");
        LaneDetailsToggle.Content = t.Get("label.laneDetails");
        VolumeToggle.Content = t.Get("label.volume");

        var rows = Root.RowDefinitions;
        rows[0].Height = new GridLength(t.Number("title.height"));
        // These rows contain fixed-height controls; their theme minimums (UiTheme) keep inspector edits from clipping them.
        rows[1].Height = new GridLength(t.Number("control.height"));
        rows[2].Height = new GridLength(t.Number("edit.height"));
        rows[3].Height = new GridLength(t.Number("overview.height"));
        bool showVolume = VolumeToggle.IsChecked == true;
        BottomPanel.IsVisible = showVolume;
        rows[6].Height = new GridLength(t.Number("status.height"));
        HScroll.Margin = new Avalonia.Thickness(ArrangeView.GridLeft, 0, 0, 0);
        VolumeControlScroller.Width = ArrangeView.GridLeft;
        UpdateVolumePanelHeight();

        Lanes.ScrollY = Lanes.ScrollY;
        Lanes.InvalidateVisual();
        Overview.InvalidateVisual();
        Volume.InvalidateVisual();
        SyncScrollBars();
    }

    private void UpdateVolumePanelHeight()
    {
        var rows = Root.RowDefinitions;
        double chrome = rows[0].Height.Value + rows[1].Height.Value + rows[2].Height.Value
                        + rows[3].Height.Value + rows[6].Height.Value;
        // Keep room for the ruler, waveform and lane rows in shorter windows. The controls scroll if needed.
        double available = Math.Max(100, Bounds.Height - chrome - Lanes.HeaderHeight - 80);
        rows[5].Height = new GridLength(VolumeToggle.IsChecked == true
            ? Math.Min(UiTheme.Current.Number("volume.height"), available) : 0);
    }

    // Where the current playback began, for "Return on stop".
    private double playStartedAt;

    private void TogglePlay()
    {
        if (audio is null || model is null) return;
        if (audio.IsPlaying) StopPlaying();
        else
        {
            // "Start from": the cursor, the left locator, the selection, or the selection falling back to the locator.
            double? selectionStart = Lanes.Selection.Count > 0 ? Lanes.Selection.Min(h => h.Time) : null;
            double? start = StartLeftItem.IsChecked ? Lanes.LocatorLeft
                : StartSelectionItem.IsChecked ? selectionStart
                : StartSelectionOrLeftItem.IsChecked ? selectionStart ?? Lanes.LocatorLeft
                : null;
            if (start is { } s) audio.Seek(s);
            playStartedAt = audio.Position;
            audio.Play();
        }

        PlayButton.Content = audio.IsPlaying ? "⏸" : "▶";
    }

    private void StopPlaying()
    {
        if (audio is null) return;
        audio.Pause();
        if (ReturnOnStopItem.IsChecked) audio.Seek(playStartedAt);
    }

    /// <summary>Cubase's Stop: stops; pressed again while stopped, goes to the start.</summary>
    private void StopPressed()
    {
        if (audio is null) return;
        if (audio.IsPlaying) StopPlaying();
        else audio.Seek(0);
        PlayButton.Content = "▶";
    }

    private void Seek(double time)
    {
        if (audio is not null) audio.Seek(time);
        else Lanes.Playhead = time;
        OnFrame();
    }

    private void UpdateLoop()
    {
        LoopButton.IsChecked = Lanes.Cycle;
        if (audio is null) return;
        audio.Loop = Lanes.Cycle && Lanes.LocatorLeft is { } l && Lanes.LocatorRight is { } r ? (l, r) : null;
    }

    private void CollapseAll()
    {
        if (model is null) return;
        foreach (var group in model.LaneRows.Select(r => r.Group).Distinct())
            if (!model.IsCollapsed(group)) model.ToggleCollapsed(group);
        SyncScrollBars();
    }

    private void OnFrame()
    {
        if (audio is not null)
        {
            Lanes.Playhead = audio.Position;
            if (!audio.IsPlaying && PlayButton.Content as string == "⏸") PlayButton.Content = "▶";

            // Page when the playhead reaches the right edge (or is off screen to the left), as Cubase does.
            if (audio.IsPlaying && FollowItem.IsChecked)
            {
                var axis = Lanes.Axis;
                if (Lanes.Playhead > axis.Left + axis.VisibleDuration * 0.98 || Lanes.Playhead < axis.Left)
                    axis.Left = Lanes.Playhead - axis.VisibleDuration * 0.02;
            }
        }

        // Redraw only when the playhead moved: axis, theme and model changes redraw through their own events.
        if (Lanes.Playhead == lastDrawnPlayhead) return;
        lastDrawnPlayhead = Lanes.Playhead;
        Overview.Playhead = Volume.Playhead = Lanes.Playhead;
        Lanes.InvalidateVisual();
        Overview.InvalidateVisual();
        if (Volume.IsEffectivelyVisible) Volume.InvalidateVisual();
        UpdateLcd();
    }

    private void OnAxisChanged()
    {
        SyncScrollBars();
        Lanes.InvalidateVisual();
        Overview.InvalidateVisual();
        Volume.InvalidateVisual();
    }

    private void SyncScrollBars()
    {
        syncingScroll = true;
        var axis = Lanes.Axis;
        HScroll.Minimum = -2000;
        HScroll.Maximum = Math.Max(axis.End, 0);
        HScroll.ViewportSize = axis.VisibleDuration;
        HScroll.Value = axis.Left;
        double visibleRows = Math.Max(0, Lanes.Bounds.Height - Lanes.HeaderHeight);
        VScroll.Maximum = Math.Max(0, Lanes.ContentHeight - visibleRows);
        VScroll.ViewportSize = visibleRows;
        VScroll.Value = Lanes.ScrollY;
        ZoomSlider.Value = SliderFromZoom(axis.Zoom);
        syncingScroll = false;
    }

    // The zoom slider is logarithmic: equal steps zoom by equal factors.
    private static double ZoomFromSlider(double v) => TimeAxis.MinZoom * Math.Pow(TimeAxis.MaxZoom / TimeAxis.MinZoom, v);

    private static double SliderFromZoom(double zoom) => Math.Log(zoom / TimeAxis.MinZoom) / Math.Log(TimeAxis.MaxZoom / TimeAxis.MinZoom);

    private void UpdateLcd()
    {
        double t = Lanes.Playhead;

        LcdTime.Text = FormatTime(t);
        int divisor = Divisors[Math.Max(0, SnapBox.SelectedIndex)];
        if (model?.RedLineAt(t) is { } red)
        {
            var (bar, beat, tick) = model.BarBeatTick(t);
            LcdBar.Text = $"{bar}.{beat}.{tick}";
            LcdBpm.Text = (60000 / red.BeatLength).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var (n, d) = model.SignatureAt(t);
            LcdMeter.Text = $"{n}/{d} /{divisor}";
        }
        else
        {
            LcdBar.Text = "1.1.1";
            LcdBpm.Text = "—";
            LcdMeter.Text = $"4/4 /{divisor}";
        }
    }

    private void UpdateStatus()
    {
        RefreshInspector();
        if (model is null)
        {
            var open = EditorCommands.Get("open");
            StatusLeft.Text = flash ?? (audioError is null ? $"Open a .osu file ({(open.Keys.Count > 0 ? open.KeysText : "File menu")})." : $"No sound: {audioError}");
            StatusRight.Text = "";
            return;
        }

        var map = model.Map;
        var notes = new List<string>();
        var conflicts = lastCompile?.Result.Conflicts ?? [];
        int unanchored = model.Session.Unanchored.Count();
        if (conflicts.Count > 0)
            notes.Add($"{conflicts.Count} moment{(conflicts.Count == 1 ? "" : "s")} can't be saved as drawn (first at {FormatTime(conflicts[0].Time)}: {conflicts[0].Reason})");
        if (unanchored > 0) notes.Add($"{unanchored} hit{(unanchored == 1 ? "" : "s")} with no object (not saved in Apply mode)");
        if (audioError is not null) notes.Add($"No sound: {audioError}");
        else if (audio is { HasSong: false }) notes.Add($"Song file \"{map.Settings.AudioFilename}\" not found; hitsounds play on their own.");
        int silent = model.Silent.Count(model.Notes.ContainsKey);
        if (silent > 0) notes.Add($"{silent} used lane{(silent == 1 ? "" : "s")} with no sound");
        if (map.Warnings.Count > 0) notes.Add($"{map.Warnings.Count} warning{(map.Warnings.Count == 1 ? "" : "s")}: {map.Warnings[0]}");
        if (notes.Count == 0) notes.Add(model.Session.IsEdited ? "Edited: Ctrl+S saves." : "Everything here can be saved.");
        StatusLeft.Text = flash ?? string.Join("  ·  ", notes);
        ToolTip.SetTip(StatusLeft, string.Join("\n", conflicts.Take(20).Select(c => $"{FormatTime(c.Time)}: {c.Reason}").Concat(map.Warnings.Take(20))));

        Volume.SelectedMoments = Lanes.SelectedMoments;
        Volume.InvalidateVisual();
        string tool = $"{Lanes.Tool}{(Lanes.SnapOn ? $", snap 1/{Lanes.Divisor}{(Lanes.Edits.SnapMode == SnapMode.GridRelative ? " relative" : "")}" : ", snap off")}";
        string selection = Lanes.Selection.Count switch
        {
            0 => "",
            1 => $"  ·  selected: {Describe(Lanes.Selection.First())}",
            int n => $"  ·  {n} hits selected",
        };
        if (Lanes.Edits.Range is { } range) selection += $"  ·  range {Math.Round(range.Start)} → {Math.Round(range.End)} ms";
        StatusRight.Text = $"{tool}  ·  {model.LaneRows.Count} lanes  ·  {model.Session.State.Hits.Count} hits{selection}";
        UpdateSavedTab();
    }

    /// <summary>The "What gets saved" tab: the selected moment's object line (and the timing point it reads) as compiled.</summary>
    private void UpdateSavedTab()
    {
        if (session is null || lastCompile is not { } compiled || Lanes.Selection.FirstOrDefault(h => h.Anchor is not null) is not { Anchor: { } key })
        {
            SavedText.Text = "Select a hit to see the hitSound and hitSample fields (and any green line) its moment saves as.";
            return;
        }

        var moment = session.MomentByKey[key];
        var line = compiled.File.HitObjects[key.ObjectIndex].Line.Text;
        var point = new TimingPointIndex(compiled.File.TimingPoints).SamplePointAt(moment.Time - session.Map.Settings.TimeOffset);
        var conflict = compiled.Result.Conflicts.FirstOrDefault(c => c.Moment == key);
        SavedText.Text = $"{Describe(moment)}\nObject line: {line}\nTiming point it reads: {point?.Line.Text ?? "none"}"
            + (conflict is not null ? $"\nCan't be saved as drawn: {conflict.Reason}" : "");
    }

    private string Describe(EditHit hit) => hit.Anchor is { } key && session?.MomentByKey.GetValueOrDefault(key) is { } m
        ? $"{hit.Lane.Name} on the {Describe(m)}"
        : $"{hit.Lane.Name} at {FormatTime(hit.Time)} (no object)";

    /// <summary>m:ss.mmm, as on the LCD.</summary>
    private static string FormatTime(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private static string Describe(Moment m)
    {
        string where = m.Position switch
        {
            HitPosition.Circle => "circle",
            HitPosition.SliderHead => "slider head",
            HitPosition.SliderRepeat => $"slider repeat {m.Node}",
            HitPosition.SliderTail => "slider tail",
            _ => "spinner end",
        };
        return $"{where} at {FormatTime(m.Time)}";
    }

    private static IReadOnlyDictionary<string, byte[]> LoadSkin(string? preferred)
    {
        try
        {
            return DefaultSkin.FindLazerResources(preferred) is { } dll ? DefaultSkin.LoadSamples(dll) : new Dictionary<string, byte[]>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return new Dictionary<string, byte[]>();
        }
    }
}
