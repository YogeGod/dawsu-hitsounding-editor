using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Keysound mode (<see cref="KeysoundEditor"/>): entered from the start screen's Keysounding panel or a recent
/// .dawsuks project. While it shows, the hitsound view stays underneath untouched; leaving reopens the map that was open.
/// </summary>
public partial class MainWindow
{
    private string? hsMapBeforeKeysound;

    private void SetupKeysound()
    {
        Keys.CloseRequested += async () => { if (await MayLeaveKeysound()) await CloseKeysound(); };
        SetupKeysoundMenu();
        // One global volume for both modes: Keysound's slider drives Hitsound mode's (which saves it in the preferences).
        Keys.GlobalVolumeChanged += value => GlobalVolumeSlider.Value = value;
        Keys.SetInspectorLayout(prefs.KeysoundCards, prefs.KeysoundFolded, prefs.KeysoundInspectorOpen);
        Keys.Changed += UpdateKeysoundTitle;
        // As in Hitsound mode: Open and Save in the ribbon, messages behind the title bar's details icon, and the settings
        // kept for all projects.
        Keys.OpenRequested += () => Run("open");
        Keys.StatusChanged += text =>
        {
            keysoundMessage = text.Length > 0 ? text : null;
            RefreshWorkflowFeedback();
        };
        Keys.ViewSettings = (prefs.KeysoundInsertVelocity, prefs.KeysoundAudition, prefs.KeysoundSnapMode, prefs.KeysoundTriplets, prefs.KeysoundController);
        Keys.QuantizeStrength = prefs.QuantizeStrength;
        Keys.ViewSettingsChanged += () =>
        {
            prefs = WithKeysoundView(prefs);
            SavePrefs();
        };
        Start.KeysoundNewRequested += () => _ = NewKeysound();
        Start.KeysoundOpenRequested += () => _ = PickKeysound();
    }

