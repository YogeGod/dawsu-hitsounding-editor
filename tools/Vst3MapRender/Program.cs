using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Bass = ManagedBass.Bass;
using Errors = ManagedBass.Errors;
using NAudio.Wave;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

internal static class Program
{
    private sealed record Trigger(long Frame, int Note, int Velocity, int SourceVolume);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 4 or > 5) { Console.Error.WriteLine("Vst3MapRender <Songs-folder-or-osu-file> <Kontakt.vst3> <trusted-P5-state.bin> <new-output-folder> [--editor]"); return 2; }
        try
        {
            // Initialize the plug-in/UI runtime on the STA before parsing the map corpus.
            const int rate = 44100, block = 512;
            using var state = File.OpenRead(args[2]);
            if (state.Length is <= 0 or > Vst3Instrument.MaxStateBytes) throw new InvalidDataException("Invalid trusted state size.");
            var bytes = new byte[(int)state.Length]; state.ReadExactly(bytes);
            using var instrument = new Vst3Instrument(args[1], rate, block);
            if (args.Contains("--editor"))
            {
                instrument.ShowEditor();
                Console.WriteLine("Load GGD and select GM, then create " + args[3] + ".ready");
                var setup = Stopwatch.StartNew(); var setupBuffer = new float[block * 2];
                while (!File.Exists(args[3] + ".ready"))
                {
                    if (setup.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Kit setup not confirmed.");
                    instrument.Process(setupBuffer, block, []); instrument.Pump(10);
                }
                bytes = instrument.SaveState();
            }
            else instrument.RestoreState(bytes);
            var warmup = Stopwatch.StartNew();
            var warmupBuffer = new float[block * 2];
            while (warmup.Elapsed < TimeSpan.FromSeconds(10)) { instrument.Process(warmupBuffer, block, []); instrument.Pump(10); }
            Console.WriteLine("Kontakt kit ready; selecting a map...");
            var output = Path.GetFullPath(args[3]);
            if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "instrument-state.bin"), bytes);
            // Randomize candidates, then accept the first eligible real difficulty. Never alter it.
            var fixedMap = File.Exists(args[0]);
            var candidates = fixedMap ? new[] { Path.GetFullPath(args[0]) } : Directory.EnumerateFiles(args[0], "*.osu", new EnumerationOptions {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
            }).ToArray();
            if (!fixedMap) Random.Shared.Shuffle(candidates);
            MapDocument? document = null;
            string? original = null, sourceHash = null;
            int checkedMaps = 0;
            foreach (var candidate in candidates)
            {
                ++checkedMaps;
                try
                {
                    var map = MapDocument.Load(candidate);
                    // This fixture deliberately needs no continuous slider-loop conversion or custom assets.
                    if (map.Map.Settings.Mode != 0 || map.Map.Warnings.Count != 0 || map.Map.Passive.Count != 0 || map.Map.Moments.Count is < 100 or > 2500) continue;
                    var hits = map.Map.Moments.SelectMany(m => m.Hits).ToArray();
                    if (hits.Length == 0 || hits.Any(h => h.Lane.Source != LaneSource.Skin || h.Lane.Missing || h.Lane.Sample?.Sound is not ("hitnormal" or "hitclap" or "hitwhistle" or "hitfinish"))) continue;
                    if (new[] { "hitclap", "hitwhistle", "hitfinish" }.Any(sound => hits.Count(h => h.Lane.Sample!.Value.Sound == sound) < 20)) continue;
                    double last = map.Map.Moments.Max(m => m.Time);
                    if (last is < 30000 or > 240000 || map.Map.Moments.Any(m => m.Time < 0)) continue;
                    original = candidate;
                    sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
                    var copy = Path.Combine(output, Path.GetFileName(candidate));
                    File.Copy(original, copy, false);
                    document = MapDocument.Load(copy);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException) { }
            }
            if (document is null || original is null) throw new InvalidOperationException("No eligible standard difficulty with only default one-shot hitsounds found.");
            // Re-decompile the copy; selection never hides mapset custom samples from the eligibility check.
            var kit = DrumMapDefaults.Create().Presets[0].Rows.ToDictionary(r => r.Name, r => r.MidiNote!.Value);
            var conversion = new Dictionary<string, int> { ["hitnormal"] = kit["Kick"], ["hitclap"] = kit["Snare"], ["hitwhistle"] = kit["Closed Tip"], ["hitfinish"] = kit["Right Crash"] };
            var triggers = document.Map.Moments.SelectMany(m => m.Hits.Select(h =>
                new Trigger((long)Math.Round(m.Time * rate / 1000), conversion[h.Lane.Sample!.Value.Sound],
                    Math.Clamp((int)Math.Round(m.PlayedVolume * 127.0 / 100), 1, 127), m.Volume))).OrderBy(t => t.Frame).ToArray();
            Console.WriteLine($"Selected {original}; {triggers.Length} hits; checked {checkedMaps} candidates.");
            var events = triggers.SelectMany(t => new[] {
                (t.Frame, Event: new MidiEvent(0, 0x99, t.Note, t.Velocity)),
                (Frame: t.Frame + rate / 10, Event: new MidiEvent(0, 0x89, t.Note, 0))
            }).OrderBy(e => e.Frame).ThenBy(e => e.Event.Status).ToArray();
            var audioName = document.File.Lines.FirstOrDefault(l => l.Section == "General" && l.Text.StartsWith("AudioFilename:", StringComparison.OrdinalIgnoreCase))?.Text.Split(':', 2)[1].Trim();
            float[]? song = null;
            // Decode through the same decoder as the editor. Resolve only safe relative mapset paths.
            if (audioName is not null && MapFolder.IsInside(audioName))
            {
                var folder = MapFolder.FromDirectory(Path.GetDirectoryName(original)!);
                if (folder.Find(audioName) is { } actual)
                {
                    if (!Bass.Init(0) && Bass.LastError != Errors.Already) throw new IOException("Audio decoder initialization failed.");
                    song = SampleMixer.Decode(folder.ReadBytes(actual)) ?? throw new IOException("Song decoding failed.");
                }
            }
            using var drums = new WaveFileWriter(Path.Combine(output, "GGD-PV-drums.wav"), WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
            using var mix = song is not null ? new WaveFileWriter(Path.Combine(output, "Song-with-GGD-PV.wav"), WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) : null;
            var buffer = new float[block * 2]; var mixed = new float[block * 2];
            long lastFrame = events.Max(e => e.Frame), minimum = Math.Max(lastFrame, (song?.LongLength ?? 0) / 2);
            long maxFrame = minimum + rate * 30L, written = 0, quietFrames = 0;
            int cursor = 0; double peak = 0, mixPeak = 0, sumSquares = 0; long nonzero = 0;
            for (long pos = 0; pos < maxFrame; pos += block)
            {
                int frames = (int)Math.Min(block, maxFrame - pos), start = cursor;
                while (cursor < events.Length && events[cursor].Frame < pos + frames) ++cursor;
                var midi = events[start..cursor].Select(e => e.Event with { Offset = (int)(e.Frame - pos) }).ToArray();
                instrument.Process(buffer, frames, midi);
                double blockPeak = 0;
                for (int i = 0; i < frames * 2; ++i)
                {
                    double value = Math.Abs(buffer[i]); peak = Math.Max(peak, value); blockPeak = Math.Max(blockPeak, value); sumSquares += value * value;
                    if (value > 0.000001) ++nonzero;
                    mixed[i] = (buffer[i] + (pos * 2 + i < (song?.LongLength ?? 0) ? song![(int)(pos * 2 + i)] : 0)) * 0.5f;
                    if (!float.IsFinite(mixed[i])) throw new InvalidDataException("Non-finite audio.");
                    mixPeak = Math.Max(mixPeak, Math.Abs(mixed[i]));
                }
                drums.WriteSamples(buffer, 0, frames * 2); mix?.WriteSamples(mixed, 0, frames * 2); written += frames;
                quietFrames = blockPeak < 0.000031623 && pos > minimum ? quietFrames + frames : 0;
                if (quietFrames >= rate * 2L && pos > minimum + rate * 2L) break;
                instrument.Pump(1);
            }
            if (nonzero == 0) throw new InvalidDataException("Kontakt returned silence.");
            bool tailComplete = quietFrames >= rate * 2L;
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
            if (after != sourceHash) throw new IOException("Source map changed during validation.");
            File.WriteAllText(Path.Combine(output, "render-report.json"), JsonSerializer.Serialize(new {
                source = original, sourceHash, sourceUnchanged = true, checkedMaps, plugin = args[1], conversion,
                hitCount = triggers.Length, sampleRate = rate, seconds = written / (double)rate, peak, mixPeak,
                rms = Math.Sqrt(sumSquares / (written * 2)), nonzero, tailComplete,
                noteOnVelocity = "osu! stable played volume (minimum 8 percent) scaled to 1..127",
                mixedGain = 0.5, soundTypes = document.Map.Moments.SelectMany(m => m.Hits).GroupBy(h => h.Lane.Name).ToDictionary(g => g.Key, g => g.Count()),
                triggers = triggers.Select(t => new { ms = t.Frame * 1000.0 / rate, t.Note, t.Velocity, t.SourceVolume })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Rendered {written / (double)rate:F2}s, peak {peak:F6}, tailComplete={tailComplete}; source unchanged.");
            if (!tailComplete) throw new InvalidOperationException("Tail exceeded 30 seconds; output is marked incomplete in the report.");
            var checks = new List<object>();
            foreach (var (label, note) in new[] { ("Kick", 36), ("Snare", 38), ("Closed-Hat", 42), ("Crash", 49) })
            {
                using var check = new WaveFileWriter(Path.Combine(output, $"Check-{label}-{note}.wav"), WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
                double checkPeak = 0, energy = 0;
                for (int pos = 0; pos < rate * 10; pos += block)
                {
                    int frames = Math.Min(block, rate * 10 - pos);
                    var midi = new List<MidiEvent>();
                    if (pos == 0) midi.Add(new(0, 0x99, note, 100));
                    if (pos <= rate / 10 && pos + frames > rate / 10) midi.Add(new(rate / 10 - pos, 0x89, note, 0));
                    instrument.Process(buffer, frames, midi.ToArray());
                    for (int i = 0; i < frames * 2; ++i) { checkPeak = Math.Max(checkPeak, Math.Abs(buffer[i])); energy += buffer[i] * buffer[i]; }
                    check.WriteSamples(buffer, 0, frames * 2); instrument.Pump(1);
                }
                if (checkPeak < 0.00001) throw new InvalidDataException($"Silent GM check: {label}");
                checks.Add(new { label, note, velocity = 100, peak = checkPeak, rms = Math.Sqrt(energy / (rate * 20)) });
            }
            File.WriteAllText(Path.Combine(output, "gm-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
