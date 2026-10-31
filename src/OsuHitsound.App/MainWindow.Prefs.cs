using Avalonia;
using Avalonia.Controls;
using OsuHitsound.Audio;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>Preferences: audio output, the default-sounds source, recent maps, the window's place, and F1 help.</summary>
public partial class MainWindow
{
    private HelpWindow? help;

    private Lazy<IReadOnlyDictionary<string, byte[]>> NewSkin()
    {
        var preferred = prefs.LazerResources;
        return new(() => LoadSkin(preferred));
    }

    private void StartAudio()
    {
        try
        {
            audio = AudioEngine.Create(prefs.AudioDevice, prefs.BufferMs);
            audioError = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or BadImageFormatException)
        {
            audio = null;
            audioError = ex.Message;
        }
    }

    private void SavePrefs()
    {
        if (!usePreferences) return;
        try
        {
            prefs.Save(Preferences.DefaultPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            flash = $"Couldn't save preferences: {ex.Message}";
            UpdateStatus();
        }
    }

    private void ApplyWindowPlacement()
    {
        if (prefs is { WindowWidth: > 200 and var w, WindowHeight: > 150 and var h })
            (Width, Height) = (w, h);
        if (prefs is { WindowX: { } x, WindowY: { } y })
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)x, (int)y);
        }

        // Kept on close, together with everything else in the preferences.
        Closed += (_, _) =>
        {
            prefs = prefs with
            {
                WindowX = Position.X, WindowY = Position.Y, WindowWidth = Bounds.Width, WindowHeight = Bounds.Height,
                SnapMode = Lanes.Edits.SnapMode, QuantizeStrength = Lanes.Edits.QuantizeStrength, ColourByVolume = Lanes.ColourByVolume,
                InspectorOpen = InspectorToggle.IsChecked == true,
                InspectorWidth = InspectorWidthToSave,
                InspectorCards = [.. Inspector.Order], InspectorFolded = [.. Inspector.Folded],
            };
            SavePrefs();
        };
    }

    /// <summary>Puts an opened map first in the recent list.</summary>
    private void Remember(string path)
    {
        prefs = prefs.WithRecent(path);
        SavePrefs();
    }

    private void FillRecent()
    {
        prefs = prefs.WithoutMissing(File.Exists);
        RecentItem.ItemsSource = prefs.RecentMaps.Count == 0
            ? [new MenuItem { Header = "(none yet)", IsEnabled = false }]
            : prefs.RecentMaps.Select(path =>
            {
                var item = new MenuItem { Header = Path.GetFileNameWithoutExtension(path) };
                ToolTip.SetTip(item, path);
                item.Click += (_, _) => Open(path);
                return item;
            }).ToList();
    }

    /// <summary>Puts the preferences' keys in force (snapshots and tests keep the built-in ones) and shows them in the menus.</summary>
    private void ApplyKeys()
    {
        if (usePreferences)
        {
            var (keys, problem) = Keymap.For(prefs);
            EditorCommands.Use(keys);
            // No Cubase file and none chosen is normal (no Cubase installed): the built-in keys are used quietly.
            if (problem is not (null or Keymap.NoCubaseFile))
            {
                flash = problem;
                UpdateStatus();
            }
        }

        foreach (var (item, id) in menuCommands) item.InputGesture = EditorCommands.Get(id).Keys.FirstOrDefault();
        help?.Close();
    }

    internal void ShowHelpForSnapshot() => ShowHelp();

    private void ShowHelp()
    {
        if (help is { IsVisible: true })
        {
            help.Activate();
            return;
        }

        help = new HelpWindow();
        help.Show(this);
    }

    private async Task ShowPreferences()
    {
        var devices = audio is null && audioError is not null ? [(-1, "System default")] : AudioEngine.Devices();
        if (await Dialogs.EditPreferences(this, prefs with { SongsFolder = prefs.SongsFolder ?? Preferences.FindSongsFolder() }, devices) is not { } edited)
            return;

        bool audioChanged = edited.AudioDevice != prefs.AudioDevice || edited.BufferMs != prefs.BufferMs;
        bool skinChanged = edited.LazerResources != prefs.LazerResources;
        prefs = edited;
        SavePrefs();
        ApplyKeys();
        if (skinChanged) skin = NewSkin();
        if (audioChanged || skinChanged) RestartAudio();
    }

    /// <summary>Starts the audio output again with the current preferences and reloads the open map's sounds; edits are kept.</summary>
    private void RestartAudio()
    {
        var (speed, offset) = (audio?.Speed ?? 1, audio?.SongOffset ?? 0);
        if (audio is { IsPlaying: true }) StopPlaying();
        audio?.Dispose();
        StartAudio();
        if (audio is not null && model is not null && doc is { } document && session is not null && folder is not null)
        {
            audio.Load(document.Map with { Lanes = session.Lanes }, folder, skin.Value);
            foreach (var lane in model.LaneRows.Select(r => r.Lane).Where(l => l.Source == LaneSource.BeatmapFile && !session.Lanes.Contains(l)))
                if (folder.Find(lane.Name) is { } f) audio.LoadLane(lane, folder.ReadBytes(f));
            audio.IsAudible = model.IsAudible;
            model.Silent = audio.SilentLanes; // which lanes have no sound may differ with other default sounds
            Lanes.InvalidateVisual();
            (audio.Speed, audio.SongOffset, audio.SongVolume) = (speed, offset, (float)(SongVolumeSlider.Value / 100));
            UpdateSongMute();
            UpdateCues();
        }

        flash = audio is null ? $"No sound: {audioError}" : "Audio output restarted.";
        UpdateStatus();
    }
}
