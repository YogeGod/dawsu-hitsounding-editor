using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool drumAutoFillBusy;

    private async Task ShowDrumAutoFill()
    {
        if (drumAutoFillBusy || WriteBusy || renderingDrums) return;
        if (doc is not { } document || model is not { } targetModel)
        { flash = "Open a beatmap before using Drum Hits auto-fill."; UpdateStatus(); return; }
        if (document.Session.IsGestureActive) return;
        var state = document.Session.State;
        var range = Lanes.Edits.Range;
        double offset = SongOffsetInForce;
        var lanes = DrumLaneChoices().Select(p => p.Lane).Distinct().ToArray();
        if (lanes.Length == 0) { flash = "Add kick/snare sample lanes first."; UpdateStatus(); return; }
        drumAutoFillBusy = true;
        using var cancel = new CancellationTokenSource();
        Window? progress = null;
        void PreventClose(object? sender, WindowClosingEventArgs e) { e.Cancel = true; cancel.Cancel(); }
        Closing += PreventClose;
        bool Current() => ReferenceEquals(doc, document) && ReferenceEquals(model, targetModel) &&
            ReferenceEquals(document.Session.State, state) && SongOffsetInForce == offset;
        try
        {
            var options = await Dialogs.DrumAutoFill(this, lanes, targetModel.NameOf, prefs.AutoFillKickLane, prefs.AutoFillSnareLane, range);
            if (options is null) return;
            if (!Current()) throw new IOException("The map or edits changed. Run Drum Hits auto-fill again.");
            string? song = document.Map.Settings.AudioFilename;
            if (options.Stem is null && (song is null || document.Folder.Find(song) is null))
                throw new IOException("This map's song could not be found. Choose a drum stem instead.");
            byte[] ReadSource() => options.Stem is { } stem ? File.ReadAllBytes(stem) : document.Folder.ReadBytes(song!);
            audio?.Pause();
            var stop = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
            var status = new TextBlock { Text = "Analysing kick and snare locally…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            stop.Click += (_, _) => { cancel.Cancel(); status.Text = "Cancelling…"; stop.IsEnabled = false; };
            progress = new Window { Title = "Drum Hits auto-fill", Width = 420, CanResize = false, SizeToContent = SizeToContent.Height,
                Background = Background, RequestedThemeVariant = RequestedThemeVariant,
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { status, stop } } };
            progress.Closing += PreventClose;
            _ = progress.ShowDialog(this);
            byte[] source = await Task.Run(ReadSource, cancel.Token);
            byte[] fingerprint = SHA256.HashData(source);
            var detected = await DrumAutoFillDetector.Detect(source, offset,
                options.SelectedRange ? range?.Start : null, options.SelectedRange ? range?.End : null, options.ThresholdScale, cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            progress.Closing -= PreventClose;
            progress.Close(); progress = null;
            if (!Current()) throw new IOException("The map or edits changed during analysis. Nothing was applied.");
            var plan = DrumAutoFillPlan.Create(document.Session, detected.Events, options,
                options.SelectedRange ? range!.Start : double.NegativeInfinity,
                options.SelectedRange ? range!.End : double.PositiveInfinity,
                (time, divisor) => (targetModel.Snap(time, divisor),
                    targetModel.RedLineAt(time) is { } red ? red.BeatLength / divisor : double.PositiveInfinity));
            var selected = await Dialogs.ReviewDrumAutoFill(this, plan, targetModel.NameOf, () =>
            {
                try { audio?.Pause(); audio?.PreviewMix(File.ReadAllBytes(detected.AuditionPath)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { flash = ex.Message; UpdateStatus(); }
            });
            audio?.StopAudition();
            if (selected is null) { flash = "Drum auto-fill cancelled; no hits were added."; return; }
            cancel.Token.ThrowIfCancellationRequested();
            var freshSource = await Task.Run(ReadSource, cancel.Token);
            var freshMap = await File.ReadAllBytesAsync(document.Path, cancel.Token);
            if (!Current() || !document.Opened.SequenceEqual(freshMap) || !fingerprint.SequenceEqual(SHA256.HashData(freshSource)))
                throw new IOException("The map, audio, or edits changed. Nothing was applied; analyse again.");
            if (selected.Any(h => !targetModel.LaneRows.Any(r => r.Lane == h.Lane)))
                throw new IOException("A destination lane changed. Nothing was applied.");
            document.Session.Add(selected, "Drum Hits auto-fill");
            Lanes.Edits.Select(selected);
            prefs = prefs with { AutoFillKickLane = options.Kick is null ? null : ProjectStore.KeyOf(options.Kick),
                AutoFillSnareLane = options.Snare is null ? null : ProjectStore.KeyOf(options.Snare) };
            SavePrefs(); ScheduleProjectSave();
            flash = $"Added {selected.Length} kick/snare hits. Ctrl+Z undoes auto-fill. Save/Generate writes the result.";
        }
        catch (OperationCanceledException) { flash = "Drum auto-fill cancelled; no hits were added."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            flash = $"Drum auto-fill stopped: {ex.Message}";
            if (progress is not null) { progress.Closing -= PreventClose; progress.Close(); progress = null; }
            await Dialogs.Confirm(this, "Drum Hits auto-fill", flash, "OK", "Close");
        }
        finally
        {
            if (progress is not null) { progress.Closing -= PreventClose; progress.Close(); }
            audio?.StopAudition(); Closing -= PreventClose; drumAutoFillBusy = false; UpdateStatus();
        }
    }
}
