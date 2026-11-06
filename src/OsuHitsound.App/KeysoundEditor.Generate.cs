using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App;

/// <summary>
/// Generate: renders every synced note of the included tracks on its own (offline: VST3 instruments on their own render
/// hosts, started with their current settings), writes the [Keysounds] mapset into the project's output folder all or
/// nothing, then offers a .osz.
/// </summary>
public partial class KeysoundEditor
{
    private bool generating;

    private async Task Generate()
    {
        if (doc is not { } project || TopLevel.GetTopLevel(this) is not Window owner || generating) return;
        if (project.SongPath is null || !File.Exists(project.SongPath))
        {
            SetStatus("The song audio is missing from this project, so Generate can't make a playable map.");
            return;
        }

        StopRecording();
        var tracks = KeyGenerate.Tracks(project).Where(t => t.Synced.Count > 0 || t.Unsynced + t.Stale > 0).ToList();
        if (tracks.Sum(t => t.Synced.Count) == 0)
        {
            SetStatus("Nothing to generate yet: Sync notes to the gameplay objects first (only tracks with G on are included).");
            return;
        }

        // A required instrument that isn't playing stops Generate before anything renders.
        foreach (var t in tracks.Where(t => t.Synced.Count > 0 && t.Track.Instrument.IsVst3))
            if (PartFor(t.Track) is not { Status: PartStatus.Ready })
            {
                SelectTrack(t.Track.Id);
                SetStatus($"Generate needs {t.Track.Name}'s instrument ({t.Track.Instrument.DisplayName}), which isn't loaded. Fix or replace it first.");
                return;
            }

        if (await Dialogs.KeysoundGenerate(owner, tracks) is not { } limits || !ReferenceEquals(doc, project)) return;
        Stop();
        generating = true;
        var progress = new Dialogs.Progress(owner, "Generate keysounds");
        string cache = Path.Combine(project.Folder, $"output.render-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(cache);
            var rendered = await Render(project, tracks, limits, cache, progress);
            if (rendered is null) { SetStatus("Generate was cancelled. The previous output is unchanged."); return; }
            var (soundsAt, truncated) = rendered.Value;
            if (truncated > 0 && !await Dialogs.Confirm(owner, "Long tails",
                    $"{truncated} note{(truncated == 1 ? " was" : "s were")} still sounding {limits.MaxTailMs / 1000:0.#} s after release and " +
                    "would be cut there. Generate with the cut tails, or go back and allow longer tails?", "Generate anyway", "Go back"))
            {
                SetStatus("Generate stopped: some tails were longer than the limit. The previous output is unchanged.");
                return;
            }

            progress.Report("Writing the [Keysounds] difficulty…", 1);
            string referencePath = Path.Combine(project.Folder, KeysoundStore.ReferenceFolder, project.File.Reference);
            var plan = KeyGenerate.Plan(project.Reference, referencePath, soundsAt);
            if (plan.Conflicts.Count > 0)
            {
                var c = plan.Conflicts[0];
                SetStatus($"Generate stopped at {TimeSpan.FromMilliseconds(Math.Max(0, c.Time)):m\\:ss\\.fff}: {c.Reason} The previous output is unchanged.");
                return;
            }

            string output = await Task.Run(() => KeyGenerate.Write(project, plan, f => f.Sounds.Count switch
            {
                0 => BlankSample.Bytes,
                1 => File.ReadAllBytes(Path.Combine(cache, f.Sounds[0] + ".wav")),
                _ => KeyWav.Encode16(KeyWav.Mix([.. f.Sounds.Select(id => (KeyWav.Decode16(File.ReadAllBytes(Path.Combine(cache, id + ".wav"))), 1f))])),
            }));
            progress.Close();

            int notes = soundsAt.Values.Sum(s => s.Count), samples = plan.Files.Count(f => f.Sounds.Count > 0);
            static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
            SetStatus($"Generated {Count(notes, "keysound")} ({Count(samples, "sample file")}) into {output}.");
            if (await Dialogs.Confirm(owner, "Generated",
                    $"[Keysounds] is in the project's output folder: {Count(notes, "note")} as {Count(samples, "sample file")}. Export it as a .osz for osu! to import?",
                    "Export .osz…", "Not now"))
                await ExportOsz(owner, project, output, plan.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Generate was cancelled. The previous output is unchanged.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            SetStatus($"Generate failed: {ex.Message} The previous output is unchanged.");
        }
        finally
        {
            generating = false;
            progress.Close();
            try { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Renders the synced notes into <paramref name="cache"/> (one WAV per distinct sound, named by its hash) and returns
    /// the sounds at each gameplay position and how many tails hit the limit; null when cancelled.
    /// </summary>
    private async Task<(Dictionary<(int Object, int Node), IReadOnlyList<string>> SoundsAt, int Truncated)?> Render(
        KeysoundDocument project, IReadOnlyList<KeyGenerateTrack> tracks, RenderLimits limits, string cache, Dialogs.Progress progress)
    {
        var soundsAt = new Dictionary<(int, int), List<string>>();
        int total = tracks.Sum(t => t.Synced.Count), done = 0, truncated = 0;
        foreach (var t in tracks.Where(t => t.Synced.Count > 0))
        {
            progress.Report($"Starting {t.Track.Name}'s instrument…", (double)done / total);
            using var renderer = await RendererFor(t.Track);
            float gain = KeyWav.Gain(t.Track.VolumeDb);
            var piano = renderer is PianoNoteRenderer ? new Dictionary<string, (string Id, bool Truncated)>() : null; // the piano is deterministic
            foreach (var note in t.Synced)
            {
                if (progress.Token.IsCancellationRequested) return null;
                var job = KeyRender.Job(t.Track, note, project.Tempo);
                string id;
                bool cut;
                if (piano?.TryGetValue(job.Key, out var known) == true) (id, cut) = known;
                else
                {
                    var audio = await renderer.RenderAsync(job, limits, progress.Token);
                    var wav = KeyWav.Encode16(audio.Stereo, gain);
                    id = KeyWav.Hash(wav);
                    string file = Path.Combine(cache, id + ".wav");
                    if (!File.Exists(file)) await File.WriteAllBytesAsync(file, wav, progress.Token);
                    cut = audio.Truncated;
                    if (piano is not null) piano[job.Key] = (id, cut);
                }

                if (cut) truncated++;
                var target = note.Target!;
                if (!soundsAt.TryGetValue((target.ObjectIndex, target.Node), out var list)) soundsAt[(target.ObjectIndex, target.Node)] = list = [];
                list.Add(id);
                done++;
                if (done % 8 == 0 || done == total) progress.Report($"Rendering {t.Track.Name}: {done} of {total} notes…", (double)done / total);
            }
        }

        return (soundsAt.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value), truncated);
    }

    /// <summary>A renderer for a track: the bundled piano, or the track's plug-in on its own offline host with its current settings.</summary>
    private async Task<IKeyNoteRenderer> RendererFor(KeyTrack track)
    {
        if (!track.Instrument.IsVst3)
            return new PianoNoteRenderer(await (piano ??= Task.Run(() => SfzInstrument.Load(SfzInstrument.BundledPianoPath))));
        // The live plug-in's settings now (tweaks not saved yet included), else the saved ones.
        string? state = track.Instrument.State;
        if (PartFor(track)?.Vst is { } live)
            try { state = await live.GetStateAsync(); }
            catch (InvalidOperationException) { }
        var host = await IsolatedVst3.StartAsync(track.Instrument.Path!, track.Instrument.ClassId, state, offline: true);
        return new VstNoteRenderer(host);
    }

    private async Task ExportOsz(Window owner, KeysoundDocument project, string output, string osuName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export .osz",
            SuggestedFileName = Path.GetFileNameWithoutExtension(osuName) + ".osz",
            DefaultExtension = "osz",
            FileTypeChoices = [new FilePickerFileType("osu! beatmap archive") { Patterns = ["*.osz"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            await Task.Run(() => KeyGenerate.ExportOsz(output, path));
            SetStatus($"Exported {Path.GetFileName(path)}. Open it with osu! to import the [Keysounds] map.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Couldn't export the .osz: {ex.Message}");
        }
    }
}
