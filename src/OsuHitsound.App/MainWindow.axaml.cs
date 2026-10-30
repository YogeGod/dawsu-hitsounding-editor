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
/// The main screen (docs/ui-design.md, variation G): opens a .osu, shows its lanes and plays it. View and play only;
/// nothing is ever written to the map folder.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly int[] Divisors = [1, 2, 4, 8, 16, 3, 6];

    private readonly AudioEngine? audio;
    private readonly string? audioError;
    private readonly Lazy<IReadOnlyDictionary<string, byte[]>> skin = new(LoadSkin);
    private readonly DispatcherTimer frameTimer;
    private ArrangeModel? model;
    private bool syncingScroll;
    private InspectorWindow? inspector;
    private double lastDrawnPlayhead = double.NaN;
    private double lastRowHeight = UiTheme.Current.Number("row.height");

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
        LoopButton.IsCheckedChanged += (_, _) => UpdateLoop();
        LaneDetailsToggle.IsCheckedChanged += (_, _) => { Lanes.ShowIndexes = LaneDetailsToggle.IsChecked == true; Lanes.InvalidateVisual(); };
        VolumeToggle.IsCheckedChanged += (_, _) => ApplyTheme();
        VolumeTab.IsCheckedChanged += (_, _) => { Volume.IsVisible = VolumeTab.IsChecked == true; SavedText.IsVisible = !Volume.IsVisible; };
        SnapBox.SelectionChanged += (_, _) => UpdateLcd();
        ZoomSlider.ValueChanged += (_, e) => { if (!syncingScroll) Lanes.Axis.Zoom = ZoomFromSlider(e.NewValue); };
        ZoomInItem.Click += (_, _) => Lanes.Axis.ZoomAt(1.25, 0);
        ZoomOutItem.Click += (_, _) => Lanes.Axis.ZoomAt(0.8, 0);
        CollapseAllItem.Click += (_, _) => CollapseAll();
        HScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.Axis.Left = HScroll.Value; };
        VScroll.Scroll += (_, _) => { if (!syncingScroll) Lanes.ScrollY = VScroll.Value; SyncScrollBars(); };

        frameTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => OnFrame());
        frameTimer.Start();

        Closed += (_, _) =>
        {
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
            var file = OsuFile.Read(full);
            var folder = MapFolder.FromDirectory(Path.GetDirectoryName(full)!);
            var map = Decompiler.Decompile(file, folder);

            audio?.Load(map, folder, skin.Value);
            model = new ArrangeModel(map, file, audio?.SilentLanes ?? new HashSet<Lane>());
            if (audio is not null) audio.IsAudible = model.IsAudible;

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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            StatusLeft.Text = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}";
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
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Home: Seek(0); break;
            case Key.Divide or Key.Oem2: LoopButton.IsChecked = LoopButton.IsChecked != true; break;
            case Key.H: Lanes.Axis.ZoomAt(1.25, Lanes.Axis.X(Lanes.Playhead)); break;
            case Key.G: Lanes.Axis.ZoomAt(0.8, Lanes.Axis.X(Lanes.Playhead)); break;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control): _ = PickMap(); break;
            case Key.I when e.KeyModifiers.HasFlag(KeyModifiers.Control | KeyModifiers.Shift): ToggleInspector(); break;
            default: base.OnKeyDown(e); return;
        }

        e.Handled = true;
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

    private void TogglePlay()
    {
        if (audio is null || model is null) return;
        if (audio.IsPlaying) audio.Pause();
        else audio.Play();
        PlayButton.Content = audio.IsPlaying ? "⏸" : "▶";
    }

    /// <summary>Cubase's Stop: stops; pressed again while stopped, goes to the start.</summary>
    private void StopPressed()
    {
        if (audio is null) return;
        if (audio.IsPlaying) audio.Pause();
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
        if (audio is null) return;
        audio.Loop = LoopButton.IsChecked == true ? (Lanes.Axis.Left, Lanes.Axis.Left + Lanes.Axis.VisibleDuration) : null;
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
            if (audio.IsPlaying && LoopButton.IsChecked != true)
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
            StatusLeft.Text = audioError is null ? "Open a .osu file (Ctrl+O)." : $"No sound: {audioError}";
            StatusRight.Text = "";
            return;
        }

        var map = model.Map;
        var notes = new List<string>();
        if (audioError is not null) notes.Add($"No sound: {audioError}");
        else if (audio is { HasSong: false }) notes.Add($"Song file \"{map.Settings.AudioFilename}\" not found; hitsounds play on their own.");
        int silent = model.Silent.Count;
        if (silent > 0) notes.Add($"{silent} lane{(silent == 1 ? "" : "s")} with no sound");
        if (map.Warnings.Count > 0) notes.Add($"{map.Warnings.Count} warning{(map.Warnings.Count == 1 ? "" : "s")}: {map.Warnings[0]}");
        StatusLeft.Text = notes.Count > 0 ? string.Join("  ·  ", notes) : "Nothing to save yet: saving arrives with the compiler.";
        ToolTip.SetTip(StatusLeft, map.Warnings.Count > 0 ? string.Join("\n", map.Warnings.Take(30)) : null);

        Volume.SelectedMoment = Lanes.SelectedMoment;
        Volume.InvalidateVisual();
        int hits = map.Moments.Sum(m => m.Hits.Count);
        string selection = Lanes.SelectedMoment is int i and >= 0 && i < map.Moments.Count
            ? $"  ·  selected: {Describe(map.Moments[i])}"
            : "";
        StatusRight.Text = $"{model.LaneRows.Count} lanes  ·  {map.Moments.Count} moments  ·  {hits} hits{selection}";
    }

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
        return $"{where} at {FormatTime(m.Time)}, volume {m.Volume}, {string.Join(" + ", m.Hits.Select(h => h.Lane.Name))}";
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
