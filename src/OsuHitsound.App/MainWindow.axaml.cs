using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// The main screen: opens a .osu, shows its lanes, plays it and edits its hitsounds.
/// Edits are checked by compiling in the background; Save (Ctrl+S) compiles the original file again, keeps a backup
/// outside the mapset and writes only the .osu.
/// </summary>
public partial class MainWindow : Window
{
    internal static readonly SnapSpacing[] SnapOptions = [new(4, 1), new(3, 1), new(2, 1), .. new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16 }.Select(d => new SnapSpacing(1, d))];
    private static readonly SnapSpacing[] StraightSnaps = [new(4, 1), new(2, 1), new(1, 1), new(1, 2), new(1, 4), new(1, 8), new(1, 16)],
        TripletSnaps = [new(3, 1), new(1, 1), new(1, 3), new(1, 6), new(1, 12)];

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
    private MapCompilation? lastCompile;
    private bool closeConfirmed;
    private string? flashMessage, workflowResult;
    private string? flash
    {
        get => flashMessage;
        set
        {
            flashMessage = value;
            if (Start is { IsVisible: true }) Start.SetMessage(value);
            // Editing clears the compact status, but operation results remain available until dismissed or replaced.
            if (value is not null) workflowResult = value;
        }
    }
    private object? drawnHits; // the hit list the notes were last built from
    private object? drawnHsCircles;
    private object? drawnSlides;

    public MainWindow() : this(null, startAudio: true)
    {
    }

    /// <param name="usePreferences">False for headless snapshots and tests: defaults, and nothing is saved.</param>
    public MainWindow(string? path, bool startAudio = true, bool showInspector = false, bool usePreferences = true)
    {
        InitializeComponent();
        SetupDrumVst();
        SetupHsMode();

        (this.usePreferences, prefs) = (usePreferences, usePreferences ? Preferences.Load(Preferences.DefaultPath()) : new());
        SetupWorkflowFeedback();
        if (usePreferences && prefs.SongsFolder is null) prefs = prefs with { SongsFolder = Preferences.FindSongsFolder() };
        ArrangeView.NamesCollapsed = prefs.LaneNamesCollapsed;
        ArrangeView.UserListWidth = prefs.LaneListWidth is { } listWidth && double.IsFinite(listWidth) ? Math.Clamp(listWidth, 160, 600) : null;
        skin = NewSkin();
        if (startAudio) StartAudio();
        ApplyWindowPlacement();

        Lanes.Axis.Changed += OnAxisChanged;
        Lanes.AuditionOnPlacement = prefs.AuditionOnPlacement;
        Lanes.Edits.ProtectHitnormals = prefs.ProtectHitnormals;
        ProtectHitnormalsItem.IsChecked = prefs.ProtectHitnormals;
        ProtectHitnormalsItem.Click += (_, _) =>
        {
            Lanes.Edits.ProtectHitnormals = ProtectHitnormalsItem.IsChecked;
            prefs = prefs with { ProtectHitnormals = Lanes.Edits.ProtectHitnormals };
            SavePrefs();
        };
        Lanes.InsertVolume = prefs.InsertVolume is { } insertVolume ? Math.Clamp(insertVolume, 0, 100) : null;
        InsertVolumeBox.Value = Lanes.InsertVolume;
        InsertVolumeBox.ValueChanged += (_, _) =>
        {
            Lanes.InsertVolume = InsertVolumeBox.Value is { } value ? (int)Math.Clamp(value, 0, 100) : null;
            prefs = prefs with { InsertVolume = Lanes.InsertVolume };
            SavePrefs();
        };
        AuditionPlacementButton.IsChecked = prefs.AuditionOnPlacement;
        AuditionPlacementButton.IsCheckedChanged += (_, _) =>
        {
            Lanes.AuditionOnPlacement = AuditionPlacementButton.IsChecked == true;
            prefs = prefs with { AuditionOnPlacement = Lanes.AuditionOnPlacement };
            SavePrefs();
        };
        Lanes.PlacementPreviewRequested += hit => audio?.Preview(hit.Lane, (session?.VolumeOf(hit) ?? 100) / 100f);
        Lanes.LaneListPreviewRequested += lane =>
        {
            if (audio is null) return;
            if (drumVstEnabled && (drumKit is not null || drumVst is not null && drumVstReady)) { audio.Preview(lane); return; }
            if (doc is null) { audio.Preview(lane); return; }
            double time = audio.IsPlaying ? audio.Position : Lanes.Playhead;
            var preview = doc.LanePreviewAt(lane, time);
            try
            {
                audio.Preview(preview, doc.Folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                flash = $"Couldn't preview {preview.Name}: {ex.Message}";
                UpdateStatus();
            }
        };
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
        ObjectLinesToggle.IsCheckedChanged += (_, _) => { Lanes.ShowObjectLines = ObjectLinesToggle.IsChecked == true; Lanes.InvalidateVisual(); };
        SkinTextures.LoadAsync(prefs.LazerResources, () => Lanes.InvalidateVisual()); // lazer's hit-circle textures for the strip
        UsedLanesToggle.IsCheckedChanged += (_, _) => { model?.SetUsedLanesOnly(UsedLanesToggle.IsChecked == true); SyncScrollBars(); };
        VolumeToggle.IsCheckedChanged += (_, _) => ApplyTheme();
        VolumeTab.IsCheckedChanged += (_, _) =>
        {
            Volume.IsVisible = VolumeTab.IsChecked == true;
            VolumeControlScroller.IsVisible = Volume.IsVisible;
            SavedPreview.IsVisible = !Volume.IsVisible;
        };
        SizeChanged += (_, _) => { RefreshWorkflowFeedback(); UpdateChromeHeights(); UpdateVolumePanelHeight(); };
        SnapBox.ItemsSource = SnapOptions.Select(s => s.ToString()).ToList();
        SnapBox.SelectedIndex = Array.IndexOf(SnapOptions, new SnapSpacing(1, 4));
        SnapBox.SelectionChanged += (_, _) =>
        {
            Lanes.Spacing = SnapOptions[Math.Max(0, SnapBox.SelectedIndex)];
            Lanes.InvalidateVisual();
            UpdateLcd();
        };
        Lanes.Edits.SnapMode = prefs.SnapMode;
        Lanes.Edits.QuantizeStrength = prefs.QuantizeStrength;
        SnapModeBox.ItemsSource = new[] { "Grid", "Grid relative", "Objects" };
        SnapModeBox.SelectedIndex = (int)prefs.SnapMode;
        SnapModeBox.SelectionChanged += (_, _) =>
        {
            Lanes.Edits.SnapMode = SnapModeBox.SelectedIndex is >= 0 and <= 2 ? (SnapMode)SnapModeBox.SelectedIndex : SnapMode.Grid;
            UpdateStatus();
        };
        Lanes.DivisorWheel += step => SnapBox.SelectedIndex = Array.IndexOf(SnapOptions,
            NextWheelSnap(Lanes.Spacing, step, TripletSnapToggle.IsChecked == true));
        Lanes.PlayheadStepRequested += SeekGridSteps;
        // V6.39 header and toolbar buttons.
        PreferencesButton.Click += (_, _) => Run("preferences");
        HelpButton.Click += (_, _) => Run("help");
        UndoButton.Click += (_, _) => Run("undo");
        RedoButton.Click += (_, _) => Run("redo");
        SnapToggle.IsCheckedChanged += (_, _) =>
        {
            if (Lanes.SnapOn == (SnapToggle.IsChecked == true)) return;
            Lanes.SnapOn = SnapToggle.IsChecked == true;
            Lanes.InvalidateVisual();
            UpdateStatus();
        };
        FitButton.Click += (_, _) => FitTimeline();
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
        GrabTool.Click += (_, _) => SetTool(Tool.Grab);
        DrawTool.Click += (_, _) => SetTool(Tool.Draw);
        EraseTool.Click += (_, _) => SetTool(Tool.Erase);
        MuteTool.Click += (_, _) => SetTool(Tool.Mute);
        RampTool.Click += (_, _) => SetTool(Tool.Ramp);

        // Playback options.
        // Separate visible speed buttons and Playback menu choices share the same action.
        var speedButtons = new List<(Avalonia.Controls.Primitives.ToggleButton Button, double Speed)>();
        var speedItems = new[] { (Speed25Item, 0.25), (Speed50Item, 0.5), (Speed75Item, 0.75), (Speed100Item, 1.0), (Speed125Item, 1.25), (Speed150Item, 1.5) };
        void SetSpeed(double speed)
        {
            if (audio is not null) audio.Speed = speed;
            foreach (var (item, s) in speedItems) item.IsChecked = s == speed;
            foreach (var (button, s) in speedButtons) button.IsChecked = s == speed;
            UpdateStatus();
        }
        foreach (var (item, speed) in speedItems)
        {
            item.Click += (_, _) => SetSpeed(speed);
            var button = new Avalonia.Controls.Primitives.ToggleButton
            {
                Name = $"Speed{speed * 100:0}Button", Content = $"{speed * 100:0}%", IsChecked = speed == 1,
                Width = 40, Height = 28, MinHeight = 28, Padding = new Avalonia.Thickness(2, 0), FontSize = 11,
            };
            ToolTip.SetTip(button, $"Playback speed {speed * 100:0}%, pitch preserved");
            Avalonia.Automation.AutomationProperties.SetName(button, $"Playback speed {speed * 100:0} percent");
            button.Click += (_, _) => SetSpeed(speed);
            speedButtons.Add((button, speed));
            SpeedPanel.Children.Add(button);
        }
        SongVolumeSlider.ValueChanged += (_, e) =>
        {
            if (audio is not null) audio.SongVolume = (float)(e.NewValue / 100);
            SongVolumeText.Text = $"{Math.Round(e.NewValue)}%";
        };
        // Global volume: 20–100 % (the slider can't go lower), applied to everything that plays.
        GlobalVolumeSlider.Value = Math.Clamp(prefs.GlobalVolume, GlobalVolumeSlider.Minimum, GlobalVolumeSlider.Maximum);
        GlobalVolumeText.Text = $"{Math.Round(GlobalVolumeSlider.Value)}%";
        GlobalVolumeSlider.ValueChanged += (_, e) =>
        {
            GlobalVolumeText.Text = $"{Math.Round(e.NewValue)}%";
            if (audio is not null) audio.MasterVolume = (float)(e.NewValue / 100);
        };
        SongMuteButton.IsCheckedChanged += (_, _) =>
        {
            Lanes.SongMuted = SongMuteButton.IsChecked == true;
            Lanes.InvalidateVisual();
            UpdateSongMute();
        };
        Lanes.SongMuteRequested += () => SongMuteButton.IsChecked = SongMuteButton.IsChecked != true;
        // The locator boxes (V6.39): L◀ / R◀ jump to a locator; a typed time in ms sets it on Enter.
        GoLeftButton.Click += (_, _) => { if (Lanes.LocatorLeft is { } at) Seek(at); };
        GoRightButton.Click += (_, _) => { if (Lanes.LocatorRight is { } at) Seek(at); };
        foreach (var box in new[] { LeftLocatorBox, RightLocatorBox })
        {
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyLocatorBoxes(); Lanes.Focus(); e.Handled = true; } };
            box.LostFocus += (_, _) => ApplyLocatorBoxes();
        }

        Lanes.LocatorsChanged += ShowLocators;
        SongOffsetBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplySongOffset(); Lanes.Focus(); e.Handled = true; } };
        SongOffsetBox.LostFocus += (_, _) => ApplySongOffset();

        // Volume panel.
        VolumeScopeBox.ItemsSource = new[] { "All lanes", "Selected lanes" };
        VolumeScopeBox.SelectedIndex = 0;
        VolumeScopeBox.SelectionChanged += (_, _) => { Volume.SelectedLanesOnly = VolumeScopeBox.SelectedIndex == 1; VolumeScopeHint.Text = Volume.SelectedLanesOnly ? "Selected lanes" : "All lanes"; Volume.InvalidateVisual(); };
        Lanes.SampleDropped += AssignChannelFile;
        RampShapeBox.ItemsSource = new[] { "Linear", "Ease in", "Ease out", "S-curve" };
        RampShapeBox.SelectedIndex = 0;
        RampShapeBox.SelectionChanged += (_, _) => Volume.Shape = (RampShape)Math.Max(0, RampShapeBox.SelectedIndex);
        StartBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                if (OsuNumber.TryParseDouble(StartBox.Text?.Trim() ?? "", out double ms) && double.IsFinite(ms)) Lanes.Edits.MoveStartTo(ms);
                Lanes.Focus();
                UpdateInfoBar();
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape) { Lanes.Focus(); UpdateInfoBar(); e.Handled = true; }
        };
        StartBox.LostFocus += (_, _) => UpdateInfoBar();
        VolumeModeBox.ItemsSource = new[] { "Relative", "Set all" };
        VolumeModeBox.SelectedIndex = 0; // relative: several hits keep their differences
        VolumeApplyButton.Click += (_, _) => ApplyVolumeValue();
        VolumeValueBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ApplyVolumeValue();
            e.Handled = true;
        };
        VolumeSelectButton.Click += (_, _) => SelectByVolume();
        SaveButton.Click += (_, _) => { if (Keys.Document is not null) _ = SaveKeysound(); else _ = Save(); };
        menuCommands =
        [
            (OpenItem, "open"), (OpenFromOsuItem, "openFromOsu"), (ImportOszItem, "importOsz"), (OpenFolderItem, "openFolder"), (OpenHitsoundFolderItem, "openHitsoundFolder"), (ReviewOutputItem, "reviewOutput"), (NewDiffItem, "newDiff"), (SaveItem, "save"), (RevertItem, "revert"), (PreferencesItem, "preferences"), (HelpItem, "help"), (InspectorItem, "inspector"),
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
        SetupInspector();
        SetupCompactLayout();
        SetupTutorial();
        SetupUpdates();
        SetupStartScreen(show: path is null);
        SetupKeysound();
        SetupOutput();
        SetupSoundSets();
        Deactivated += (_, _) =>
        {
            UpdateToolCursors(KeyModifiers.None);
            Keys.ReleaseTypedNotes(); // their key releases now go to another app
        };
        HScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.Axis.Left = HScroll.Value; };
        VScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.ScrollY = VScroll.Value; SyncScrollBars(); };

        frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => OnFrame());
        frameTimer.Start();

        Closing += OnEditorClosing;
        Closed += (_, _) =>
        {
            StopWatchingMap();
            loadNumber++; // a map still loading is dropped, not shown in a closed window
            compileCancel?.Cancel();
            frameTimer.Stop();
            UiTheme.Current.Changed -= OnThemeChanged;
            inspector?.Close();
            UnloadDrumVst();
            drumVstPanel?.Close();
            audio?.Dispose();
        };

        // Drop a .osu to open it, or an .osz to import it.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DroppedMap(e) is null ? DragDropEffects.None : DragDropEffects.Copy);
        AddHandler(DragDrop.DropEvent, async (_, e) => { if (DroppedMap(e) is { } dropped) await OpenDropped(dropped); });

        // Pick mode: while the inspector's Pick is on, a click selects the part under it instead of acting on it.
        AddHandler(PointerPressedEvent, OnPickPressed, RoutingStrategies.Tunnel);
        // The play key (Space) always plays or stops, before a focused button, toggle or list can take it.
        AddHandler(KeyDownEvent, OnPlayKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPlayKeyUp, RoutingStrategies.Tunnel);
        UiTheme.Current.Changed += OnThemeChanged;
        WireMapFeatures();
        ApplyTheme();
        if (showInspector) Opened += (_, _) => ToggleInspector();

        UpdateStatus();
        if (path is not null) Open(path);
        else Loading = ShowDefaultLanes();
    }

    /// <summary>Before a map is opened: the default lanes in their sampleset groups, to listen to; the grid stays empty.</summary>
    private async Task ShowDefaultLanes()
    {
        int number = loadNumber;
        var file = OsuFile.Parse("osu file format v14\r\n\r\n[General]\r\nSampleSet: Normal\r\n\r\n[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n"u8.ToArray());
        var folder = new MapFolder([]);
        var map = Decompiler.Decompile(file, folder);
        var placeholder = new EditSession(file, folder, map);
        var skinSounds = skin;
        await Task.Run(() => _ = skinSounds.Value); // reading lazer's sounds can take a moment
        if (number != loadNumber || doc is not null) return; // a map was opened meanwhile

        if (audio is not null)
        {
            var sounds = skinSounds.Value;
            var prepared = await Task.Run(() => AudioEngine.Prepare(map with { Lanes = placeholder.Lanes }, folder, sounds));
            if (number != loadNumber || doc is not null)
            {
                prepared.Dispose();
                return;
            }

            audio.Apply(prepared);
        }

        if (number != loadNumber || doc is not null) return;
        var preview = new ArrangeModel(placeholder, file, audio?.SilentLanes ?? new HashSet<Lane>());
        if (audio is not null) audio.IsAudible = preview.IsAudible;
        Lanes.IsPreview = true;
        Lanes.SetModel(preview);
        preview.Changed += UpdateActiveLaneText;
        UpdateActiveLaneText();
    }

    private int loadNumber; // a newer Open makes an older one still loading drop its result

    /// <summary>The map loading now (completed when idle), for the headless snapshots.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Opens a map: reading and decoding run off the UI thread. Errors are shown in the status bar, never thrown.</summary>
    public Task Open(string path) => Loading = Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".osz" => OpenDropped(path),
        KeysoundStore.Extension => OpenKeysound(path),
        _ => LeaveKeysoundThen(() => OpenAsync(path)),
    };

    private async Task<bool> OpenAsync(string path, bool restoreUnanchored = true, HsProject? hsOverride = null)
    {
        // The current map's project first: a save still waiting for its timer must not be lost or land on the new map.
        SaveProject();
        int number = ++loadNumber;
        flash = $"Loading {Path.GetFileName(path)}…";
        UpdateStatus();
        var skinSounds = skin;
        try
        {
            bool withAudio = audio is not null;
            PreparedLoad? prepared = null;
            var opened = await Task.Run(() =>
            {
                var sounds = skinSounds.Value; // read the default sounds here too, the first time
                var document = MapDocument.Load(path);
                // Decode every drawable lane off the UI thread. The window only publishes the buffers afterwards.
                if (withAudio)
                    prepared = AudioEngine.Prepare(document.Map with { Lanes = document.Session.Lanes }, document.Folder, sounds);
                return document;
            });
            if (number != loadNumber)
            {
                prepared?.Dispose();
                return false;
            }
            var (full, file, mapFolder, map, editing) = (opened.Path, opened.File, opened.Folder, opened.Map, opened.Session);

            compileCancel?.Cancel();
            if (session is not null) session.Changed -= OnEdited;
            (doc, lastCompile, closeConfirmed, flash) = (opened, null, false, null);

            // Load every drawable lane's sample, not only the ones the map uses, so new hits sound at once.
            if (audio is not null && prepared is not null)
                audio.Apply(prepared);
            else
                prepared?.Dispose();
            UsedLanesToggle.IsChecked = false; // a new map shows all its lanes
            model = new ArrangeModel(editing, file, audio?.SilentLanes ?? new HashSet<Lane>());
            if (audio is not null) audio.IsAudible = model.IsAudible;
            editing.Changed += OnEdited;
            Remember(full);
            LoadWaveform();
            mapSongOffset = 0; // a map without a project has none of its own
            ApplyOffsets();
            RestoreProject(restoreUnanchored, hsOverride);
            closeProjectBaseline = CaptureProject();
            model.PreviewMutesChanged += UpdateCues;
            model.Changed += UpdateActiveLaneText;
            model.Changed += Volume.InvalidateVisual;
            UpdateActiveLaneText();
            UpdateCues(); // project restoration may have changed layers and preview mutes
            model.PreviewMutesChanged += ScheduleProjectSave;
            model.Changed += UpdateSongMute; // soloing a lane silences the song

            Lanes.IsPreview = false;
            Lanes.SetModel(model);
            Lanes.Axis.End = Math.Max(audio?.Length ?? 0, model.LastTime);
            Lanes.Axis.Left = map.Moments.Count > 0 ? map.Moments[0].Time - 500 : 0;
            Overview.Attach(model, Lanes.Axis, Lanes.Axis.End);
            Volume.Attach(model, Lanes.Axis);
            FileTitle.Text = $"{Path.GetFileName(full)} — Hitsounds";
            ToolTip.SetTip(FileTitle, full);
            Inspector.Browser.SetRoots(Path.GetDirectoryName(full), prefs.SongsFolder ?? (usePreferences ? Preferences.FindSongsFolder() : Path.GetDirectoryName(full)));
            Title = $"{Path.GetFileNameWithoutExtension(full)} — dawsu! Hitsound";
            flash = $"Opened {Path.GetFileName(full)}. Save .osu updates this difficulty.";
            UpdateLoop();
            SyncScrollBars();
            UpdateLcd();
            UpdateStatus();
            ScheduleCompile();
            RevertItem.IsEnabled = true;
            RefreshHsMode();
            WatchMapFile(full); // live reload when osu! (or anything else) saves it
            HideStartScreen();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            if (number != loadNumber) return false;
            flash = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
            UpdateStatus();
            return false;
        }
    }

    internal ArrangeView ArrangeArea => Lanes;

    internal static SnapSpacing NextWheelSnap(SnapSpacing current, int direction, bool triplets)
    {
        var choices = triplets ? TripletSnaps : StraightSnaps;
        double length = current.Step(1);
        return direction > 0 ? choices.FirstOrDefault(s => s.Step(1) < length, choices[^1])
            : direction < 0 ? choices.LastOrDefault(s => s.Step(1) > length, choices[0]) : current;
    }

    /// <summary>Asks before a command switches to another map while this one has unsaved edits.</summary>
    private async Task<bool> MayLeaveMap() =>
        !savingHs && (session is not { IsEdited: true }
        || await Dialogs.Confirm(this, "Unsaved edits", "This map has edits that aren't saved. Open the other map and discard them?", "Discard and open"));

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
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } osz) await ImportOszFile(osz, songs);
    }

    // The first dropped file that is a .osu or an .osz, or null.
    private static string? DroppedMap(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null && Path.GetExtension(p).ToLowerInvariant() is ".osu" or ".osz" or KeysoundStore.Extension);

    private async Task OpenDropped(string path)
    {
        if (Path.GetExtension(path).Equals(KeysoundStore.Extension, StringComparison.OrdinalIgnoreCase)) await Open(path);
        else if (Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase))
        {
            if (await MayLeaveMap()) await Open(path);
        }
        else if (prefs.SongsFolder is { } songs && Directory.Exists(songs)) await ImportOszFile(path, songs);
        else
        {
            flash = "Set the osu! Songs folder in Preferences first: the .osz is unpacked there.";
            UpdateStatus();
        }
    }

    /// <summary>Unpacks an .osz into a new folder in the Songs folder, then opens a difficulty from it.</summary>
    private async Task ImportOszFile(string osz, string songs)
    {
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
            FileTypeFilter = [new FilePickerFileType("osu! map or archive") { Patterns = ["*.osu", "*.osz"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } local) await OpenDropped(local);
    }

    // Holding Alt shows the pen over the grid and the bars (Alt draws with any tool), as in V6.39.
    private void UpdateToolCursors(KeyModifiers modifiers)
    {
        Lanes.UpdateCursor(modifiers);
        Volume.UpdateCursor(modifiers);
    }

    private Key? shortcutKeyHeld;

    // A focused button, toggle, slider, list or tree used to take the editor's keys first: Space pressed the button just
    // clicked, and a number key searched the file browser's list instead of picking a tool. Shortcuts now reach the editor
    // first wherever focus is, except while typing in a text box, with a drop-down list open, and for the keys those
    // controls need to work (arrows, Enter, Esc, Tab, Page Up/Down, Home/End). On the editor's own canvases the normal
    // key handling (OnKeyDown) runs as before.
    private void OnPlayKeyDown(object? sender, KeyEventArgs e)
    {
        // Musical typing takes the keyboard first (except in text boxes): only Space and Esc keep a meaning, as in DAWs.
        if (!Start.IsVisible && Keys.Document is not null && Keys.TypingOn && FocusManager?.GetFocusedElement() is not TextBox)
        {
            if (Keys.TypingKeyDown(e)) e.Handled = true;
            return;
        }
        if (Start.IsVisible || EditorCommands.Match(e) is not { } command) return;
        // In a flyout the key comes from its pop-up, whose focus the window doesn't track: the event's source is it.
        object? focused = e.Source as Control ?? FocusManager?.GetFocusedElement();
        if (focused is TextBox || focused is ComboBox { IsDropDownOpen: true }) return;
        if (focused is ArrangeView or PianoRollView or VolumeLane or OverviewStrip or Window or null && command.Id != "play") return;
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Escape or Key.Tab
            or Key.PageUp or Key.PageDown or Key.Home or Key.End) return;
        if (command.Id is "help" or "preferences" or "inspector" or "save") return; // OnKeyDown runs these in both modes
        e.Handled = true;
        shortcutKeyHeld = e.Key;
        if (Keys.Document is not null) Keys.HandleKey(e);
        else Run(command.Id);
    }

    // The key's release would click a focused button: it belongs to the press above.
    private void OnPlayKeyUp(object? sender, KeyEventArgs e)
    {
        if (Keys.Document is not null && Keys.TypingOn && FocusManager?.GetFocusedElement() is not TextBox && Keys.TypingKeyUp(e))
        {
            if (shortcutKeyHeld == e.Key) shortcutKeyHeld = null; // e.g. the K of the Alt+K that turned typing on
            e.Handled = true;
            return;
        }
        if (shortcutKeyHeld != e.Key) return;
        shortcutKeyHeld = null;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt) UpdateToolCursors(e.KeyModifiers & ~KeyModifiers.Alt);
        base.OnKeyUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt) UpdateToolCursors(e.KeyModifiers | KeyModifiers.Alt);
        if (StartScreenKey(e)) { e.Handled = true; return; }
        if (KeysoundKey(e)) { e.Handled = true; return; }
        // Typing in a text box (volume value, offset…) must not trigger editor shortcuts like 5 (Mute) or Backspace (delete hits).
        var focused = FocusManager?.GetFocusedElement();
        bool controlKey = (focused is Button or Avalonia.Controls.Primitives.ToggleButton && e.Key is Key.Space or Key.Enter)
            || (focused is ComboBox or Slider && e.Key is Key.Space or Key.Enter or Key.Escape
                or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown);
        var command = EditorCommands.Match(e);
        bool globalCommand = (command?.Id is "save" or "help")
            && (e.Key is >= Key.F1 and <= Key.F24
                || e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                && e.Key is not (Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y or Key.Left or Key.Right
                    or Key.Up or Key.Down or Key.Home or Key.End or Key.Back or Key.Delete));
        if (e.Handled || focused is TextBox && !globalCommand
            || (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && controlKey))
        {
            base.OnKeyDown(e);
            return;
        }

        if (command is null)
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
        if (savingHs || renderingDrums) return;
        switch (id)
        {
            case "open": _ = PickMap(); break;
            case "openFromOsu": _ = OpenFromOsu(); break;
            case "startScreen": _ = GoToStartScreen(); break;
            case "updates": _ = CheckForUpdates(quiet: false); break;
            case "importOsz": _ = ImportOsz(); break;
            case "openFolder": OpenMapFolder(); break;
            case "openHitsoundFolder": OpenHitsoundFolder(); break;
            case "reviewOutput": _ = ReviewOutput(); break;
            case "newDiff": _ = NewHitsoundDiff(); break;
            case "save": _ = Save(); break;
            case "revert": _ = RevertToLastSaved(); break;
            case "preferences": _ = ShowPreferences(); break;
            case "help": ShowHelp(); break;
            case "inspector": ToggleInspector(); break;
            case "play": TogglePlay(); break;
            case "start": Seek(0); break;
            case "first": JumpToEnd(first: true); break;
            case "last": JumpToEnd(first: false); break;
            case ['s', 'n', 'a', 'p', var digit] when char.IsAsciiDigit(digit):
                SnapBox.SelectedIndex = Array.IndexOf(SnapOptions, new SnapSpacing(1, digit - '0'));
                break;
            case "stop": StopPressed(); break;
            case "cycle": ToggleCycle(); break;
            case "locatorsToSelection": LocatorsToSelection(); break;
            case "playLocatorRange": PlayLocatorRange(); break;
            case "jumpLeft": if (Lanes.LocatorLeft is { } l) Seek(l); break;
            case "seekBack": SeekGridSteps(-1); break;
            case "seekForward": SeekGridSteps(1); break;
            case "seekBackFar": SeekGridSteps(-4); break;
            case "seekForwardFar": SeekGridSteps(4); break;
            case "jumpRight": if (Lanes.LocatorRight is { } r) Seek(r); break;
            case "setLeft": Lanes.SetLocators(Lanes.Playhead, Lanes.LocatorRight ?? Lanes.Playhead); break;
            case "setRight": Lanes.SetLocators(Lanes.LocatorLeft ?? Lanes.Playhead, Lanes.Playhead); break;
            case "toolSelect": SetTool(Tool.Select); break;
            case "toolGrab": SetTool(Tool.Grab); break;
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
            case "deselect": Lanes.Edits.Deselect(); model?.DeselectLanes(); break;
            case "quantize": Quantize(Lanes.Edits.QuantizeStrength); break;
            case "quantizeDialog": _ = QuantizeDialog(); break;
            case "repeat": _ = Repeat(); break;
            case "editRange": _ = EditRange(); break;
            case "scaleByVolume": _ = ScaleByVolume(); break;
            case "delete": Lanes.Edits.DeleteSelection(); break;
            case "nudgeLeft": Lanes.Edits.Nudge(-1, fine: false, toGrid: false); break;
            case "selectPrevious": Lanes.Edits.SelectNeighbour(-1, extend: false, Lanes.Playhead); break;
            case "selectNext": Lanes.Edits.SelectNeighbour(1, extend: false, Lanes.Playhead); break;
            case "addPrevious": Lanes.Edits.SelectNeighbour(-1, extend: true, Lanes.Playhead); break;
            case "addNext": Lanes.Edits.SelectNeighbour(1, extend: true, Lanes.Playhead); break;
            case "navUp": Lanes.Edits.SelectAcross(-1, extend: false, Lanes.Axis.VisibleDuration / 2); break;
            case "navDown": Lanes.Edits.SelectAcross(1, extend: false, Lanes.Axis.VisibleDuration / 2); break;
            case "navAddUp": Lanes.Edits.SelectAcross(-1, extend: true, Lanes.Axis.VisibleDuration / 2); break;
            case "navAddDown": Lanes.Edits.SelectAcross(1, extend: true, Lanes.Axis.VisibleDuration / 2); break;
            case "rowsOut": Lanes.ZoomRows(1 / 1.15); break;
            case "rowsIn": Lanes.ZoomRows(1.15); break;
            case "volumeUp": ShiftSelectedVolumes(5); break;
            case "volumeDown": ShiftSelectedVolumes(-5); break;
            case "volumeUpFine": ShiftSelectedVolumes(1); break;
            case "volumeDownFine": ShiftSelectedVolumes(-1); break;
            case "nudgeRight": Lanes.Edits.Nudge(1, fine: false, toGrid: false); break;
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
        else
        {
            var times = session.Moments.Select(m => m.Moment.Time).Concat(session.State.Hits.Select(h => h.Time)).ToList();
            Seek(times.Count == 0 ? first ? 0 : Lanes.Axis.End : first ? times.Min() : times.Max());
        }
    }

    private void SetTool(Tool tool)
    {
        if (tool == Tool.Ramp) { VolumeToggle.IsChecked = true; VolumeTab.IsChecked = true; }
        Lanes.Tool = tool;
        SelectTool.IsChecked = tool == Tool.Select;
        GrabTool.IsChecked = tool == Tool.Grab;
        DrawTool.IsChecked = tool == Tool.Draw;
        EraseTool.IsChecked = tool == Tool.Erase;
        MuteTool.IsChecked = tool == Tool.Mute;
        RampTool.IsChecked = tool == Tool.Ramp;
        Volume.RampTool = tool == Tool.Ramp;
        Volume.Tool = tool;
        Volume.UpdateCursor(KeyModifiers.None);
        UpdateStatus();
    }

    /// <summary>What playback plays: every hit (less those muted for preview) and the map's ticks and storyboard samples.</summary>
    private void UpdateCues()
    {
        if (model is null || session is null || audio is null) return;
        audio.SetCues(PlaybackCues(model));
        RefreshDrumVstRouting();
    }

    internal static IEnumerable<Cue> PlaybackCues(ArrangeModel model) =>
        model.Session.State.Hits.Where(h => !model.IsPreviewMuted(h)).Select(h => h.SetPlacement == Guid.Empty
            ? HitScheduler.HitCue(h.Time, h.Lane, model.Session.VolumeOf(h))
            : new Cue(h.Time, h.Lane, Math.Clamp(model.Session.VolumeOf(h), 0, 100) / 100f))
        .Concat(HitScheduler.PassiveCues(model.Session.Map));

    /// <summary>Sets the locators from their boxes; a box that isn't a number goes back to the locator as it was.</summary>
    private void ApplyLocatorBoxes()
    {
        static double? Parse(string? text) => OsuNumber.TryParseDouble(text ?? "", out double ms) && double.IsFinite(ms) ? Math.Max(0, ms) : null;
        double? left = Parse(LeftLocatorBox.Text) ?? Lanes.LocatorLeft, right = Parse(RightLocatorBox.Text) ?? Lanes.LocatorRight;
        if (left is null && right is null)
        {
            ShowLocators();
            return;
        }

        double l = left ?? right!.Value, r = right ?? l;
        if (l == Lanes.LocatorLeft && r == Lanes.LocatorRight) ShowLocators();
        else Lanes.SetLocators(l, r); // shows them again through LocatorsChanged
    }

    private void ShowLocators()
    {
        static string Ms(double? t) => t is { } v ? Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        LeftLocatorBox.Text = Ms(Lanes.LocatorLeft);
        RightLocatorBox.Text = Ms(Lanes.LocatorRight);
    }

    // The controller bar names the active lane, as in V6.39.
    private void UpdateActiveLaneText()
    {
        var displayed = model ?? Lanes.Model;
        ActiveLaneText.Text = displayed?.ActiveLane is { } lane ? displayed.NameOf(lane) : "";
        RefreshInspector();
    }

    private void UpdateSongMute()
    {
        // Soloing lanes silences the song, unless the song is soloed too.
        if (audio is not null) audio.SongMuted = SongMuteButton.IsChecked == true || (model is { AnySoloed: true, SongSoloed: false });
    }

    // The map's own song offset (the Offset box, saved with the map). What plays and is drawn adds the one for all maps
    // (Preferences): maps are timed for osu!'s own audio clock, about 17 ms ahead of the raw audio here.
    private double mapSongOffset;

    private double SongOffsetInForce => EffectiveSongOffset(mapSongOffset, prefs.GlobalSongOffset);

    internal static double EffectiveSongOffset(double map, double global) => Math.Clamp(
        (double.IsFinite(map) ? Math.Clamp(map, -10000, 10000) : 0) +
        (double.IsFinite(global) ? Math.Clamp(global, -1000, 1000) : 0), -10000, 10000);

    /// <summary>Puts the map's offset plus the one for all maps on the song and its waveform, and shows the map's in its box.</summary>
    private void ApplyOffsets()
    {
        if (audio is not null) audio.SongOffset = SongOffsetInForce;
        Lanes.SongOffset = SongOffsetInForce;
        SongOffsetBox.Text = mapSongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Lanes.InvalidateVisual();
    }

    private void ApplySongOffset()
    {
        if (!OsuNumber.TryParseDouble(SongOffsetBox.Text ?? "", out double offset) || !double.IsFinite(offset))
        {
            SongOffsetBox.Text = mapSongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        if (Math.Clamp(offset, -10000, 10000) == mapSongOffset) return;
        mapSongOffset = Math.Clamp(offset, -10000, 10000);
        ApplyOffsets();
        ScheduleProjectSave();
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

        if (VolumeModeBox.SelectedIndex == 0) ShiftSelectedVolumes(value);
        else if (Volume.SelectedLanesOnly && model is not null)
        {
            var selected = model.SelectedLanes;
            var values = session.State.Hits.Where(h => selected.Contains(h.Lane) && h.Anchor is { } key && keys.Contains(key))
                .ToDictionary(h => h, _ => Math.Clamp(value, 1, 100));
            if (values.Count == 0) { flash = "Select lanes with hits at the selected times."; UpdateStatus(); return; }
            session.SetLayerVolumes(values);
        }
        else session.SetVolume(keys, Math.Clamp(value, 1, 100));
    }

    /// <summary>
    /// Moves the selected hits' volumes by <paramref name="delta"/>, each kept within 1–100, so their differences stay
    /// (the Value box's Relative, and Alt+Up/Down). In "Selected lanes" scope only those lanes' sounds change.
    /// </summary>
    private void ShiftSelectedVolumes(int delta, string label = "Volume")
    {
        if (session is null) return;
        var keys = Lanes.SelectedMoments;
        if (keys.Count == 0) { flash = "Select some hits first."; UpdateStatus(); return; }
        if (Volume.SelectedLanesOnly && model is not null)
        {
            var selected = model.SelectedLanes;
            var values = session.State.Hits.Where(h => selected.Contains(h.Lane) && h.Anchor is { } key && keys.Contains(key))
                .ToDictionary(h => h, h => Math.Clamp(session.VolumeOf(h) + delta, 1, 100));
            if (values.Count == 0) { flash = "Select lanes with hits at the selected times."; UpdateStatus(); return; }
            session.SetLayerVolumes(values, label);
            return;
        }

        var affected = session.State.Hits.Where(h => h.Anchor is { } key && keys.Contains(key)).ToList();
        if (affected.Any(session.State.LayerVolumes.ContainsKey))
            session.SetLayerVolumes(affected.ToDictionary(h => h, h => Math.Clamp(session.VolumeOf(h) + delta, 1, 100)), label);
        else session.SetVolumes(VolumeRamp.Shift(session.State.Volumes, keys, delta), label);
    }

    private void SelectByVolume()
    {
        if (session is null) return;
        int min = int.TryParse(VolumeMinBox.Text, out int a) ? a : 0, max = int.TryParse(VolumeMaxBox.Text, out int b) ? b : 100;
        Lanes.Edits.SelectWhere(h => h.Anchor is not null && (!Volume.SelectedLanesOnly || model?.SelectedLanes.Contains(h.Lane) == true) && session.VolumeOf(h) >= min && session.VolumeOf(h) <= max);
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
        Inspector.Browser.OpenMapRequested += async path => await OpenDropped(path);
        Inspector.Browser.SampleRequested += path =>
        {
            if (model?.ActiveLane is { } lane) AssignChannelFile(lane, path);
            else { flash = "Select a lane before choosing its sample."; UpdateStatus(); }
        };
        Inspector.Browser.PreviewRequested += path =>
        {
            try
            {
                if (audio is null) throw new IOException("Audio output is unavailable.");
                audio.Pause();
                audio.PreviewMix(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { flash = ex.Message; UpdateStatus(); }
        };
        Inspector.Browser.SetRoots(null, prefs.SongsFolder ?? (usePreferences ? Preferences.FindSongsFolder() : null));
        Inspector.ChannelSampleRequested += AssignChannelChoice;
        Inspector.BrowseChannelSampleRequested += lane =>
        {
            BrowserPanelItem.IsChecked = true; ShowBrowser(true);
            if (prefs.ClassicBrowser) Inspector.Reveal("browser");
            Inspector.Browser.Focus();
        };
        Inspector.PreviewChannelRequested += lane =>
        {
            try
            {
                if (doc is not null) audio?.Preview(doc.LanePreviewAt(lane, Lanes.Playhead), doc.Folder);
                else audio?.Preview(lane);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { flash = ex.Message; UpdateStatus(); }
        };
        Inspector.ActivateLaneRequested += lane =>
        {
            model?.SetActive(lane);
            ScheduleProjectSave();
            Lanes.InvalidateVisual();
        };
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
        Inspector.CloseRequested += () => InspectorToggle.IsChecked = false;
        Inspector.SeekRequested += Seek;
        Inspector.RenameBookmarkRequested += (t, label) => session?.RenameBookmark(t, label);
        SetupWorkflowPanels();
        Inspector.AddBookmarkRequested += () => Run("addBookmark");
        Inspector.RemoveBookmarkRequested += t => session?.RemoveBookmarkNear(t, 1);
        Inspector.GoToStepRequested += p => session?.GoTo(p);
    }

    /// <summary>The V6.39 selection bar: how many hits, where the selection starts, and the object under it.</summary>
    private void UpdateInfoBar()
    {
        var selected = Lanes.Selection;
        if (session is null || selected.Count == 0)
        {
            (InfoSelection.Text, InfoStart.Text, InfoObject.Text) = ("No events", "", "—");
            if (!StartBox.IsFocused) StartBox.Text = "";
            return;
        }

        InfoSelection.Text = selected.Count == 1 ? "1 event" : $"{selected.Count} events";
        double start = selected.Min(h => h.Time);
        if (!StartBox.IsFocused) StartBox.Text = Math.Round(start).ToString(System.Globalization.CultureInfo.InvariantCulture);
        InfoStart.Text = $"({FormatTime(start)})";
        var anchors = selected.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value).Distinct().ToList();
        InfoObject.Text = anchors.Count switch
        {
            0 => "none (red: no object)",
            1 when session.MomentByKey.GetValueOrDefault(anchors[0]) is { } m => Describe(m),
            var n => $"{n} objects",
        };
    }

    // The inspector's width while shown, kept here so hiding it before closing doesn't lose it.
    private double inspectorWidth = 250;

    /// <summary>The width to save: the panel's when shown, else the one it had.</summary>
    internal double InspectorWidthToSave => InspectorToggle.IsChecked == true && Root.ColumnDefinitions[0].ActualWidth > 0 ? Root.ColumnDefinitions[0].ActualWidth : inspectorWidth;

    private void RefreshInspector()
    {
        var displayed = model ?? Lanes.Model;
        Inspector.Refresh(session ?? displayed?.Session, displayed, Lanes.Selection, FormatTime, canEdit: doc is not null);
        Inspector.SoundSets.Refresh(session, Lanes.Selection);
        RefreshDrumMapToggle();
        Lanes.DrawSoundSetActive = Inspector.SoundSets.SelectedSet is { } id && session?.State.SoundSets.Any(s => s.Id == id) == true;
    }

    /// <summary>Fit: zooms out so the whole map is in view.</summary>
    private void FitTimeline()
    {
        if (Lanes.Axis.End <= 0) return;
        Lanes.Axis.ZoomAt(Lanes.Axis.VisibleDuration / Lanes.Axis.End, 0);
        Lanes.Axis.Left = 0;
        Lanes.InvalidateVisual();
    }

    /// <summary>Q: quantizes the selected loose hits; says so when there are none.</summary>
    private void Quantize(double strength)
    {
        if (session is null) return;
        if (Lanes.Edits.Quantize(strength) == 0)
        {
            flash = "Quantize moves loose hits or HS-mode circles; select an off-grid hit to quantize.";
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

        if (await Dialogs.Repeat(this, Lanes.Edits.DefaultRepeatGap(), Lanes.Spacing) is { } r) Lanes.Edits.Repeat(r.Copies, r.Gap);
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
        Lanes.SongOffset = SongOffsetInForce;
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
        ShowPlaying(true);
    }

    /// <summary>After any edit: redraw from the session, play what's wanted now, and check it in the background.</summary>
    private void OnEdited()
    {
        if (model is null || session is null) return;
        if (!ReferenceEquals(session.State.Hits, drawnHits) || !ReferenceEquals(session.State.HsCircles, drawnHsCircles)
            || !ReferenceEquals(session.State.Slides, drawnSlides))
        {
            drawnSlides = session.State.Slides;
            // Volume-only edits (bar drags, ramps) skip this: the notes and the selection only depend on the hits.
            drawnHits = session.State.Hits;
            drawnHsCircles = session.State.HsCircles;
            Lanes.Edits.Prune();
            model.RebuildNotes();
            if (model.HasHiddenLanes) model.RefreshRows(); // a deleted lane whose hits came back (undo) shows again
        }
        else Lanes.InvalidateVisual();

        Lanes.Axis.End = Math.Max(audio?.Length ?? 0, model.LastTime);
        Overview.Attach(model, Lanes.Axis, Lanes.Axis.End);
        RefreshHsMode();
        UpdateCues();
        Title = $"{(session.IsEdited ? "• " : "")}{Path.GetFileNameWithoutExtension(mapPath)} — dawsu! Hitsound";
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
        var output = exportSettings;
        _ = Task.Run(async () =>
        {
            await Task.Delay(150, cancel.Token);
            MapCompilation compiled;
            try { compiled = document.Compile(state, output, skin.Value); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
            {
                compiled = document.Compile(state);
                compiled = compiled with { Result = compiled.Result with { Conflicts = compiled.Result.Conflicts.Concat(
                    state.Hits.Where(h => h.Anchor is not null).GroupBy(h => h.Anchor!.Value)
                        .Select(g => new Conflict(g.First().Time, g.Key, ex.Message))).DistinctBy(c => c.Moment).ToList() } };
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancel.IsCancellationRequested || !ReferenceEquals(editing, session) || model is null) return;
                lastCompile = compiled;
                model.SetConflicts(compiled.Result.Conflicts.Select(c => c.Moment).ToHashSet());
                Lanes.ExportProblems = compiled.Result.Conflicts;
                UpdateStatus();
            });
        }, cancel.Token).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private bool savedMapWritten; // set by SaveMap/SaveHs when the .osu itself was written

    /// <summary>Saves (see <see cref="SaveMap"/>); when that wrote the .osu and osu!'s editor has it open, reloads it there.</summary>
    private async Task<bool> Save()
    {
        savedMapWritten = false;
        bool saved = await SaveMap();
        if (savedMapWritten && prefs.ReloadOsuAfterSave && mapPath is { } path) _ = ReloadOsuEditor(path);
        return saved;
    }

    /// <summary>
    /// Saves the edits: compiles a fresh copy of the map as opened, refuses if the file changed on disk since, keeps a
    /// timestamped backup in the editor's app-data folder (never in the mapset), writes the .osu and reloads it.
    /// </summary>
    private async Task<bool> SaveMap()
    {
        if (savingHs || generating || !Loading.IsCompleted) return false;
        if (session is null || mapPath is null || originalBytes is null || folder is null) return false;
        if (exportSettings.PairSamples || session.State.HsMode || session.HasHsObjects) return await SaveHs();
        bool success = false;
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
                savedMapWritten = true;
                if (!await ReopenKeepingView(mapPath)) return false;
                flash = string.Join(' ', [
                    $"Saved. {result.TimingPointsAdded} green line{(result.TimingPointsAdded == 1 ? "" : "s")} added, {result.TimingPointsChanged} changed."
                        + Problems(result.Conflicts.Count, unanchored),
                    .. result.Notes, $"Backup: {saved.Backup}"]);
            }
            success = result.Conflicts.Count == 0 && unanchored == 0 && SaveProject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            flash = $"Not saved: {ex.Message}";
        }

        UpdateStatus();
        return success;

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
            if (ReferenceEquals(v, WindowBar)) return "window";
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
        // A new row height in the theme replaces a Ctrl+Shift+wheel zoom.
        double rowHeight = UiTheme.Current.Number("row.height");
        if (rowHeight != lastRowHeight) Lanes.FollowThemeRowHeight();
        lastRowHeight = rowHeight;
        ApplyTheme();
    }

    private void ShowPlaying(bool playing) => AppIcon.ShowPlaying(PlayButton, PlayGlyph, playing);

    // With our own title bar, a maximised window's frame reaches past the screen edges: the content steps in by that margin.
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // WindowRoot is null while the XAML is still loading.
        if ((change.Property == WindowStateProperty || change.Property == OffScreenMarginProperty) && WindowRoot is not null)
            WindowRoot.Margin = WindowState == WindowState.Maximized ? OffScreenMargin : default;
    }

    /// <summary>The controls lined up with the lane list's right edge: the scrollbar, filter, drum toggle and volume panel.</summary>
    private void AlignToLaneList()
    {
        HScroll.Margin = new Avalonia.Thickness(ArrangeView.GridLeft, 0, 0, 0);
        LaneFilterBox.Width = ArrangeView.GridLeft - 16;
        DrumMapToggle.Margin = new Avalonia.Thickness(ArrangeView.GridLeft - 96, 0, 0, 0);
        VolumeControlScroller.Width = ArrangeView.GridLeft;
        Volume.InvalidateVisual();
    }

    /// <summary>Applies the theme to the window's bars and controls and redraws the drawn parts.</summary>
    private void ApplyTheme()
    {
        var t = UiTheme.Current;
        Avalonia.Media.IBrush B(string key) => Paint.Brush(t.Color(key));

        // Colours and control sizes come from the application resources (ThemeResources); this places the bars.
        FileTitle.Foreground = B("title.text");
        FileTitle.FontSize = t.Number("title.fontSize");
        StatusLeft.Foreground = StatusRight.Foreground = B("status.text");
        StatusLeft.FontSize = StatusRight.FontSize = t.Number("status.fontSize");
        var rows = Root.RowDefinitions;
        rows[0].Height = new GridLength(t.Number("title.height"));
        // Toolbar and selection details wrap; the transport keeps its original themed height.
        UpdateChromeHeights();
        rows[3].Height = new GridLength(t.Number("overview.height"));
        bool showVolume = VolumeToggle.IsChecked == true;
        BottomPanel.IsVisible = showVolume;

        AlignToLaneList();
        UpdateVolumePanelHeight();

        Lanes.ScrollY = Lanes.ScrollY;
        Lanes.InvalidateVisual();
        Overview.InvalidateVisual();
        Volume.InvalidateVisual();
        SyncScrollBars();
    }

    private void UpdateChromeHeights()
    {
        double width = Bounds.Width > 0 ? Bounds.Width : Width;
        var t = UiTheme.Current;
        UpdateCompactLayout(width);
        TitleBarContent.Width = Math.Max(TitleBarContent.MinWidth, width - 24);
        ControlBarContent.Width = Math.Max(ControlBarContent.MinWidth, width - 24);
        EditBarContent.Width = Math.Max(EditBarContent.MinWidth, width - 24);
        StatusBarContent.Width = Math.Max(StatusBarContent.MinWidth, width - 24);
        ControlBarContent.MinHeight = t.Number("control.height") - 8;
        SelectionBarLayout.Apply(EditBarContent, SelectionDisclosure, SelectionBarCollapsed, t.Number("edit.height") - 4);
        StatusBarContent.Height = t.Number("status.height") - 1;
        Root.RowDefinitions[1].Height = GridLength.Auto;
        Root.RowDefinitions[2].Height = GridLength.Auto;
        Root.RowDefinitions[6].Height = new GridLength(t.Number("status.height"));
    }

    private void UpdateVolumePanelHeight()
    {
        var rows = Root.RowDefinitions;
        double chrome = rows[0].Height.Value + ControlBar.Bounds.Height + EditBar.Bounds.Height
                        + rows[3].Height.Value + StatusBar.Bounds.Height;
        // Keep room for the ruler, waveform and lane rows in shorter windows. The controls scroll if needed.
        double height = Root.Bounds.Height > 0 ? Root.Bounds.Height : Bounds.Height - WindowBar.Bounds.Height;
        double available = Math.Max(0, height - chrome - Lanes.HeaderHeight - 80);
        double maximum = Math.Max(0, height - chrome - Lanes.HeaderHeight - 8);
        rows[5].Height = new GridLength(VolumeToggle.IsChecked == true
            ? Math.Min(maximum, Math.Min(UiTheme.Current.Number("volume.height"), Math.Max(80, available))) : 0);
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

        ShowPlaying(audio.IsPlaying);
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
        ShowPlaying(false);
    }

    /// <summary>The playhead by whole grid lines at the snap's spacing (the osu! editor's arrows and wheel).</summary>
    private void SeekGridSteps(int steps) => Seek(WheelMode.StepGrid(audio?.Position ?? Lanes.Playhead, steps, Lanes.Edits.GridStep));

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
        foreach (var parent in model.Rows.OfType<LaneRow>().Where(r => model.IsFamilyParent(r.Lane)).ToList())
            if (!model.IsFamilyCollapsed(parent.Lane)) model.ToggleFamily(parent.Lane);
        SyncScrollBars();
    }

    private void OnFrame()
    {
        if (audio is not null)
        {
            Lanes.Playhead = audio.Position;
            if (!audio.IsPlaying && PlayGlyph.Glyph == "Pause") ShowPlaying(false);

            // Page when the playhead reaches the right edge (or is off screen to the left), as Cubase does.
            if (audio.IsPlaying && FollowItem.IsChecked)
            {
                var axis = Lanes.Axis;
                if (Lanes.Playhead > axis.Left + axis.VisibleDuration * 0.98 || Lanes.Playhead < axis.Left)
                    axis.Left = Lanes.Playhead - axis.VisibleDuration * 0.02;
            }
        }

        // The scene (notes, names, bars, overview hits) stays cached. Only the playhead layer moves.
        if (Lanes.Playhead == lastDrawnPlayhead) return;
        lastDrawnPlayhead = Lanes.Playhead;
        Overview.Playhead = Volume.Playhead = Lanes.Playhead;
        Lanes.InvalidatePlayhead();
        Overview.InvalidatePlayhead();
        if (Volume.IsEffectivelyVisible) Volume.InvalidatePlayhead();
        UpdateLcd();
    }

    private void OnAxisChanged()
    {
        SyncScrollBars();
        Lanes.InvalidateVisual();
        Overview.InvalidateViewport();
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

    private static double ZoomFromSlider(double v) => TimeAxis.ZoomFromSlider(v);

    private static double SliderFromZoom(double zoom) => TimeAxis.SliderFromZoom(zoom);

    private void UpdateLcd()
    {
        double t = Lanes.Playhead;

        LcdTime.Text = FormatTime(t);
        if (model?.RedLineAt(t) is { } red)
        {
            var (bar, beat, tick) = model.BarBeatTick(t);
            LcdBar.Text = $"{bar}.{beat}.{tick}";
            LcdBpm.Text = (60000 / red.BeatLength).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var (n, d) = model.SignatureAt(t);
            LcdMeter.Text = $"{n}/{d}";
        }
        else
        {
            LcdBar.Text = "1.1.1";
            LcdBpm.Text = "—";
            LcdMeter.Text = "4/4";
        }
    }

    private void UpdateStatus()
    {
        RefreshInspector();
        UpdateInfoBar();
        if (SnapToggle.IsChecked != Lanes.SnapOn) SnapToggle.IsChecked = Lanes.SnapOn;
        if (model is null)
        {
            var open = EditorCommands.Get("open");
            StatusLeft.Text = flash ?? (audioError is null ? $"Open a .osu file ({(open.Keys.Count > 0 ? open.KeysText : "File menu")})." : $"No sound: {audioError}");
            StatusRight.Text = "";
            workflowWarning = null;
            RefreshWorkflowFeedback();
            return;
        }

        var map = model.Map;
        var notes = new List<string>();
        var conflicts = lastCompile?.Result.Conflicts ?? [];
        int unanchored = model.Session.Unanchored.Count();
        if (conflicts.Count > 0)
            notes.Add($"{conflicts.Count} moment{(conflicts.Count == 1 ? "" : "s")} can't be saved as drawn (first at {FormatTime(conflicts[0].Time)}: {conflicts[0].Reason})");
        if (unanchored > 0) notes.Add($"{unanchored} hit{(unanchored == 1 ? " has" : "s have")} no gameplay object and won't be included by Save .osu. Use HS mode or Generate to include them.");
        if (audioError is not null) notes.Add($"No sound: {audioError}");
        else if (audio is { HasSong: false }) notes.Add($"Song file \"{map.Settings.AudioFilename}\" not found; hitsounds play on their own.");
        int silent = model.Silent.Count(model.Notes.ContainsKey);
        if (silent > 0) notes.Add($"{silent} used lane{(silent == 1 ? "" : "s")} with no sound");
        if (map.Warnings.Count > 0) notes.Add($"{map.Warnings.Count} warning{(map.Warnings.Count == 1 ? "" : "s")}: {map.Warnings[0]}");
        workflowWarning = notes.Count > 0 ? string.Join("  ·  ", notes) : null;
        if (notes.Count == 0) notes.Add(model.Session.IsEdited ? "Edited: Ctrl+S saves." : "Everything here can be saved.");
        StatusLeft.Text = flash ?? string.Join("  ·  ", notes);
        ToolTip.SetTip(StatusLeft, string.Join("\n", conflicts.Take(20).Select(c => $"{FormatTime(c.Time)}: {c.Reason}").Concat(map.Warnings.Take(20))));
        RefreshWorkflowFeedback();

        Volume.SelectedMoments = Lanes.SelectedMoments;
        Volume.InvalidateVisual();
        string snap = Lanes.Edits.SnapMode == SnapMode.Objects ? "objects" : $"{Lanes.Spacing}{(Lanes.Edits.SnapMode == SnapMode.GridRelative ? " relative" : "")}";
        string tool = $"{Lanes.Tool}{(Lanes.SnapOn ? $", snap {snap}" : ", snap off")}";
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
        SavedFields.IsVisible = false;
        SavedText.Text = "";
        if (session is null || Lanes.Selection.Count == 0)
        {
            SavedSummary.Text = "Select a hit to see whether Save .osu includes it.";
            return;
        }
        int loose = Lanes.Selection.Count(h => h.Anchor is null);
        string looseSummary = loose == 0 ? "" : $"{loose} selected hit{(loose == 1 ? " has" : "s have")} no gameplay object. Save .osu omits {(loose == 1 ? "it" : "them")}; use HS mode or Generate to include {(loose == 1 ? "it" : "them")}.\n";
        if (Lanes.Selection.FirstOrDefault(h => h.Anchor is not null) is not { Anchor: { } key })
        {
            SavedSummary.Text = looseSummary.TrimEnd();
            return;
        }
        if (lastCompile is not { } compiled || !session.MomentByKey.TryGetValue(key, out var moment)
            || !compiled.Keys.TryGetValue(key, out var savedKey))
        {
            SavedSummary.Text = looseSummary + "Checking how this selected gameplay object will be saved…";
            return;
        }
        var conflict = compiled.Result.Conflicts.FirstOrDefault(c => c.Moment == key);
        SavedSummary.Text = looseSummary + (conflict is null
            ? $"Save .osu updates the {Describe(moment)}.\nSounds at this gameplay time share its volume setting."
            : $"Can't be saved as drawn: {conflict.Reason}");
        var line = compiled.File.HitObjects[savedKey.ObjectIndex].Line.Text;
        var point = new TimingPointIndex(compiled.File.TimingPoints).SamplePointAt(moment.Time - session.Map.Settings.TimeOffset);
        SavedText.Text = $"Object line: {line}\nTiming point it reads: {point?.Line.Text ?? "none"}";
        SavedFields.IsVisible = true;
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

    // lazer's default sounds when it's installed; the built-in ones fill in whatever it doesn't have.
    private static IReadOnlyDictionary<string, byte[]> LoadSkin(string? preferred)
    {
        try
        {
            return DefaultSkin.WithFallback(DefaultSkin.FindLazerResources(preferred) is { } dll ? DefaultSkin.LoadSamples(dll) : []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return DefaultSkin.Bundled();
        }
    }
}
