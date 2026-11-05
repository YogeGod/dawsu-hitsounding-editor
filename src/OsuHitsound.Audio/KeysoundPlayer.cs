using System.Collections.Concurrent;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>
/// An instrument part the mixer renders alongside the song. Called under the mixer's lock on the audio thread. One part
/// plays every audible track today; per-track instruments (VST3) will add more parts.
/// </summary>
public interface ILivePart
{
    /// <summary>Adds one block into <paramref name="stereo"/>. While stopped, <paramref name="playing"/> is false: only tails and auditions sound.</summary>
    void Render(Span<float> stereo, int frames, double start, double msPerFrame, bool playing);

    /// <summary>Playback stopped, jumped or looped: sounding notes are released (their tails fade naturally).</summary>
    void ReleaseAll();
}

/// <summary>A note to play, in timeline ms. <see cref="Part"/> picks its pitch-bend curve (a track); -1 for none.</summary>
public readonly record struct PlayedNote(double Start, double End, int Pitch, int Velocity, int Part = -1);

/// <summary>A pitch-bend change of one part, in timeline ms and semitones.</summary>
public readonly record struct PlayedBend(double Time, double Semitones);

/// <summary>
/// Keysound mode's live piano: plays the scheduled notes of the audible tracks with an <see cref="SfzInstrument"/>,
/// and auditions single notes. The note list is replaced whole from the UI thread; the audio thread only reads it.
/// </summary>
public sealed class KeysoundPlayer(SfzInstrument instrument) : ILivePart
{
    private const int MaxVoices = 96;
    private const float Gain = 0.7f; // chords stay below clipping in the usual case
    private const int Rate = PlaybackMixer.Rate;

    private PlayedNote[] notes = [];
    private PlayedBend[][] bends = [];
    private readonly ConcurrentQueue<(int Pitch, int Velocity, double Ms)> auditions = new();
    private readonly List<Voice> voices = [];
    private long clock; // frames rendered so far

    /// <summary>
    /// Replaces what plays: the notes, and per part its pitch-bend changes sorted by time. Safe from any thread.
    /// </summary>
    public void SetNotes(IEnumerable<PlayedNote> played, IReadOnlyList<PlayedBend[]>? partBends = null)
    {
        var sorted = played.ToArray();
        Array.Sort(sorted, (a, b) => a.Start.CompareTo(b.Start));
        Volatile.Write(ref bends, partBends is null ? [] : [.. partBends]);
        Volatile.Write(ref notes, sorted);
    }

    // The bend of a part at a time: the last change at or before it (none before the first).
    private static double BendAt(PlayedBend[][] all, int part, double time)
    {
        if (part < 0 || part >= all.Length || all[part].Length == 0) return 0;
        int i = Sorted.LowerBound(all[part], time + 1e-9, b => b.Time) - 1;
        return i >= 0 ? all[part][i].Semitones : 0;
    }

    /// <summary>Plays one note now, for <paramref name="ms"/> before its release, whether or not playback runs.</summary>
    public void Audition(int pitch, int velocity, double ms = 400) => auditions.Enqueue((pitch, velocity, ms));

    public void ReleaseAll()
    {
        foreach (var v in voices) v.ReleaseAt = Math.Min(v.ReleaseAt, clock);
    }

    internal int ActiveVoices => voices.Count;

    public void Render(Span<float> stereo, int frames, double start, double msPerFrame, bool playing)
    {
        while (auditions.TryDequeue(out var a)) Start(a.Pitch, a.Velocity, 0, (long)Math.Round(a.Ms * Rate / 1000), part: -1);

        if (playing && msPerFrame > 0)
        {
            var list = Volatile.Read(ref notes);
            double end = start + frames * msPerFrame;
            for (int i = Sorted.LowerBound(list, start, n => n.Start); i < list.Length && list[i].Start < end; i++)
            {
                var n = list[i];
                int offset = (int)Math.Clamp(Math.Round((n.Start - start) / msPerFrame), 0, frames - 1);
                long length = Math.Max(1, (long)Math.Round((n.End - n.Start) / msPerFrame));
                Start(n.Pitch, n.Velocity, offset, length, n.Part);
            }
        }

        var partBends = Volatile.Read(ref bends);
        for (int v = voices.Count - 1; v >= 0; v--)
        {
            var voice = voices[v];
            // Bends follow the timeline while playing; a stopped voice keeps its last bend.
            if (playing && voice.Part >= 0) voice.Bend = Math.Pow(2, BendAt(partBends, voice.Part, start) / 12);
            if (!voice.Mix(stereo, frames, clock)) voices.RemoveAt(v);
        }
        clock += frames;
    }

