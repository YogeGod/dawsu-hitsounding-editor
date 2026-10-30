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
/// The main screen (docs/ui-design.md, variation G): opens a .osu, shows its lanes, plays it and edits its hitsounds.
/// Edits are checked by compiling in the background; Save (Ctrl+S) compiles the original file again, keeps a backup
/// outside the mapset and writes only the .osu.
/// </summary>
public partial class MainWindow : Window
{
    // Beat snap divisors, as in osu!'s editor plus 1/5, 1/7 and 1/9.
    private static readonly int[] Divisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];

    private readonly AudioEngine? audio;
    private readonly string? audioError;
    private readonly Lazy<IReadOnlyDictionary<string, byte[]>> skin = new(LoadSkin);
    private readonly DispatcherTimer frameTimer;
    private ArrangeModel? model;
    private bool syncingScroll;
    private InspectorWindow? inspector;
    private double lastDrawnPlayhead = double.NaN;
    private double lastRowHeight = UiTheme.Current.Number("row.height");

    // The open map: its path, its bytes as opened (Save compiles a fresh copy of these), and its folder.
    private string? mapPath;
    private byte[]? originalBytes;
    private MapFolder? folder;
    private EditSession? session;
    private CancellationTokenSource? compileCancel;
    private (CompileResult Result, OsuFile File)? lastCompile;
    private bool closeConfirmed;
    private string? flash; // a one-off message (saved, not saved, close warning) shown until the next edit

    public MainWindow() : this(null, startAudio: true)
    {
    }

    public MainWindow(string? path, bool startAudio = true, bool showInspector = true)
    {
        InitializeComponent();

        if (startAudio)
        {
            try
            {
                audio = AudioEngine.Create();
            }
            catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or BadImageFormatException)
            {
                audioError = ex.Message;
            }
        }

        Lanes.Axis.Changed += OnAxisChanged;
        Lanes.PreviewRequested += lane => audio?.Preview(lane);
        Lanes.SeekRequested += Seek;
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
        VolumeTab.IsCheckedChanged += (_, _) => { Volume.IsVisible = VolumeTab.IsChecked == true; SavedText.IsVisible = !Volume.IsVisible; };
        SnapBox.ItemsSource = Divisors.Select(d => $"1/{d}").ToList();
        SnapBox.SelectedIndex = Array.IndexOf(Divisors, 4);
        SnapBox.SelectionChanged += (_, _) =>
        {
            Lanes.Divisor = Divisors[Math.Max(0, SnapBox.SelectedIndex)];
            Lanes.InvalidateVisual();
            UpdateLcd();
        };
        Lanes.DivisorWheel += step => SnapBox.SelectedIndex = Math.Clamp(SnapBox.SelectedIndex + step, 0, Divisors.Length - 1);
        SelectTool.Click += (_, _) => SetTool(Tool.Select);
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
        UndoItem.Click += (_, _) => session?.Undo();
        RedoItem.Click += (_, _) => session?.Redo();
        CutItem.Click += (_, _) => Lanes.Cut();
        CopyItem.Click += (_, _) => Lanes.Copy();
        PasteItem.Click += (_, _) => Lanes.Paste(Lanes.Playhead);
        DuplicateItem.Click += (_, _) => Lanes.Duplicate();
        DeleteItem.Click += (_, _) => Lanes.DeleteSelection();
        SelectAllItem.Click += (_, _) => Lanes.SelectAll();
        ZoomSlider.ValueChanged += (_, e) => { if (!syncingScroll) Lanes.Axis.Zoom = ZoomFromSlider(e.NewValue); };
        ZoomInItem.Click += (_, _) => Lanes.Axis.ZoomAt(1.25, 0);
        ZoomOutItem.Click += (_, _) => Lanes.Axis.ZoomAt(0.8, 0);
        CollapseAllItem.Click += (_, _) => CollapseAll();
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
            compileCancel?.Cancel();
            frameTimer.Stop();
            UiTheme.Current.Changed -= OnThemeChanged;
            inspector?.Close();
            audio?.Dispose();
        };

        // Pick mode: while the inspector's Pick is on, a click selects the part under it instead of acting on it.
        AddHandler(PointerPressedEvent, OnPickPressed, RoutingStrategies.Tunnel);
        UiTheme.Current.Changed += OnThemeChanged;
        ApplyTheme();
        if (showInspector) Opened += (_, _) => ToggleInspector();
        InspectorItem.Click += (_, _) => ToggleInspector();

        UpdateStatus();
        if (path is not null) Open(path);
    }

    /// <summary>Opens a map. Errors are shown in the status bar, never thrown.</summary>
    public void Open(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var bytes = File.ReadAllBytes(full);
            var file = OsuFile.Parse(bytes);
            var mapFolder = MapFolder.FromDirectory(Path.GetDirectoryName(full)!);
            var map = Decompiler.Decompile(file, mapFolder);
            var editing = new EditSession(file, mapFolder, map);

            compileCancel?.Cancel();
            if (session is not null) session.Changed -= OnEdited;
            (mapPath, originalBytes, folder, session, lastCompile, closeConfirmed, flash) = (full, bytes, mapFolder, editing, null, false, null);

            // Load every drawable lane's sample, not only the ones the map uses, so new hits sound at once.
            audio?.Load(map with { Lanes = editing.Lanes }, mapFolder, skin.Value);
            model = new ArrangeModel(editing, file, audio?.SilentLanes ?? new HashSet<Lane>());
            if (audio is not null) audio.IsAudible = model.IsAudible;
            editing.Changed += OnEdited;
            model.PreviewMutesChanged += UpdateCues;
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
            flash = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
            UpdateStatus();
        }
    }

    internal ArrangeView ArrangeArea => Lanes;

    private async Task PickMap()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a map",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("osu! map") { Patterns = ["*.osu"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } local) Open(local);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Typing in a text box (volume value, offset…) must not trigger editor shortcuts like 5 (Mute) or Backspace (delete hits).
        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            base.OnKeyDown(e);
            return;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift), alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Home: Seek(0); break;
            // Cycle and locators (dawsu / Cubase keys).
            case Key.Divide or Key.Oem2 or Key.L when !ctrl && !shift: ToggleCycle(); break;
            case Key.P when alt: PlayLocatorRange(); break;
            case Key.P when !ctrl: LocatorsToSelection(); break;
            case Key.NumPad1 when ctrl: Lanes.SetLocators(Lanes.Playhead, Lanes.LocatorRight ?? Lanes.Playhead); break;
            case Key.NumPad2 when ctrl: Lanes.SetLocators(Lanes.LocatorLeft ?? Lanes.Playhead, Lanes.Playhead); break;
            case Key.NumPad1: if (Lanes.LocatorLeft is { } l) Seek(l); break;
            case Key.NumPad2: if (Lanes.LocatorRight is { } r) Seek(r); break;
            case Key.NumPad0: StopPressed(); break;
            case Key.H when !ctrl: Lanes.Axis.ZoomAt(1.25, Lanes.Axis.X(Lanes.Playhead)); break;
            case Key.G when !ctrl: Lanes.Axis.ZoomAt(0.8, Lanes.Axis.X(Lanes.Playhead)); break;
            case Key.O when ctrl: _ = PickMap(); break;
            case Key.S when ctrl: Save(); break;
            case Key.I when ctrl && shift: ToggleInspector(); break;

            // Tools (dawsu numbering) and snapping.
            case Key.D1 when !ctrl: SetTool(Tool.Select); break;
            case Key.D3 when !ctrl: SetTool(Tool.Draw); break;
            case Key.D4 when !ctrl: SetTool(Tool.Erase); break;
            case Key.D5 when !ctrl: SetTool(Tool.Mute); break;
            case Key.D6 when !ctrl: SetTool(Tool.Ramp); break;
            case Key.J when !ctrl:
                Lanes.SnapOn = !Lanes.SnapOn;
                Lanes.InvalidateVisual();
                UpdateStatus();
                break;

            // Editing.
            case Key.Z when ctrl && shift: session?.Redo(); break;
            case Key.Z when ctrl: session?.Undo(); break;
            case Key.Y when ctrl: session?.Redo(); break;
            case Key.C when ctrl: Lanes.Copy(); break;
            case Key.X when ctrl: Lanes.Cut(); break;
            case Key.V when ctrl: Lanes.Paste(Lanes.Playhead); break;
            case Key.D when ctrl: Lanes.Duplicate(); break;
            case Key.A when ctrl && shift: Lanes.ClearSelection(); break;
            case Key.A when ctrl: Lanes.SelectAll(); break;
            case Key.Delete or Key.Back: Lanes.DeleteSelection(); break;
            case Key.Escape: Lanes.ClearSelection(); break;
            case Key.Left: Lanes.Nudge(-1, fine: alt, toGrid: ctrl); break;
            case Key.Right: Lanes.Nudge(1, fine: alt, toGrid: ctrl); break;
            case Key.Up when ctrl: Lanes.ShiftSelectedLanes(-1); break;
            case Key.Down when ctrl: Lanes.ShiftSelectedLanes(1); break;
            default: base.OnKeyDown(e); return;
        }

        e.Handled = true;
    }

    private void SetTool(Tool tool)
    {
        Lanes.Tool = tool;
        SelectTool.IsChecked = tool == Tool.Select;
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
        Lanes.SelectWhere(h => h.Anchor is { } k && state.Volumes[k] >= min && state.Volumes[k] <= max);
    }

    /// <summary>P: the locators span the selection (its first to last hit), or the visible range when nothing is selected.</summary>
    private void LocatorsToSelection()
    {
        if (Lanes.Selection.Count > 0)
            Lanes.SetLocators(Lanes.Selection.Min(h => h.Time), Lanes.Selection.Max(h => h.Time));
        else
            Lanes.SetLocators(Lanes.Axis.Left, Lanes.Axis.Left + Lanes.Axis.VisibleDuration);
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
        Lanes.PruneSelection();
        model.RebuildNotes();
        UpdateCues();
        Title = $"{(session.IsEdited ? "• " : "")}{Path.GetFileNameWithoutExtension(mapPath)} — osu! Hitsound Editor";
        closeConfirmed = false;
        flash = null;
        UpdateStatus();
        ScheduleCompile();
    }

    /// <summary>Compiles the wanted sounds on a copy of the original in the background, 150 ms after the last edit.</summary>
    private void ScheduleCompile()
    {
        if (session is null || originalBytes is null || folder is null) return;
        compileCancel?.Cancel();
        var cancel = compileCancel = new CancellationTokenSource();
        var state = session.State; // immutable: the wish list is built off the UI thread
        var (bytes, mapFolder, editing) = (originalBytes, folder, session);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150, cancel.Token);
            var copy = OsuFile.Parse(bytes);
            var result = Compiler.Apply(copy, mapFolder, editing.Wanted(state));
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
    private void Save()
    {
        if (session is null || mapPath is null || originalBytes is null || folder is null) return;
        try
        {
            int unanchored = session.Unanchored.Count();
            var saved = MapSaver.Save(mapPath, originalBytes, folder, session.Wanted(), MapSaver.DefaultBackupRoot());
            var result = saved.Compile;
            if (!saved.Written)
            {
                flash = "Nothing to save: the map already plays like this." + Problems(result.Conflicts.Count, unanchored);
            }
            else
            {
                // Reload so the editor starts again from what is now on disk (and its undo history starts fresh).
                double left = Lanes.Axis.Left, zoom = Lanes.Axis.Zoom;
                Open(mapPath);
                (Lanes.Axis.Zoom, Lanes.Axis.Left) = (zoom, left);
                flash = string.Join(' ', [
                    $"Saved. {result.TimingPointsAdded} green line{(result.TimingPointsAdded == 1 ? "" : "s")} added, {result.TimingPointsChanged} changed."
                        + Problems(result.Conflicts.Count, unanchored),
                    .. result.Notes, $"Backup: {saved.Backup}"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        Resources["ButtonBg"] = B("button.bg");
        Resources["ButtonText"] = B("button.text");
        Resources["ButtonActive"] = B("button.active");
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
        rows[1].Height = new GridLength(t.Number("control.height"));
        rows[2].Height = new GridLength(t.Number("edit.height"));
        rows[3].Height = new GridLength(t.Number("overview.height"));
        bool showVolume = VolumeToggle.IsChecked == true;
        BottomPanel.IsVisible = showVolume;
        rows[5].Height = new GridLength(showVolume ? t.Number("volume.height") : 0);
        rows[6].Height = new GridLength(t.Number("status.height"));
        HScroll.Margin = new Avalonia.Thickness(ArrangeView.GridLeft, 0, 0, 0);

        Lanes.ScrollY = Lanes.ScrollY;
        Lanes.InvalidateVisual();
        Overview.InvalidateVisual();
        Volume.InvalidateVisual();
        SyncScrollBars();
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
        double visibleRows = Math.Max(0, Lanes.Bounds.Height - ArrangeView.HeaderHeight);
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
            LcdMeter.Text = $"{red.Meter}/4 /{divisor}";
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
        if (model is null)
        {
            StatusLeft.Text = flash ?? (audioError is null ? "Open a .osu file (Ctrl+O)." : $"No sound: {audioError}");
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
        string tool = $"{Lanes.Tool}{(Lanes.SnapOn ? $", snap 1/{Lanes.Divisor}" : ", snap off")}";
        string selection = Lanes.Selection.Count switch
        {
            0 => "",
            1 => $"  ·  selected: {Describe(Lanes.Selection.First())}",
            int n => $"  ·  {n} hits selected",
        };
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

    private static IReadOnlyDictionary<string, byte[]> LoadSkin()
    {
        try
        {
            return DefaultSkin.FindLazerResources() is { } dll ? DefaultSkin.LoadSamples(dll) : new Dictionary<string, byte[]>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            return new Dictionary<string, byte[]>();
        }
    }
}
