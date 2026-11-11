using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private Vst3Instrument? drumVst;
    private Window? drumVstPanel;
    private TextBlock? drumVstStatus;
    private DispatcherTimer? drumVstTimer;
    private string? drumVstName;
    private string? drumVstError;
    private Action? refreshDrumVstControls;
    private bool drumVstEnabled = true;
    private bool drumVstReady;
    private string? drumVstPath;
    private bool renderingDrums;
    // A SoundFont preset as the drum kit, instead of a VST3: plays in this process, no editor window or saved state.
    private SoundFontDrumKit? drumKit;
    private OsuHitsound.Lanes.Keysound.KeyInstrument? drumKitPreset;

    private void SetupDrumVst()
    {
        DrumVstItem.Click += (_, _) => ShowDrumVst();
        SetupDrumKitMapping();
    }

    internal static Dictionary<Lane, int> DrumVstNotes(ArrangeModel model)
    {
        var notes = new Dictionary<Lane, int>();
        foreach (var lane in model.LaneRows.Select(r => r.Lane))
            if (lane.Source == LaneSource.Skin && lane.Sample is { } sample)
            {
                int? note = sample.Sound switch { "hitnormal" => 36, "hitclap" => 38, "hitwhistle" => 42, "hitfinish" => 49, _ => null };
                if (note is { } n) notes[lane] = n;
            }
        // First assigned alias wins: aliases of a sample continue to play one sound.
        var assigned = new HashSet<Lane>();
        foreach (var row in model.ActiveDrumMap?.Rows ?? [])
            if (row.LaneKey is { } key && ProjectStore.LaneOf(key) is { } lane && assigned.Add(lane))
            {
                if (row.MidiNote is { } note) notes[lane] = note;
                else notes.Remove(lane);
            }
        foreach (var lane in notes.Keys.Where(l => model.DrumRenders.ContainsKey(ProjectStore.KeyOf(l))).ToArray()) notes.Remove(lane);
        return notes;
    }

    private void RefreshDrumVstRouting()
    {
        var notes = model is null ? new Dictionary<Lane, int>() : DrumVstNotes(model);
        IDrumInstrument? live = drumKit is not null ? drumKit : drumVstReady ? drumVst : null;
        audio?.SetDrumInstrument(!renderingDrums && drumVstEnabled ? live : null, notes);
        if (drumVstStatus is not null)
            drumVstStatus.Text = drumVstError ?? (drumKitPreset is { } kit ? $"{kit.DisplayName} · {(drumVstEnabled ? $"Live · {notes.Count} mapped lanes" : "Sample playback")}"
                : drumVst is null ? "No instrument loaded. Choose an installed VST3 instrument or a SoundFont kit."
                : $"{drumVstName} · {(!drumVstReady ? "Load a kit, then click Start live" : drumVstEnabled ? $"Live · {notes.Count} mapped lanes" : "Sample playback")}");
    }

    private void ShowDrumVst()
    {
        if (drumVstPanel is not null) { drumVstPanel.Activate(); return; }
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "Drum instrument", FontSize = 20, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "Play a VST3 instrument or a SoundFont (.sf2) kit from the existing hitsound lanes.", TextWrapping = TextWrapping.Wrap });
        var choose = new Button { Content = "Choose VST3…" };
        var chooseKit = new Button { Content = "Choose SoundFont kit…" };
        var kitEffects = new CheckBox { Content = "SoundFont reverb and chorus", IsChecked = drumKitPreset?.Effects != false };
        var open = new Button { Content = "Open instrument" };
        var unload = new Button { Content = "Unload" };
        var startLive = new Button { Content = "Start live" };
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { choose, chooseKit, open, startLive, unload }) { button.Margin = new Thickness(0, 0, 8, 4); controls.Children.Add(button); }
        body.Children.Add(controls);
        var enabled = new CheckBox { Content = "Use the instrument for mapped drum lanes", IsChecked = drumVstEnabled };
        body.Children.Add(enabled);
        body.Children.Add(kitEffects);
        var stateControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var loadState = new Button { Content = "Load instrument state…" };
        var saveState = new Button { Content = "Save instrument state…" };
        stateControls.Children.Add(loadState); stateControls.Children.Add(saveState); body.Children.Add(stateControls);
        var mapping = new Button { Content = "Edit lane MIDI assignments…" };
        var kitMap = new Button { Content = "Map lanes from instrument…" };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mapping, kitMap } });
        var render = new Button { Content = "Render drums to WAVs…" };
        body.Children.Add(render);
        render.Click += async (_, _) => await RenderDrumVst();
        drumVstStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        body.Children.Add(drumVstStatus);
        body.Children.Add(new TextBlock { Text = "Default hitsounds: normal → Kick 36 · clap → Snare 38\nwhistle → Closed hat 42 · finish → Crash 49 (MIDI channel 10).\nDrum-map assignments override these defaults. Volume controls MIDI velocity.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        body.Children.Add(new TextBlock { Text = "Render VST drums to assign WAVs for Save/Generate. Rendered hits play their WAVs until you explicitly rerender them. Save/Generate never starts a VST render.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        drumVstPanel = new Window { Title = "Drum instrument — dawsu!", Width = 570, SizeToContent = SizeToContent.Height,
            MinWidth = 520, CanResize = false, Content = body, Background = Background, RequestedThemeVariant = RequestedThemeVariant };
        drumVstPanel.Closed += (_, _) => { drumVstPanel = null; drumVstStatus = null; refreshDrumVstControls = null; };
        void Refresh()
        {
            open.IsEnabled = loadState.IsEnabled = saveState.IsEnabled = drumVst is not null;
            unload.IsEnabled = drumVst is not null || drumKit is not null;
            startLive.IsEnabled = drumVst is not null && !drumVstReady;
            mapping.IsEnabled = model is not null;
            kitMap.IsEnabled = model is not null && drumVst is not null && !renderingDrums;
            render.IsEnabled = model is not null && (drumKit is not null || drumVst is not null && drumVstReady) && !renderingDrums;
            kitEffects.IsVisible = drumKit is not null;
            kitEffects.IsChecked = drumKitPreset?.Effects != false;
            RefreshDrumVstRouting();
        }
        choose.Click += async (_, _) =>
        {
            var panel = drumVstPanel!;
            var choices = Vst3Picker.Installed();
            string browse = "Browse for another VST3 file…";
            string? selected = await Dialogs.Choose(panel, "Choose drum instrument", "Installed VST3 plug-ins (choose an instrument):", [.. choices.Keys, browse], accept: "Load");
            if (selected is null || drumVstPanel is null) return;
            string? path = selected == browse ? await Vst3Picker.Browse(panel) : choices.GetValueOrDefault(selected);
            if (path is null || drumVstPanel is null) return;
            choose.IsEnabled = false;
            bool loaded = false;
            try { LoadDrumVst(path); loaded = true; }
            catch (Exception ex) { ReportDrumVstError(ex); }
            finally { choose.IsEnabled = true; Refresh(); }
            if (loaded) await OfferKitMappingAfterLoad(); // a known kit at once; Kontakt and the like once a kit is loaded in them
        };
        chooseKit.Click += async (_, _) =>
        {
            var panel = drumVstPanel!;
            if (await SoundFontPicker.Browse(panel, "Choose a SoundFont drum kit") is not { } file || drumVstPanel is null) return;
            if (await SoundFontPicker.ChoosePreset(panel, file, drumKitPreset, message => { drumVstError = message; RefreshDrumVstRouting(); }, drums: true) is not { } preset) return;
            chooseKit.IsEnabled = false;
            try { await LoadDrumKit(preset); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException) { ReportDrumVstError(ex); }
            finally { chooseKit.IsEnabled = true; Refresh(); }
        };
        kitEffects.IsCheckedChanged += async (_, _) =>
        {
            if (drumKitPreset is not { } kit || kit.Effects == (kitEffects.IsChecked == true)) return;
            try { await LoadDrumKit(kit with { Effects = kitEffects.IsChecked == true }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException) { ReportDrumVstError(ex); }
            finally { Refresh(); }
        };
        open.Click += (_, _) => { try { drumVst?.ShowEditor(); } catch (Exception ex) { ReportDrumVstError(ex); } };
        startLive.Click += (_, _) =>
        {
            try { drumVst!.EnableRealtime(); drumVstReady = true; drumVstError = null; }
            catch (Exception ex) { ReportDrumVstError(ex); }
            Refresh();
        };
        unload.Click += (_, _) => { drumVstError = null; UnloadDrumVst(); UnloadDrumKit(); Refresh(); };
        enabled.IsCheckedChanged += (_, _) => { drumVstEnabled = enabled.IsChecked == true; RefreshDrumVstRouting(); };
        mapping.Click += async (_, _) => { await ShowDrumMapSetup(); Refresh(); };
        kitMap.Click += async (_, _) => { await MapLanesFromDrumKit(onLoad: false); Refresh(); };
        loadState.Click += async (_, _) =>
        {
            var files = await drumVstPanel!.StorageProvider.OpenFilePickerAsync(new() { Title = "Load instrument state", FileTypeFilter = [new("Instrument state") { Patterns = ["*.bin"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            try
            {
                using var input = File.OpenRead(path);
                if (input.Length is <= 0 or > Vst3Instrument.MaxStateBytes) throw new IOException("Instrument state must be between 1 byte and 64 MB.");
                var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
                audio?.Pause(); audio?.SetDrumInstrument(null);
                drumVstReady = false;
                drumVst!.RestoreState(bytes);
                drumVst.EnableRealtime(); drumVstReady = true; drumVstError = null;
            }
            catch (Exception ex) { ReportDrumVstError(ex); }
            finally { Refresh(); }
        };
        saveState.Click += async (_, _) =>
        {
            var file = await drumVstPanel!.StorageProvider.SaveFilePickerAsync(new() { Title = "Save instrument state", SuggestedFileName = "drums-state.bin", DefaultExtension = "bin", ShowOverwritePrompt = true });
            if (file?.TryGetLocalPath() is not { } path) return;
            try { audio?.Pause(); audio?.SetDrumInstrument(null); File.WriteAllBytes(path, drumVst!.SaveState()); }
            catch (Exception ex) { ReportDrumVstError(ex); }
            finally { Refresh(); }
        };
        refreshDrumVstControls = Refresh;
        Refresh(); drumVstPanel.Show(this);
    }

    /// <summary>A SoundFont preset as the drum kit (it replaces a loaded VST3). The file is read off the UI thread.</summary>
    internal async Task LoadDrumKit(OsuHitsound.Lanes.Keysound.KeyInstrument preset)
    {
        var synth = await Task.Run(() => SoundFontSynth.Create(preset));
        audio?.Pause(); UnloadDrumVst();
        drumVstError = null;
        drumKit = new SoundFontDrumKit(synth);
        drumKitPreset = preset;
        RefreshDrumVstRouting();
    }

    private void UnloadDrumKit()
    {
        audio?.SetDrumInstrument(null);
        drumKit = null; drumKitPreset = null;
        RefreshDrumVstRouting();
    }

    internal void LoadDrumVst(string path, byte[]? state = null)
    {
        audio?.Pause(); UnloadDrumVst(); UnloadDrumKit();
        drumVstError = null;
        try
        {
            drumVst = new Vst3Instrument(path);
            drumVstPath = Path.GetFullPath(path);
            if (state is not null) drumVst.RestoreState(state);
            try { drumVst.EnableRealtime(); drumVstReady = true; }
            catch (InvalidOperationException) when (state is null) { drumVstReady = false; }
            drumVstName = Path.GetFileNameWithoutExtension(path);
            drumVstTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            drumVstTimer.Tick += (_, _) =>
            {
                try
                {
                    drumVst?.Pump(1);
                    if (audio?.DrumInstrumentError is { } error) { UnloadDrumVst(); throw new InvalidOperationException(error); }
                }
                catch (Exception ex) { UnloadDrumVst(); ReportDrumVstError(ex); }
            };
            drumVstTimer.Start(); RefreshDrumVstRouting(); drumVst.ShowEditor();
        }
        catch { UnloadDrumVst(); throw; }
    }

    private void ReportDrumVstError(Exception ex)
    {
        flash = $"Drums VST: {ex.Message}";
        drumVstError = flash;
        if (drumVstStatus is not null) drumVstStatus.Text = flash;
        UpdateStatus();
    }

    private void UnloadDrumVst()
    {
        drumVstTimer?.Stop(); drumVstTimer = null;
        audio?.SetDrumInstrument(null); // waits for any audio callback before native destruction
        drumVst?.Dispose(); drumVst = null; drumVstName = null; drumVstReady = false;
        RefreshDrumVstRouting();
        refreshDrumVstControls?.Invoke();
    }
}
