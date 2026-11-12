using System.Globalization;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private static readonly FilePickerFileType MidiType = new("MIDI file") { Patterns = ["*.mid", "*.midi"] };

    private async void ImportMidi_Click(object? sender, RoutedEventArgs e)
    {
        if (session is not { } editing || model is not { } arrange) { MidiStatus("Open a map before importing MIDI."); return; }
        if (editing.State.MidiSource is not null) { MidiStatus("This project already has a MIDI source. Undo its import before importing another; combining source metadata is not supported."); return; }
        if (arrange.TimeSignatures.Count != 0) { MidiStatus("Clear the editor time-signature overrides before importing MIDI. MIDI signatures are retained separately."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import MIDI", AllowMultiple = false, FileTypeFilter = [MidiType] });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("MIDI file is too large (maximum 32 MB).");
            var source = MidiFile.Read(await File.ReadAllBytesAsync(path));
            if (source.Notes.Count == 0) throw new InvalidDataException("The MIDI file has no notes to import.");
            if (editing != session || arrange != model) return;
            var offsetText = await Dialogs.Prompt(this, "Import MIDI", "Timeline offset in milliseconds (0 or greater). MIDI tempo and other events move with the notes.", "0");
            if (offsetText is null) return;
            if (!double.TryParse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset) || !double.IsFinite(offset) || offset < 0)
                throw new InvalidDataException("The MIDI offset must be a non-negative number of milliseconds.");
            var available = arrange.LaneRows.Select(r => r.Lane).Distinct().ToList();
            var mappings = new Dictionary<(int Channel, int Note), Lane>();
            foreach (var key in source.Notes.Select(n => (n.Channel, n.Note)).Distinct().OrderBy(k => k.Channel).ThenBy(k => k.Note))
            {
                var labels = available.Select(l => $"{arrange.NameOf(l)}  [{ProjectStore.KeyOf(l)}]").ToList();
                if (labels.Count == 0) throw new InvalidDataException("There are more MIDI channel/note combinations than available lanes. Add lanes first.");
                var label = await Dialogs.Choose(this, "Map MIDI to lanes", $"Choose a unique lane for channel {key.Channel}, note {key.Note}:", labels);
                if (label is null) return;
                var lane = available[labels.IndexOf(label)];
                mappings.Add(key, lane);
                available.Remove(lane);
            }
            if (editing != session || arrange != model) return;
            var shifted = ShiftMidi(source, offset);
            var hits = PrepareMidiImport(editing, shifted, mappings);
            editing.ImportMidi(hits, shifted, mappings.Select(kv => (kv.Value, new MidiLaneMapping(kv.Key.Note, kv.Key.Channel))));
            MidiStatus($"Imported {hits.Count} MIDI notes. MIDI metadata is saved with the editor project.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException or NotSupportedException)
        { MidiStatus($"Couldn't import MIDI: {ex.Message}"); }
    }

    private async void ExportMidi_Click(object? sender, RoutedEventArgs e)
    {
        if (session is not { } editing || model is not { } arrange || doc is not { } document) { MidiStatus("Open a map before exporting MIDI."); return; }
        if (arrange.TimeSignatures.Count != 0) { MidiStatus("MIDI export cannot represent editor time-signature overrides yet. Clear them to export; original MIDI signatures are retained."); return; }
        try
        {
            var mappings = editing.State.MidiLanes.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var lane in editing.State.Hits.Where(h => editing.MidiOf(h) is null).Select(h => h.Lane).Distinct())
            {
                if (mappings.ContainsKey(lane)) continue;
                var text = await Dialogs.Prompt(this, "MIDI lane mapping", $"Enter note and channel for {arrange.NameOf(lane)} (note 0–127, channel 1–16), separated by a space. New notes use 100 ms length and the hit volume as velocity.", "60 10");
                if (text is null) return;
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out int note) || !int.TryParse(parts[1], out int channel) || note is < 0 or > 127 || channel is < 1 or > 16)
                    throw new InvalidDataException("The lane mapping needs a note from 0–127 and channel from 1–16.");
                mappings[lane] = new MidiLaneMapping(note, channel);
            }
            if (editing != session || arrange != model) return;
            var midi = BuildMidiExport(editing, document.File, mappings);
            var bytes = MidiFile.Write(midi); // validate before opening or truncating a destination
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export MIDI", SuggestedFileName = "hitsounds.mid", DefaultExtension = "mid", FileTypeChoices = [MidiType] });
            if (file is null) return;
            if (file.TryGetLocalPath() is not { } destination || Path.GetExtension(destination).ToLowerInvariant() is not (".mid" or ".midi"))
                throw new InvalidDataException("Choose a local destination ending in .mid or .midi.");
            WriteMidiFile(destination, bytes);
            if (editing == session) foreach (var (lane, mapping) in mappings) if (!editing.State.MidiLanes.ContainsKey(lane)) editing.SetMidiLane(lane, mapping);
            MidiStatus($"Exported {midi.Notes.Count} MIDI notes.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException or NotSupportedException or FormatException)
        { MidiStatus($"Couldn't export MIDI: {ex.Message}"); }
    }

    private void MidiStatus(string message) { flash = message; UpdateStatus(); }

    internal static MidiDocument ShiftMidi(MidiDocument source, double offset) => source with
    {
        Notes = [.. source.Notes.Select(n => n with { Time = n.Time + offset })],
        Tempos = [.. source.Tempos.Select(t => t with { Time = t.Time + offset })],
        OtherEvents = [.. source.OtherEvents.Select(x => x with { Time = x.Time + offset })],
    };

    internal static List<(EditHit Hit, MidiHitData Midi)> PrepareMidiImport(EditSession editing, MidiDocument source, IReadOnlyDictionary<(int Channel, int Note), Lane> mappings)
    {
        var seen = editing.State.Hits.ToHashSet();
        var result = new List<(EditHit, MidiHitData)>();
        foreach (var n in source.Notes)
        {
            var hit = editing.HitAt(mappings[(n.Channel, n.Note)], n.Time);
            if (Math.Abs(hit.Time - n.Time) > 0.000001)
                throw new InvalidDataException($"A note at {n.Time:0.###} ms would snap to a nearby map object at {hit.Time:0.###} ms. Move the import offset to preserve its timing.");
            if (!seen.Add(hit)) throw new InvalidDataException($"Two notes or an existing hit occupy the same lane at {hit.Time:0.###} ms. Choose another lane or offset.");
            result.Add((hit, new MidiHitData(n.Track, n.Channel, n.Note, n.Velocity, n.ReleaseVelocity, n.Duration) { StartOrder = n.StartOrder, EndOrder = n.EndOrder }));
        }
        return result;
    }

    internal static MidiDocument BuildMidiExport(EditSession editing, OsuFile file, IReadOnlyDictionary<Lane, MidiLaneMapping> mappings)
    {
        var notes = editing.State.Hits.Select(h =>
        {
            var m = editing.MidiOf(h);
            if (m is not null) return new MidiNote(m.Track, m.Channel, m.Note, m.Velocity, m.ReleaseVelocity, h.Time, m.Duration) { StartOrder = m.StartOrder, EndOrder = m.EndOrder };
            var lane = mappings[h.Lane];
            return new MidiNote(0, lane.Channel, lane.Note, Math.Clamp((int)Math.Round(editing.VolumeOf(h) * 127.0 / 100), 1, 127), 0, h.Time, 100);
        }).ToArray();
        if (editing.State.MidiSource is { } source) return source with { Notes = notes };
        var reds = file.TimingPoints.Where(p => p.Uninherited && p.BeatLength > 0).Select(p => (Time: p.Time + editing.Map.Settings.TimeOffset, p.BeatLength, p.Meter)).OrderBy(p => p.Time).ToList();
        if (reds.Count == 0) throw new InvalidDataException("The map has no usable tempo points for MIDI export.");
        var first = reds.LastOrDefault(p => p.Time <= 0);
        if (first == default) first = reds[0];
        var tempos = new List<MidiTempo> { Tempo(0, first.BeatLength) };
        tempos.AddRange(reds.Where(p => p.Time > 0).Select(p => Tempo(p.Time, p.BeatLength)));
        var signatures = new List<MidiPreservedEvent> { Signature(0, first.Meter) };
        signatures.AddRange(reds.Where(p => p.Time > 0).Select(p => Signature(p.Time, p.Meter)));
        return new MidiDocument(notes, tempos, 1000) { OtherEvents = signatures };
    }

    private static MidiPreservedEvent Signature(double time, int meter)
    {
        if (meter is < 1 or > 255) throw new InvalidDataException("A map meter is outside the MIDI time-signature range (1–255).");
        // osu! red-line meters count quarter notes. MIDI uses log2(denominator), clocks per click and 32nds per quarter.
        return new MidiPreservedEvent(0, time, [0xff, 0x58, 4, (byte)meter, 2, 24, 8]);
    }

    private static MidiTempo Tempo(double time, double beatLength)
    {
        double microseconds = Math.Round(beatLength * 1000);
        if (!double.IsFinite(microseconds) || microseconds is < 1 or > 0xffffff)
            throw new InvalidDataException("A map tempo is outside the MIDI tempo range.");
        return new MidiTempo(time, (int)microseconds);
    }

    internal static void WriteMidiFile(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".saving";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