    private void Start(int pitch, int velocity, int offset, long length, int part)
    {
        if (instrument.RegionFor(pitch, velocity) is not { } region) return;
        if (voices.Count >= MaxVoices) voices.RemoveAt(0); // the oldest goes
        float amp = Gain * MathF.Pow(Math.Clamp(velocity, 1, 127) / 127f, 2); // SFZ's default amp_veltrack=100
        voices.Add(new Voice(region, instrument.Sample(region), pitch, amp, clock + offset, clock + offset + length) { Part = part });
    }

    /// <summary>One sounding note: resampled from its region, looping while held, fading over the release time.</summary>
    private sealed class Voice
    {
        private readonly float[] data;
        private readonly double step;
        private readonly float amp;
        private readonly long startAt, loopStart, loopEnd, loopLength;
        private readonly int frameCount, releaseFrames;
        private readonly bool loops;
        private readonly Biquad? left, right;
        private double position;
        private float fade = 1;

        public long ReleaseAt { get; set; }
        public int Part { get; init; } = -1;
        /// <summary>The pitch-bend factor (1 = none), set before each block.</summary>
        public double Bend { get; set; } = 1;

        public Voice(SfzRegion region, float[] data, int pitch, float amp, long startAt, long releaseAt)
        {
            (this.data, this.amp, this.startAt, ReleaseAt) = (data, amp, startAt, releaseAt);
            step = Math.Pow(2, (pitch - region.KeyCenter) / 12.0);
            releaseFrames = Math.Max(1, (int)(region.Release * Rate));
            frameCount = data.Length / 2;
            (loopStart, loopEnd) = (region.LoopStart, Math.Min(region.LoopEnd + 1, frameCount));
            loopLength = loopEnd - loopStart;
            loops = region.HasLoop && loopLength > 1;
            if (region.Cutoff > 0) (left, right) = (new Biquad(region.Cutoff), new Biquad(region.Cutoff));
        }

        /// <summary>Adds this block; false once the voice has finished.</summary>
        public bool Mix(Span<float> stereo, int frames, long clock)
        {
            for (int i = (int)Math.Clamp(startAt - clock, 0, frames); i < frames; i++)
            {
                if (clock + i >= ReleaseAt)
                {
                    fade -= 1f / releaseFrames;
                    if (fade <= 0) return false;
                }

                if (loops && position >= loopEnd) position -= loopLength;
                int f = (int)position;
                // The frame after f, for interpolation: a loop's end leads back to its start.
                long next = loops && f + 1 >= loopEnd ? loopStart : f + 1;
                if (next >= frameCount) return false;
                float t = (float)(position - f);
                float l = data[f * 2] + (data[next * 2] - data[f * 2]) * t;
                float r = data[f * 2 + 1] + (data[next * 2 + 1] - data[f * 2 + 1]) * t;
                if (left is not null) (l, r) = (left.Process(l), right!.Process(r));
                float g = amp * fade;
                stereo[i * 2] += l * g;
                stereo[i * 2 + 1] += r * g;
                position += step * Bend;
            }

            return true;
        }
    }

    /// <summary>A 2-pole (12 dB/octave) low-pass, Butterworth Q, as SFZ's lpf_2p.</summary>
    private sealed class Biquad
    {
        private readonly float b0, b1, b2, a1, a2;
        private float x1, x2, y1, y2;

        public Biquad(double cutoff)
        {
            double w = 2 * Math.PI * Math.Min(cutoff, Rate * 0.45) / Rate, alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5)), cos = Math.Cos(w), a0 = 1 + alpha;
            b0 = (float)((1 - cos) / 2 / a0);
            b1 = (float)((1 - cos) / a0);
            b2 = b0;
            a1 = (float)(-2 * cos / a0);
            a2 = (float)((1 - alpha) / a0);
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            (x2, x1, y2, y1) = (x1, x, y1, y);
            return y;
        }
    }
}
