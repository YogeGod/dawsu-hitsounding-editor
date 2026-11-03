using System.Numerics;
using ManagedBass;

namespace OsuHitsound.Audio;

/// <summary>
/// A song's loudness over time for the waveform lane: the largest absolute sample in each slice of
/// 1000 / <see cref="PeaksPerSecond"/> ms, 0–1. Times are song time (add the song offset to place it on the timeline).
/// <see cref="Low"/>, <see cref="Mid"/> and <see cref="High"/> are each slice's energy in three frequency bands (0–1,
/// scaled to the song), so kicks, snares and hi-hats can be told apart, as in osu!lazer's editor.
/// </summary>
public sealed record Waveform(float[] Peaks, int PeaksPerSecond, float[]? Low = null, float[]? Mid = null, float[]? High = null)
{
    public const int DefaultPeaksPerSecond = 200; // a 5 ms slice: finer than the grid ever needs

    public double Duration => Peaks.Length * 1000.0 / PeaksPerSecond;

    /// <summary>The loudest peak between two song times (ms), or 0 outside the song.</summary>
    public float Max(double from, double to) => MaxOf(Peaks, from, to);

    /// <summary>The strongest low, mid and high energy between two song times (ms); zeros without bands.</summary>
    public (float Low, float Mid, float High) Bands(double from, double to) =>
        Low is null || Mid is null || High is null ? default : (MaxOf(Low, from, to), MaxOf(Mid, from, to), MaxOf(High, from, to));

    private float MaxOf(float[] values, double from, double to)
    {
        int a = Math.Max(0, (int)Math.Floor(from * PeaksPerSecond / 1000)), b = Math.Min(values.Length - 1, (int)Math.Floor(to * PeaksPerSecond / 1000));
        float max = 0;
        for (int i = a; i <= b; i++)
            if (values[i] > max) max = values[i];
        return max;
    }

    /// <summary>Peaks and bands from interleaved float samples (pure: the part tests can check without audio decoding).</summary>
    public static Waveform FromSamples(ReadOnlySpan<float> interleaved, int channels, int sampleRate, int peaksPerSecond = DefaultPeaksPerSecond)
    {
        channels = Math.Max(channels, 1);
        var builder = new Builder(sampleRate, peaksPerSecond);
        builder.AddFrames(interleaved, channels);
        return builder.Finish();
    }

    /// <summary>
    /// Decodes a song file's bytes with BASS (already initialised by the audio engine) into peaks and bands. Slow for
    /// long songs: call it off the UI thread. Returns null when BASS can't decode the file.
    /// </summary>
    public static Waveform? Decode(byte[] song, int peaksPerSecond = DefaultPeaksPerSecond, CancellationToken cancel = default)
    {
        int stream = Bass.CreateStream(song, 0, song.Length, BassFlags.Decode | BassFlags.Float); // no Prescan: reading straight through needs no seeking
        if (stream == 0) return null;
        try
        {
            var info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels);
            var builder = new Builder(info.Frequency, peaksPerSecond);
            var buffer = new float[4096 * channels];
            while (!cancel.IsCancellationRequested)
            {
                int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0) break;
                builder.AddFrames(buffer.AsSpan(0, bytes / sizeof(float)), channels);

            }

            return cancel.IsCancellationRequested ? null : builder.Finish();
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    /// <summary>
    /// Builds a waveform one frame at a time. Bands follow osu-framework's Waveform (MIT, ppy Pty Ltd): the summed FFT
    /// magnitude over 20–100, 100–2000 and 2000–12000 Hz of a 1024-sample window. Here there is one window per slice,
    /// centred on the slice, so the colours never sit earlier or later than the sound.
    /// </summary>
    private sealed class Builder(int sampleRate, int peaksPerSecond)
    {
        private const int Window = 1024;
        private static readonly float[] Hann = [.. Enumerable.Range(0, Window).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1))))];

        private readonly int framesPerPeak = Math.Max(1, sampleRate / peaksPerSecond);
        private readonly List<float> peaks = [], low = [], mid = [], high = [];
        private readonly float[] ring = new float[Window]; // the last 1024 mono frames
        private readonly Complex[] fft = new Complex[Window];
        private long frame;
        private float current;
        private int inPeak, analysed;

        /// <summary>Adds interleaved frames: each one's loudest channel for the peaks, their average for the bands.</summary>
        public void AddFrames(ReadOnlySpan<float> interleaved, int channels)
        {
            for (int f = 0; f + channels <= interleaved.Length; f += channels)
            {
                float peak = 0, sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    float s = interleaved[f + c];
                    peak = Math.Max(peak, Math.Abs(s));
                    sum += s;
                }

                Add(peak, sum / channels);
            }
        }

        public void Add(float peak, float mono)
        {
            ring[(int)(frame % Window)] = mono;
            frame++;
            current = Math.Max(current, peak);
            if (++inPeak == framesPerPeak)
            {
                peaks.Add(Math.Min(current, 1));
                (current, inPeak) = (0, 0);
            }

            // Slice i is centred on frame i·n + n/2; its window is ready once half a window has followed the centre.
            while (analysed < peaks.Count && frame >= (long)analysed * framesPerPeak + framesPerPeak / 2 + Window / 2) Analyse();
        }

        public Waveform Finish()
        {
            if (inPeak > 0) peaks.Add(Math.Min(current, 1));
            // Pad with silence so the last slices get their windows too.
            while (analysed < peaks.Count)
            {
                ring[(int)(frame % Window)] = 0;
                frame++;
                if (frame >= (long)analysed * framesPerPeak + framesPerPeak / 2 + Window / 2) Analyse();
            }

            return new Waveform([.. peaks], peaksPerSecond, Normalise(low), Normalise(mid), Normalise(high));
        }

        private void Analyse()
        {
            // The window is the last 1024 frames, oldest first (frames before the song are silence).
            for (int i = 0; i < Window; i++)
            {
                long f = frame - Window + i;
                fft[i] = f < 0 ? Complex.Zero : new Complex(ring[(int)(f % Window)] * Hann[i], 0);
            }

            Fft(fft);
            low.Add(Band(20, 100));
            mid.Add(Band(100, 2000));
            high.Add(Band(2000, 12000));
            analysed++;
        }

        private float Band(double fromHz, double toHz)
        {
            int a = Math.Max(1, (int)(Window * fromHz / sampleRate)), b = Math.Min(Window / 2, (int)(Window * toHz / sampleRate));
            double sum = 0;
            for (int i = a; i < b; i++) sum += fft[i].Magnitude;
            return (float)sum;
        }

        // Each band scaled to the song: 1 at its loudest (ignoring the top 0.5 %, so one spike doesn't flatten the rest).
        private static float[] Normalise(List<float> values)
        {
            if (values.Count == 0) return [];
            var sorted = values.Order().ToArray();
            float top = sorted[Math.Clamp((int)(sorted.Length * 0.995), 0, sorted.Length - 1)];
            return top <= 0 ? new float[values.Count] : [.. values.Select(v => Math.Min(1, v / top))];
        }

        // In-place radix-2 FFT (the window is a power of two).
        private static void Fft(Complex[] a)
        {
            int n = a.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) (a[i], a[j]) = (a[j], a[i]);
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
                for (int i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (int k = 0; k < len / 2; k++)
                    {
                        var u = a[i + k];
                        var v = a[i + k + len / 2] * w;
                        a[i + k] = u + v;
                        a[i + k + len / 2] = u - v;
                        w *= step;
                    }
                }
            }
        }
    }
}
