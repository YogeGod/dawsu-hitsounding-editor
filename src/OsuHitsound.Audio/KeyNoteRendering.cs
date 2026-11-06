using System.Security.Cryptography;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Audio;

/// <summary>
/// When a rendered note's tail counts as finished: its output stays below <see cref="ThresholdDb"/> for
/// <see cref="QuietMs"/> after the key and pedal let go. A tail still sounding <see cref="MaxTailMs"/> later is cut and
/// reported (<see cref="RenderedAudio.Truncated"/>), so Generate can ask for a longer limit instead of cutting silently.
/// </summary>
public sealed record RenderLimits(double ThresholdDb = -80, double QuietMs = 250, double MaxTailMs = 10_000)
{
    public float Threshold => (float)Math.Pow(10, ThresholdDb / 20);
    public long QuietFrames => Math.Max(1, (long)Math.Round(QuietMs * PlaybackMixer.Rate / 1000));
    public long MaxTailFrames => Math.Max(1, (long)Math.Round(MaxTailMs * PlaybackMixer.Rate / 1000));
}

/// <summary>Rendered stereo audio at <see cref="PlaybackMixer.Rate"/> (interleaved); <see cref="Truncated"/> when the tail hit the limit.</summary>
public sealed record RenderedAudio(float[] Stereo, bool Truncated)
{
    public int Frames => Stereo.Length / 2;
}

/// <summary>Renders single notes of one track's instrument, off the real-time path (Generate).</summary>
public interface IKeyNoteRenderer : IDisposable
{
    Task<RenderedAudio> RenderAsync(KeyRenderJob job, RenderLimits limits, CancellationToken cancel = default);
}

/// <summary>The bundled piano, one fresh player per note (no state carries over).</summary>
public sealed class PianoNoteRenderer(SfzInstrument instrument) : IKeyNoteRenderer
{
    private const int Block = 512;

    public Task<RenderedAudio> RenderAsync(KeyRenderJob job, RenderLimits limits, CancellationToken cancel = default) => Task.Run(() =>
    {
        var player = new KeysoundPlayer(instrument);
        // The piano can't read pedal events: the pedal holds the key until it lets go, as in playback.
        var bends = job.Controllers.Where(c => c.Kind == ControllerKind.PitchBend)
            .Select(c => new PlayedBend(c.Time, c.Value / 8192.0 * instrument.PitchBendRange)).ToArray();
        player.SetNotes([new PlayedNote(0, Math.Max(job.Held, 1), job.Pitch, job.Velocity, 0)], [bends]);

        double msPerFrame = 1000.0 / PlaybackMixer.Rate;
        long releaseFrame = (long)Math.Ceiling(job.Held / msPerFrame);
        long max = releaseFrame + limits.MaxTailFrames;
        var output = new List<float>();
        var block = new float[Block * 2];
        long frame = 0, lastLoud = -1;
        float threshold = limits.Threshold;
        bool truncated = true;
        while (frame < max)
        {
            cancel.ThrowIfCancellationRequested();
            int n = (int)Math.Min(Block, max - frame);
            Array.Clear(block);
            player.Render(block, n, frame * msPerFrame, msPerFrame, playing: true);
            for (int i = 0; i < n; i++)
            {
                output.Add(block[i * 2]);
                output.Add(block[i * 2 + 1]);
                if (Math.Max(Math.Abs(block[i * 2]), Math.Abs(block[i * 2 + 1])) >= threshold) lastLoud = frame + i;
            }

            frame += n;
            if (frame > releaseFrame && frame - 1 - Math.Max(lastLoud, releaseFrame) >= limits.QuietFrames)
            {
                truncated = false;
                break;
            }
        }

        long keep = truncated ? frame : Math.Max(lastLoud, releaseFrame) + 1;
        return new RenderedAudio([.. output.Take((int)(keep * 2))], truncated);
    }, cancel);

    public void Dispose() { }
}

/// <summary>A VST3 instrument on its own offline host (<see cref="IsolatedVst3.Offline"/>), started with the track's settings.</summary>
public sealed class VstNoteRenderer(IsolatedVst3 host) : IKeyNoteRenderer
{
    public IsolatedVst3 Host => host;

    public async Task<RenderedAudio> RenderAsync(KeyRenderJob job, RenderLimits limits, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        var (events, last) = Events(job);
        return await host.RenderAsync(events, limits, last);
    }

    /// <summary>The note as MIDI frames: the controllers in force first, the note on at 0, its note-off and the later changes.</summary>
    internal static (List<(long Frame, byte Status, byte Data1, byte Data2)> Events, long Last) Events(KeyRenderJob job)
    {
        static long Frame(double ms) => Math.Max(0, (long)Math.Round(ms * PlaybackMixer.Rate / 1000));
        var events = new List<(long Frame, byte Status, byte Data1, byte Data2)>();
        foreach (var c in job.Controllers.Where(c => c.Time <= 0))
        {
            var (s, a, b) = KeysoundMidi.Controller(c.Kind, c.Value);
            events.Add((0, s, a, b));
        }

        events.Add((0, KeysoundMidi.NoteOn, (byte)job.Pitch, (byte)Math.Clamp(job.Velocity, 1, 127)));
        long off = Math.Max(1, Frame(job.Length));
        var later = new List<(long Frame, byte Status, byte Data1, byte Data2)> { (off, KeysoundMidi.NoteOff, (byte)job.Pitch, 0) };
        foreach (var c in job.Controllers.Where(c => c.Time > 0))
        {
            var (s, a, b) = KeysoundMidi.Controller(c.Kind, c.Value);
            later.Add((Frame(c.Time), s, a, b));
        }

        events.AddRange(later.OrderBy(e => e.Frame)); // stable: a note-off before a pedal change at the same frame
        return (events, Math.Max(off, Frame(job.Held)));
    }

    public void Dispose() => host.Dispose();
}

/// <summary>Rendered audio as keysound WAV files: 16-bit stereo PCM at 44.1 kHz.</summary>
public static class KeyWav
{
    /// <summary>The WAV bytes for interleaved stereo, scaled by <paramref name="gain"/> and clipped to full scale.</summary>
    public static byte[] Encode16(ReadOnlySpan<float> stereo, float gain = 1)
    {
        int frames = stereo.Length / 2, dataBytes = frames * 4;
        var wav = new byte[44 + dataBytes];
        var w = new BinaryWriter(new MemoryStream(wav));
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)2);
        w.Write(PlaybackMixer.Rate);
        w.Write(PlaybackMixer.Rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        for (int i = 0; i < frames * 2; i++)
            w.Write((short)Math.Round(Math.Clamp(stereo[i] * gain, -1f, 1f) * 32767));
        w.Flush();
        return wav;
    }

    /// <summary>Several renders played together (a chord osu! can't layer natively), each with its own gain.</summary>
    public static float[] Mix(IReadOnlyList<(float[] Stereo, float Gain)> parts)
    {
        var mix = new float[parts.Count == 0 ? 0 : parts.Max(p => p.Stereo.Length)];
        foreach (var (stereo, gain) in parts)
            for (int i = 0; i < stereo.Length; i++) mix[i] += stereo[i] * gain;
        return mix;
    }

    /// <summary>Equal files share one name: a hash of the bytes.</summary>
    public static string Hash(byte[] wav) => Convert.ToHexString(SHA256.HashData(wav));

    /// <summary>A gain in dB as a factor.</summary>
    public static float Gain(double db) => (float)Math.Pow(10, db / 20);
}
