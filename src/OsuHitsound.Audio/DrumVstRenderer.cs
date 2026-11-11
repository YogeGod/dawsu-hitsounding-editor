using System.Diagnostics;
using NAudio.Wave;
using OsuHitsound.Lanes;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

namespace OsuHitsound.Audio;

/// <summary>Owner-thread rendering; yields between blocks so cancellation and the plugin UI keep working.</summary>
public static class DrumVstRenderer
{
    public const int TailLimitSeconds = 15;
    private const int Rate = 44100, Block = 512;
    private const float Silence = 0.00001f;

    public static async Task Warmup(Vst3Instrument instrument, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew(); var buffer = new float[Block * 2];
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            cancellation.ThrowIfCancellationRequested();
            instrument.Process(buffer, Block, []); instrument.Pump(1); await Task.Delay(12, cancellation);
        }
    }

    /// <param name="fadeAtLimit">A note still ringing at the tail limit fades out over its last half second instead of
    /// failing (lane samples, where a cut cymbal is fine; Render drums keeps refusing to truncate).</param>
    public static Task Render(Vst3Instrument instrument, DrumRenderKey key, string output, CancellationToken cancellation, bool fadeAtLimit = false) =>
        RenderCore(instrument.Process, () => instrument.Pump(1), key, output, cancellation, fadeAtLimit: fadeAtLimit);

    /// <summary>The same one-shot render from a SoundFont kit: no plug-in window to keep alive, so it runs unpaced.</summary>
    public static Task Render(SoundFontDrumKit kit, DrumRenderKey key, string output, CancellationToken cancellation) =>
        RenderCore(kit.Process, () => { }, key, output, cancellation, pace: false);

    internal static async Task RenderCore(Action<float[], int, MidiEvent[]> process, Action pump, DrumRenderKey key, string output, CancellationToken cancellation, bool pace = true, bool fadeAtLimit = false)
    {
        key.Validate();
        var buffer = new float[Block * 2];
        // Flush previous notes/effect tails before the next isolated one-shot.
        var panic = Enumerable.Range(0, 128).Select(n => new MidiEvent(0, 0x89, n, 0))
            .Concat([new MidiEvent(0, 0xb9, 120, 0), new MidiEvent(0, 0xb9, 123, 0)]).ToArray();
        int quiet = 0;
        for (int f = 0; quiet < Rate; f += Block)
        {
            if (f >= Rate * TailLimitSeconds) throw new IOException("The previous drum tail did not stop within 15 seconds. No assignments were changed.");
            cancellation.ThrowIfCancellationRequested();
            process(buffer, Block, f == 0 ? panic : []);
            quiet = buffer.All(s => Math.Abs(s) < Silence) ? quiet + Block : 0;
            pump(); if (pace) await Task.Delay(12, cancellation);
        }
        using var writer = new WaveFileWriter(output, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2));
        if (key.Velocity == 0) { writer.WriteSamples(new float[2], 0, 2); return; }
        quiet = 0; bool sounding = false;
        for (int frame = 0; frame < Rate * TailLimitSeconds; frame += Block)
        {
            cancellation.ThrowIfCancellationRequested();
            int frames = Math.Min(Block, Rate * TailLimitSeconds - frame);
            var midi = new List<MidiEvent>();
            if (frame == 0) midi.Add(new(0, 0x99, key.Note, key.Velocity));
            if (frame <= Rate / 10 && frame + frames > Rate / 10) midi.Add(new(Rate / 10 - frame, 0x89, key.Note, 0));
            process(buffer, frames, midi.ToArray());
            int limit = Rate * TailLimitSeconds, fade = Rate / 2;
            if (fadeAtLimit && frame + frames > limit - fade)
                for (int i = 0; i < frames; i++)
                {
                    float gain = Math.Clamp((limit - (frame + i)) / (float)fade, 0, 1);
                    buffer[2 * i] *= gain; buffer[2 * i + 1] *= gain;
                }
            float peak = 0;
            for (int i = 0; i < frames * 2; i++)
            {
                if (!float.IsFinite(buffer[i])) throw new IOException("The instrument returned invalid audio.");
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
            sounding |= peak >= Silence;
            quiet = peak < Silence ? quiet + frames : 0;
            writer.WriteSamples(buffer, 0, frames * 2);
            if (sounding && frame >= Rate * 2 && quiet >= Rate) return;
            if (!sounding && frame >= Rate * 2) throw new SilentRenderException($"MIDI note {key.Note}, velocity {key.Velocity} rendered silence. Check the kit mapping.");
            pump(); if (pace) await Task.Delay(12, cancellation);
        }
        if (fadeAtLimit) return;
        throw new IOException($"MIDI note {key.Note}, velocity {key.Velocity} still sounds at the 15-second tail limit. Export stopped without truncating it; shorten the kit's release or effects and retry.");
    }
}

/// <summary>A note the kit plays nothing for (no sound in its first two seconds).</summary>
public sealed class SilentRenderException(string message) : IOException(message);
