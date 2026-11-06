using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using System.Security.Cryptography;
using System.Text;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private async Task RenderDrumVst()
    {
        if (renderingDrums || drumVst is null || drumVstPath is null || model is null || session is null || mapPath is null || generating || savingHs) return;
        var targetModel = model; var targetSession = session; var state = session.State; var path = mapPath;
        var plan = DrumRenderPlan.Create(state, DrumVstNotes(model), model.DrumRenders);
        if (plan.Hits.Count == 0) { flash = "No mapped drum hits to render."; UpdateStatus(); return; }
        if (session.IsGestureActive) { flash = "Finish the current edit before rendering."; UpdateStatus(); return; }
        renderingDrums = true;
        bool enabledBefore = Root.IsEnabled;
        Window? progress = null;
        string staging = Path.Combine(Path.GetTempPath(), "dawsu-drum-render-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        void PreventClose(object? sender, WindowClosingEventArgs e) { e.Cancel = true; cancellation.Cancel(); }
        Closing += PreventClose;
        try
        {
            audio?.Pause(); audio?.SetDrumInstrument(null);
            var captured = drumVst.SaveState();
            string pluginPath = drumVstPath;
            Root.IsEnabled = false;
            if (drumVstPanel is not null) drumVstPanel.IsEnabled = false;
            var cache = new DrumRenderCache(DrumRenderCache.DefaultRoot(), path);
            string fingerprint = await Task.Run(() => DrumRenderFingerprint(pluginPath, captured));
            string directory = Path.GetDirectoryName(path)!;
            var reused = plan.Keys.Select(k => (Key: k, Asset: cache.Find(fingerprint, k, directory)))
                .Where(p => p.Asset is not null).ToDictionary(p => p.Key, p => p.Asset!);
            string Parent(EditHit hit) => targetModel.DrumRenders.GetValueOrDefault(ProjectStore.KeyOf(hit.Lane))?.Parent ?? ProjectStore.KeyOf(hit.Lane);
            int files = plan.Hits.Select(p => (Parent(p.Key), p.Value)).Distinct().Count();
            var reviewRows = plan.Hits.GroupBy(p => (Parent: Parent(p.Key), Key: p.Value)).Select(g =>
                $"{targetModel.NameOf(ProjectStore.LaneOf(g.Key.Parent)!)} · note {g.Key.Key.Note} · velocity {g.Key.Key.Velocity} · {g.Count()} hits").ToArray();
            if (!await Dialogs.ReviewDrumRender(this, $"{plan.Hits.Count} mapped hits · {plan.Keys.Count} distinct note/velocity sounds\n{plan.Keys.Count - reused.Count} sounds to render · {reused.Count} cached sounds to reuse\n{files} lane WAV assignments\n\n" +
                "The current kit settings will be captured. Rendered hits play WAVs until you explicitly rerender. Timing and unrelated layers stay intact; the conversion is one Undo step.\n\n" +
                "Natural tails are included. A sound still ringing at 15 seconds stops the render with a warning. Save/Generate remain separate actions.", reviewRows)) return;
            cancellation.Token.ThrowIfCancellationRequested();
            var label = new TextBlock { Text = "Preparing captured kit…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
            cancel.Click += (_, _) => { cancellation.Cancel(); label.Text = "Cancelling…"; cancel.IsEnabled = false; };
            progress = new Window { Title = "Rendering VST drums", Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { label, cancel } }, Background = Background, RequestedThemeVariant = RequestedThemeVariant };
            progress.Closing += PreventClose;
            _ = progress.ShowDialog(this);
            Directory.CreateDirectory(staging);
            var renderedFiles = new Dictionary<DrumRenderKey, string>();
            if (reused.Count < plan.Keys.Count)
            {
                using var renderer = new Vst3Instrument(pluginPath);
                renderer.RestoreState(captured);
                await DrumVstRenderer.Warmup(renderer, cancellation.Token);
                foreach (var key in plan.Keys.Where(k => !reused.ContainsKey(k)))
                {
                    label.Text = $"Rendering {renderedFiles.Count + 1} / {plan.Keys.Count - reused.Count}\nMIDI note {key.Note} · velocity {key.Velocity}";
                    string output = Path.Combine(staging, $"{key.Note}-{key.Velocity}.wav");
                    await DrumVstRenderer.Render(renderer, key, output, cancellation.Token);
                    renderedFiles[key] = output;
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(targetSession, session) || !ReferenceEquals(state, targetSession.State) || mapPath != path)
                throw new InvalidOperationException("The map changed during rendering. Retry; no assignments were changed.");
            using var install = new DrumRenderInstall(directory);
            var assets = new Dictionary<(string Parent, DrumRenderKey Key), DrumRenderAsset>();
            var assignments = new Dictionary<EditHit, Lane>();
            var metadata = new Dictionary<string, DrumRenderSource>();
            foreach (var (hit, key) in plan.Hits)
            {
                string parent = Parent(hit);
                if (!assets.TryGetValue((parent, key), out var asset))
                {
                    string wav = renderedFiles.GetValueOrDefault(key) ?? Path.Combine(directory, reused[key].FileName);
                    asset = install.Install(key, wav, parent); assets[(parent, key)] = asset;
                }
                var lane = new Lane(LaneSource.BeatmapFile, asset.FileName);
                assignments[hit] = lane; metadata[ProjectStore.KeyOf(lane)] = new(parent, key.Note, key.Velocity);
            }
            // Decode before applying the edit; an unreadable result cannot leave partial assignments.
            foreach (var lane in assignments.Values.Distinct()) audio?.LoadLane(lane, File.ReadAllBytes(Path.Combine(directory, lane.Name)));
            targetModel.ApplyDrumRender(assignments, metadata);
            install.Commit();
            string? cacheWarning = null;
            try { cache.Remember(fingerprint, assets.Values.DistinctBy(a => a.Key)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { cacheWarning = $" Cache could not be saved: {ex.Message}"; }
            RefreshFolder(); ScheduleProjectSave(); SyncScrollBars();
            flash = $"Rendered {plan.Hits.Count} drum hits into {assets.Count} WAV assignments. Save or Generate to export; Undo restores the previous hits." + cacheWarning;
        }
        catch (OperationCanceledException) { flash = "Drum rendering cancelled. No assignments changed."; }
        catch (Exception ex) { flash = $"Drum rendering stopped: {ex.Message}"; }
        finally
        {
            if (progress is not null) { progress.Closing -= PreventClose; progress.Close(); }
            Closing -= PreventClose;
            if (Directory.Exists(staging))
                foreach (string file in Directory.EnumerateFiles(staging))
                    try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Root.IsEnabled = enabledBefore;
            if (drumVstPanel is not null) drumVstPanel.IsEnabled = true;
            renderingDrums = false;
            RefreshDrumVstRouting(); refreshDrumVstControls?.Invoke(); UpdateStatus();
        }
    }

    private static string DrumRenderFingerprint(string plugin, byte[] state)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("dawsu-drum-render-v1-44100-512-noteoff100ms\n" + plugin));
        hash.AppendData(state);
        IEnumerable<string> files = Directory.Exists(plugin) ? Directory.EnumerateFiles(plugin, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.OrdinalIgnoreCase) : [plugin];
        foreach (string file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file));
            using var input = File.OpenRead(file); hash.AppendData(SHA256.HashData(input));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
