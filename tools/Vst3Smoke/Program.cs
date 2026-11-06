using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OsuHitsound.Audio;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Vst3Smoke <instrument.vst3> <new-output-directory> [--editor] [--play] [--state=<saved-state.bin>]");
            return 2;
        }
        try
        {
            var output = Path.GetFullPath(args[1]);
            // Every run is independent; never overwrite an earlier render or preset.
            if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
            Directory.CreateDirectory(output);
            const int rate = 44100, block = 512;
            using var instrument = new Vst3Instrument(args[0], rate, block);
            Console.WriteLine("Loaded instrument");
            var savedState = args.FirstOrDefault(a => a.StartsWith("--state=", StringComparison.Ordinal));
            if (savedState is not null)
            {
                using var input = File.OpenRead(savedState[8..]);
                if (input.Length is <= 0 or > Vst3Instrument.MaxStateBytes) throw new InvalidDataException("Invalid saved state size.");
                var bytes = new byte[(int)input.Length];
                input.ReadExactly(bytes);
                instrument.RestoreState(bytes);
                Console.WriteLine("Restored state from disk");
            }
            if (args.Contains("--editor"))
            {
                instrument.ShowEditor();
                Console.WriteLine($"Editor open: load the kit, then create {Path.Combine(output, "render.ready")} to start testing (10 minute limit)");
                var setupBuffer = new float[block * 2];
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(output, "render.ready")))
                {
                    if (wait.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Instrument setup was not confirmed; render skipped.");
                    instrument.Process(setupBuffer, block, []); instrument.Pump(10);
                }
            }
            else
            {
                var setupBuffer = new float[block * 2];
                var wait = Stopwatch.StartNew();
                while (wait.Elapsed < TimeSpan.FromSeconds(10)) { instrument.Process(setupBuffer, block, []); instrument.Pump(10); }
            }
            var state = instrument.SaveState();
            File.WriteAllBytes(Path.Combine(output, "instrument-state.bin"), state);
            instrument.RestoreState(state);
            Console.WriteLine($"Saved and restored {state.Length} bytes");
            using var restored = new Vst3Instrument(args[0], rate, block);
            restored.RestoreState(state);
            Console.WriteLine("Restored saved state into a new instrument instance");
            VerifyGuards(restored, block);
            // A MIDI sweep discovers sounding keys without assuming a GM drum map.
            var results = new List<object>();
            foreach (var velocity in new[] { 48, 112 })
            {
                var path = Path.Combine(output, $"midi-sweep-v{velocity}.wav");
                using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
                var buffer = new float[block * 2];
                double sum = 0, peak = 0;
                long nonzero = 0, frames = rate * 20L;
                // Notes 24..83, one every 250 ms; release after 100 ms; five seconds tail.
                var events = Enumerable.Range(24, 60).SelectMany((note, index) => new[] {
                    (Frame: (long)index * rate / 4, Status: 0x99, Note: note, Velocity: velocity),
                    (Frame: (long)index * rate / 4 + rate / 10, Status: 0x89, Note: note, Velocity: 0)
                }).OrderBy(e => e.Frame).ToArray();
                for (long pos = 0; pos < frames; pos += block)
                {
                    var count = (int)Math.Min(block, frames - pos);
                    var midi = events.Where(e => e.Frame >= pos && e.Frame < pos + count)
                        .Select(e => new MidiEvent((int)(e.Frame - pos), e.Status, e.Note, e.Velocity)).ToArray();
                    restored.Process(buffer, count, midi);
                    for (var i = 0; i < count * 2; ++i)
                    {
                        if (!float.IsFinite(buffer[i])) throw new InvalidDataException("Non-finite instrument audio");
                        var value = Math.Abs(buffer[i]); peak = Math.Max(peak, value); sum += value * value;
                        if (value > 0.000001) ++nonzero;
                    }
                    writer.WriteSamples(buffer, 0, count * 2);
                    restored.Pump(1);
                }
                var rms = Math.Sqrt(sum / (frames * 2));
                results.Add(new { velocity, peak, rms, nonzero, frames, path });
                Console.WriteLine($"Velocity {velocity}: peak={peak:F6}, RMS={rms:F6}, sounding samples={nonzero}");
                if (nonzero == 0 || peak < 0.00001) throw new InvalidDataException("Instrument returned silence; check library readiness/activation.");
                if (args.Contains("--play"))
                {
                    writer.Dispose();
                    using var reader = new WaveFileReader(path);
                    var volume = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = 0.25f };
                    using var player = new WaveOut();
                    player.Init(volume); player.Play();
                    while (player.PlaybackState == PlaybackState.Playing) instrument.Pump(25);
                }
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { plugin = args[0], stateBytes = state.Length, results }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: load, state round-trip and non-silent MIDI renders. Inspect WAVs to assess sound.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void VerifyGuards(Vst3Instrument instrument, int block)
    {
        static void Reject<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}");
        }
        Reject<ArgumentOutOfRangeException>(() => instrument.Process(new float[1], block, []));
        Reject<ArgumentOutOfRangeException>(() => instrument.Process(new float[block * 4], block + 1, []));
        Reject<ArgumentException>(() => instrument.Process(new float[block * 2], block, [new(block, 0x90, 60, 100)]));
        Reject<ArgumentException>(() => instrument.Process(new float[block * 2], block, [new(0, 0xf0, 60, 100)]));
        Reject<ArgumentOutOfRangeException>(() => instrument.RestoreState([]));
        Task.Run(() => Reject<InvalidOperationException>(() => instrument.SaveState())).GetAwaiter().GetResult();
        Console.WriteLine("PASS: 6 buffer, MIDI, state and thread guards");
    }
}
