using Avalonia.Controls.Primitives;
using Avalonia.Input;
using OsuHitsound.Format;

namespace OsuHitsound.App;

/// <summary>
/// The transport and song controls, as in Hitsound mode: L/R locators and Cycle, Stop, return-on-stop, follow, speed,
/// global volume (shared with Hitsound mode), and the song's volume, mute, solo and offset.
/// </summary>
public partial class KeysoundEditor
{
    private double? locatorLeft, locatorRight;
    private double playStartedAt;
    private bool songSoloed;
    private bool showingGlobalVolume;
    private static readonly double[] Speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5];
    private readonly List<(ToggleButton Button, double Speed)> speedButtons = [];

    /// <summary>The global volume changed here (20–100): the window keeps Hitsound mode's slider and the preferences in step.</summary>
    public event Action<double>? GlobalVolumeChanged;

    /// <summary>Preferences → Song offset for all maps, added to the project's own offset.</summary>
    public double GlobalSongOffset { get; set; }

    private void SetupTransport()
    {
        GoLeftButton.Click += (_, _) => { if (locatorLeft is { } at) Seek(at); };
        GoRightButton.Click += (_, _) => { if (locatorRight is { } at) Seek(at); };
        foreach (var box in new[] { LeftLocatorBox, RightLocatorBox })
        {
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyLocatorBoxes(); Roll.Focus(); e.Handled = true; } };
            box.LostFocus += (_, _) => ApplyLocatorBoxes();
        }

        Roll.LocatorRequested += (left, time) => SetLocator(left, time);
        Roll.CycleRangeChanged += (left, right, drawn) =>
        {
            (locatorLeft, locatorRight) = (left, right);
            if (drawn) LoopButton.IsChecked = true; // drawing a range turns cycling on, as in Cubase
            ShowLocators();
        };
        Roll.CycleToggleRequested += () => { LoopButton.IsChecked = LoopButton.IsChecked != true; ToggleCycle(); };
        LoopButton.Click += (_, _) => ToggleCycle();

        foreach (double speed in Speeds)
        {
            var button = new ToggleButton { Content = $"{speed * 100:0}%", IsChecked = speed == 1, Focusable = true, Padding = new Avalonia.Thickness(6, 4), FontSize = 12 };
            button.Classes.Add("viewToggle");
            button.Click += (_, _) => SetSpeed(speed);
            speedButtons.Add((button, speed));
            SpeedPanel.Children.Add(button);
        }

        GlobalVolumeSlider.ValueChanged += (_, e) =>
        {
            GlobalVolumeText.Text = $"{Math.Round(e.NewValue)}%";
            if (showingGlobalVolume) return;
            if (audio is not null) audio.MasterVolume = (float)(e.NewValue / 100);
            GlobalVolumeChanged?.Invoke(e.NewValue);
        };

        SongVolumeSlider.ValueChanged += (_, e) => { if (audio is not null) audio.SongVolume = (float)(e.NewValue / 100); };
        SongMuteButton.IsCheckedChanged += (_, _) => { SongRowMute.IsChecked = SongMuteButton.IsChecked; UpdateSongMute(); };
        SongRowMute.IsCheckedChanged += (_, _) => SongMuteButton.IsChecked = SongRowMute.IsChecked;
        SongRowSolo.IsCheckedChanged += (_, _) =>
        {
            songSoloed = SongRowSolo.IsChecked == true;
            UpdateSongMute();
            SchedulePlayback();
        };
        SongOffsetBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplySongOffsetBox(); Roll.Focus(); e.Handled = true; } };
        SongOffsetBox.LostFocus += (_, _) => ApplySongOffsetBox();
    }

    /// <summary>Hitsound mode's slider moved, or a project opened: show the shared global volume without echoing it back.</summary>
    public void ShowGlobalVolume(double value)
    {
        showingGlobalVolume = true;
        GlobalVolumeSlider.Value = Math.Clamp(value, GlobalVolumeSlider.Minimum, GlobalVolumeSlider.Maximum);
        GlobalVolumeText.Text = $"{Math.Round(GlobalVolumeSlider.Value)}%";
        showingGlobalVolume = false;
    }

    /// <summary>A project opened or the audio restarted: put this view's song, speed and cycle settings on the engine.</summary>
    private void ApplyTransport()
    {
        if (doc is null) return;
        SongOffsetBox.Text = doc.SongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplySongOffset();
        if (audio is null) return;
        audio.SongVolume = (float)(SongVolumeSlider.Value / 100);
        audio.MasterVolume = (float)(GlobalVolumeSlider.Value / 100);
        audio.Speed = speedButtons.FirstOrDefault(b => b.Button.IsChecked == true).Speed is var s and > 0 ? s : 1;
        UpdateSongMute();
        UpdateLoop();
    }

    private void SetSpeed(double speed)
    {
        if (audio is not null) audio.Speed = speed;
        foreach (var (button, s) in speedButtons) button.IsChecked = s == speed;
    }

    // ---- Locators and Cycle ----

    private void SetLocator(bool left, double time)
    {
        if (left) locatorLeft = Math.Max(0, time); else locatorRight = Math.Max(0, time);
        ShowLocators();
    }

    private void ApplyLocatorBoxes()
    {
        static double? Parse(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : OsuNumber.TryParseDouble(text.Trim(), out double v) && double.IsFinite(v) ? Math.Max(0, v) : double.NaN;
        var (l, r) = (Parse(LeftLocatorBox.Text), Parse(RightLocatorBox.Text));
        if (l is not double.NaN) locatorLeft = l;
        if (r is not double.NaN) locatorRight = r;
        ShowLocators();
    }

    private void ShowLocators()
    {
        string Text(double? t) => t is { } v ? v.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "";
        LeftLocatorBox.Text = Text(locatorLeft);
        RightLocatorBox.Text = Text(locatorRight);
        Roll.LocatorLeft = locatorLeft;
        Roll.LocatorRight = locatorRight;
        Roll.InvalidateVisual();
        UpdateLoop();
    }

    private void ToggleCycle()
    {
        // No range yet: as in Hitsound mode, the locators go around the selected notes first.
        if (LoopButton.IsChecked == true && (locatorLeft is null || locatorRight is null)) LocatorsToSelection();
        if (LoopButton.IsChecked == true && (locatorLeft is null || locatorRight is null))
        {
            LoopButton.IsChecked = false;
            SetStatus("Set a cycle range first: drag in the top of the ruler, or Ctrl+click it for L and Alt+click for R.");
            return;
        }

        UpdateLoop();
    }

    /// <summary>The engine repeats L→R while Cycle is on (not while recording: a take is one pass).</summary>
    private void UpdateLoop()
    {
        bool on = LoopButton.IsChecked == true && locatorLeft is not null && locatorRight is not null;
        Roll.Cycle = on;
        if (doc is not null) (doc.LocatorLeft, doc.LocatorRight, doc.Cycle) = (locatorLeft, locatorRight, on); // saved with the project
        if (audio is not null) audio.Loop = on && recorder is null ? (locatorLeft!.Value, locatorRight!.Value) : null;
    }

    // ---- Song ----

    /// <summary>The song is silent when muted, or when a track is soloed and the song isn't.</summary>
    private void UpdateSongMute()
    {
        if (audio is null || doc is null) return;
        audio.SongMuted = SongMuteButton.IsChecked == true || doc.Session.Soloed.Count > 0 && !songSoloed;
    }

    private void ApplySongOffsetBox()
    {
        if (doc is null) return;
        if (!OsuNumber.TryParseDouble(SongOffsetBox.Text ?? "", out double offset) || !double.IsFinite(offset))
        {
            SongOffsetBox.Text = doc.SongOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        offset = Math.Clamp(offset, -10000, 10000);
        if (offset == doc.SongOffset) return;
        doc.SongOffset = offset;
        ApplySongOffset();
        SetStatus($"Song offset {offset:0.#} ms for this project. Save project keeps it.");
    }

    private void ApplySongOffset()
    {
        if (doc is null) return;
        double offset = MainWindow.EffectiveSongOffset(doc.SongOffset, GlobalSongOffset);
        if (audio is not null) audio.SongOffset = offset;
        Roll.SongOffset = offset;
        Roll.InvalidateVisual();
    }
}