    /// <summary>Asks to save a Keysound project with unsaved edits. False when the user cancels or saving fails.</summary>
    private async Task<bool> MayLeaveKeysound()
    {
        Keys.StopRecording(); // a take in progress becomes an edit first, so the prompt covers it
        if (Keys.Document is not { Session.IsEdited: true } project) return true;
        return await Dialogs.UnsavedClose(this, $"the Keysound project {Path.GetFileName(project.Folder)}") switch
        {
            CloseChoice.Save => await SaveKeysound(),
            CloseChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Asks about both modes' unsaved edits before another project opens.</summary>
    private async Task<bool> MayLeaveForKeysound() => await MayLeaveKeysound() && (Keys.Document is not null || await MayLeaveMap());

    /// <summary>Opening a beatmap from Keysound mode first leaves it (asking about unsaved edits).</summary>
    private async Task LeaveKeysoundThen(Func<Task> next)
    {
        if (Keys.Document is not null)
        {
            if (!await MayLeaveKeysound()) return;
            hsMapBeforeKeysound = null;
            CloseKeysoundView();
        }

        await next();
    }

    private async Task NewKeysound()
    {
        if (!await MayLeaveForKeysound()) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the reference beatmap for the Keysound project",
            AllowMultiple = false,
            SuggestedStartLocation = prefs.SongsFolder is { } songs ? await StorageProvider.TryGetFolderFromPathAsync(songs) : null,
            FileTypeFilter = [new FilePickerFileType("osu! difficulty") { Patterns = ["*.osu"] }],
        });
        if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } osu) return;

        var root = KeysoundStore.DefaultRoot();
        try { Directory.CreateDirectory(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var parents = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to keep the Keysound project (a new folder is made inside)",
            AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(root) ? await StorageProvider.TryGetFolderFromPathAsync(root) : null,
        });
        if (parents.Count == 0 || parents[0].TryGetLocalPath() is not { } parent) return;

        if (prefs.SongsFolder is { } songsFolder && SafeFile.IsInside(parent, songsFolder))
        {
            ShowKeysoundMessage("Keep Keysound projects outside the osu! Songs folder: osu! would list the copied reference as another difficulty.");
            return;
        }

        var target = KeysoundStore.SuggestFolder(parent, osu);
        var warnings = new List<string>();
        string project;
        try
        {
            project = await Task.Run(() => KeysoundStore.Create(osu, target, warnings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            ShowKeysoundMessage($"Couldn't make the Keysound project: {ex.Message}");
            return;
        }

        await OpenKeysound(project, confirmed: true);
        if (Keys.Document is not null)
            Keys.SetStatus($"Made the project in {target}." + (warnings.Count > 0 ? " " + string.Join(" ", warnings) : ""));
    }

    /// <summary>Keysound projects ask about the hitsound map's edits themselves (<see cref="OpenKeysound"/>).</summary>
    private static bool IsKeysoundProject(string path) => Path.GetExtension(path).Equals(KeysoundStore.Extension, StringComparison.OrdinalIgnoreCase);

    private async Task PickKeysound()
    {
        var root = KeysoundStore.DefaultRoot();
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Keysound project",
            AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(root) ? await StorageProvider.TryGetFolderFromPathAsync(root) : null,
            FileTypeFilter = [new FilePickerFileType("Keysound project") { Patterns = ["*" + KeysoundStore.Extension] }],
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) await Open(path);
    }

    /// <param name="confirmed">The caller already asked about unsaved edits (<see cref="MayLeaveForKeysound"/>).</param>
    private async Task OpenKeysound(string path, bool confirmed = false)
    {
        if (Keys.Document is { } current && string.Equals(current.ProjectPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return;
        if (!confirmed && !await MayLeaveForKeysound()) return;

        bool withAudio = audio is not null;
        var skinSounds = skin;
        KeysoundDocument project;
        PreparedLoad? prepared;
        try
        {
            (project, prepared) = await Task.Run(() =>
            {
                var opened = KeysoundStore.Open(path);
                var load = withAudio
                    ? AudioEngine.Prepare(new DecompiledMap(opened.Settings, [], [], [], []), MapFolder.FromDirectory(Path.Combine(opened.Folder, KeysoundStore.ReferenceFolder)), skinSounds.Value)
                    : null;
                return (opened, load);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
        {
            ShowKeysoundMessage($"Couldn't open {path}: {ex.Message}");
            return;
        }

        if (Keys.Document is null)
        {
            SaveProject(); // the hitsound map's editor data, as when opening another map
            hsMapBeforeKeysound = doc is not null ? mapPath : null;
        }

        if (audio is not null && prepared is not null) audio.Apply(prepared);
        else prepared?.Dispose();
        Keys.GlobalSongOffset = prefs.GlobalSongOffset;
        Keys.ShowGlobalVolume(GlobalVolumeSlider.Value);
        Keys.Open(project, audio);
        Keys.IsVisible = true;
        HideStartScreen();
        ShowKeysoundChrome(true);
        Remember(project.ProjectPath);
        UpdateKeysoundTitle();
    }

    /// <summary>Leaves Keysound mode: back to the hitsound map that was open, or the start screen.</summary>
    private async Task CloseKeysound()
    {
        var map = hsMapBeforeKeysound;
        hsMapBeforeKeysound = null;
        CloseKeysoundView();
        if (map is not null && File.Exists(map)) await OpenAsync(map);
        else if (usePreferences)
        {
            Start.IsVisible = true;
            RefreshStartScreen();
        }
    }

    private void CloseKeysoundView()
    {
        Keys.Close();
        Keys.IsVisible = false;
        ShowKeysoundChrome(false);
        FileTitle.Text = mapPath is not null ? $"{Path.GetFileName(mapPath)} — Hitsounds" : "No map open";
        Title = mapPath is not null ? $"{Path.GetFileNameWithoutExtension(mapPath)} — dawsu! Hitsound" : "dawsu! Hitsound";
    }

    /// <summary>The latest Keysound message, shown behind the title bar's details icon while Keysound mode shows.</summary>
    private string? keysoundMessage;

    private Preferences WithKeysoundView(Preferences p)
    {
        var v = Keys.ViewSettings;
        return p with
        {
            KeysoundInsertVelocity = v.InsertVelocity, KeysoundAudition = v.Audition, KeysoundSnapMode = v.SnapMode,
            KeysoundTriplets = v.Triplets, KeysoundController = v.Controller,
        };
    }

    /// <summary>The title bar in Keysound mode: Keysound's menus instead of the hitsound ones (the key editor has its own ribbon).</summary>
    private bool keysoundChrome;

    private void ShowKeysoundChrome(bool on)
    {
        KeysoundMenu.IsVisible = on;
        HeaderMenu.IsVisible = !on;
        // The title bar's Save button serves the editor that shows: Save .osu, or the key editor's Save project.
        keysoundChrome = on;
        SaveLabel.Text = on ? "Save project" : "Save .osu";
        Avalonia.Automation.AutomationProperties.SetName(SaveButton, on ? "Save project" : "Save .osu");
        if (on)
        {
            ToolTip.SetTip(SaveButton, "Save the Keysound project (Ctrl+S): tracks, notes and instrument settings. The reference beatmap is never changed.");
            SaveBadge.IsVisible = false;
        }
        else ShowSaveState(); // the .osu tooltip and the problem badge
        if (!on) keysoundMessage = null;
        RefreshWorkflowFeedback();
    }

    /// <summary>The Keysound menu items whose Tag is a shared key-binding command, for their shortcut chips.</summary>
    private IEnumerable<(MenuItem Item, string Id)> KeysoundMenuCommands()
    {
        foreach (var item in KeysoundMenu.GetLogicalDescendants().OfType<MenuItem>())
            if (item.Tag is string tag && (tag.StartsWith("ks:", StringComparison.Ordinal) ? tag[3..] : tag) is var id
                && EditorCommands.All.Any(c => c.Id == id))
                yield return (item, id);
    }

    /// <summary>
    /// The Keysound menus: each item's Tag names its command, "ks:" for the key editor's own (see
    /// <see cref="KeysoundEditor.CommandIds"/>), otherwise the window's (open, save, preferences, help).
    /// </summary>
    private void SetupKeysoundMenu()
    {
        foreach (var top in KeysoundMenu.Items.OfType<MenuItem>())
        {
            foreach (var item in top.Items.OfType<MenuItem>())
                item.Click += async (_, _) =>
                {
                    switch (item.Tag as string)
                    {
                        case { } ks when ks.StartsWith("ks:", StringComparison.Ordinal): await Keys.RunCommand(ks[3..]); break;
                        case "save": await SaveKeysound(); break;
                        case { } id: Run(id); break;
                    }
                };

            // Greyed out when they can't run, and the switches show their state.
            top.SubmenuOpened += (_, _) =>
            {
                foreach (var item in top.Items.OfType<MenuItem>())
                    if (item.Tag is string tag && tag.StartsWith("ks:", StringComparison.Ordinal)) item.IsEnabled = Keys.CanRun(tag[3..]);
                KsGhostsItem.IsChecked = Keys.GhostsShown;
                KsObjectLinesItem.IsChecked = Keys.ObjectLinesShown;
                KsInspectorPanelItem.IsChecked = Keys.InspectorLayout.Open;
                KsControllerItem.IsChecked = Keys.ViewSettings.Controller;
                KsReturnOnStopItem.IsChecked = Keys.ReturnOnStop;
                KsFollowItem.IsChecked = Keys.FollowPlayback;
            };
        }

        // Start playing from, as Hitsound mode's Playback menu.
        foreach (var (choice, start) in new[]
                 {
                     (KsStartCursorChoice, PlayStart.Cursor), (KsStartLeftChoice, PlayStart.LeftLocator),
                     (KsStartSelectionChoice, PlayStart.Selection), (KsStartSelectionOrLeftChoice, PlayStart.SelectionOrLeft),
                 })
            choice.IsCheckedChanged += (_, _) => { if (choice.IsChecked == true) Keys.StartFrom = start; };
    }

    private void UpdateKeysoundTitle()
    {
        if (Keys.Document is not { } project) return;
        string name = Path.GetFileName(project.Folder);
        FileTitle.Text = $"{name} — Keysound{(project.Session.IsEdited ? " (unsaved)" : "")}";
        ToolTip.SetTip(TitleChip, project.ProjectPath);
        Title = $"{name} — dawsu! Keysound";
    }

    private bool savingKeysound;

    /// <summary>Saves the Keysound project, with each VST3 instrument's current settings.</summary>
    private async Task<bool> SaveKeysound()
    {
        if (Keys.Document is not { } project) return false;
        if (savingKeysound)
        {
            Keys.SetStatus("Still saving: the instruments are reporting their settings.");
            return false;
        }

        savingKeysound = true;
        IReadOnlyList<string> unanswered;
        try { unanswered = await Keys.CaptureInstrumentStates(); }
        finally { savingKeysound = false; }
        if (!ReferenceEquals(Keys.Document, project)) return false;
        try
        {
            KeysoundStore.Save(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Keys.SetStatus($"Couldn't save the project: {ex.Message}");
            return false;
        }

        Keys.SetStatus($"Saved {Path.GetFileName(project.Folder)}." + (unanswered.Count == 0 ? ""
            : $" {string.Join(", ", unanswered)}: the instrument didn't report its settings, so the ones saved before were kept."));
        UpdateKeysoundTitle();
        return true;
    }

    private void ShowKeysoundMessage(string message)
    {
        if (Keys.Document is not null) Keys.SetStatus(message);
        else if (Start.IsVisible) Start.SetMessage(message);
        else
        {
            flash = message;
            UpdateStatus();
        }
    }

    /// <summary>Keys while Keysound shows: Save, help and preferences stay global; the rest go to the key editor.</summary>
    private bool KeysoundKey(KeyEventArgs e)
    {
        if (Keys.Document is null) return false;
        var command = EditorCommands.Match(e);
        if (command?.Id == "save") { _ = SaveKeysound(); return true; }
        if (command?.Id is "help" or "preferences" or "inspector" or "open") return false;
        var focused = FocusManager?.GetFocusedElement();
        bool ownKey = focused is TextBox
            || focused is Button or Avalonia.Controls.Primitives.ToggleButton && e.Key is Key.Space or Key.Enter
            || focused is ComboBox or Slider && e.Key is Key.Space or Key.Enter or Key.Escape or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.Home or Key.End or Key.PageUp or Key.PageDown;
        if (ownKey) return false;
        return Keys.HandleKey(e) || command is not null; // hitsound commands do nothing here
    }
}
