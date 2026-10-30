using ManagedBass;

namespace OsuHitsound.Audio;

/// <summary>
/// A song's loudness over time for the waveform lane: the largest absolute sample in each slice of
/// 1000 / <see cref="PeaksPerSecond"/> ms, 0–1. Times are song time (add the song offset to place it on the timeline).
/// </summary>
public sealed record Waveform(float[] Peaks, int PeaksPerSecond)
{
    public const int DefaultPeaksPerSecond = 200; // a 5 ms slice: finer than the grid ever needs

    public double Duration => Peaks.Length * 1000.0 / PeaksPerSecond;

    /// <summary>The loudest peak between two song times (ms), or 0 outside the song.</summary>
    public float Max(double from, double to)
    {
        int a = Math.Max(0, (int)Math.Floor(from * PeaksPerSecond / 1000)), b = Math.Min(Peaks.Length - 1, (int)Math.Floor(to * PeaksPerSecond / 1000));
        float max = 0;
        for (int i = a; i <= b; i++)
            if (Peaks[i] > max) max = Peaks[i];
        return max;
    }

    /// <summary>Peaks from interleaved float samples (pure: the part tests can check without audio decoding).</summary>
    public static Waveform FromSamples(ReadOnlySpan<float> interleaved, int channels, int sampleRate, int peaksPerSecond = DefaultPeaksPerSecond)
    {
        int framesPerPeak = Math.Max(1, sampleRate / peaksPerSecond);
        int frames = interleaved.Length / Math.Max(channels, 1);
        var peaks = new float[(frames + framesPerPeak - 1) / framesPerPeak];
        for (int f = 0; f < frames; f++)
        {
            float a = 0;
            for (int c = 0; c < channels; c++) a = Math.Max(a, Math.Abs(interleaved[f * channels + c]));
            ref float p = ref peaks[f / framesPerPeak];
            if (a > p) p = a;
        }

        return new Waveform(peaks, peaksPerSecond);
    }

    /// <summary>
    /// Decodes a song file's bytes with BASS (already initialised by the audio engine) into peaks. Slow for long songs:
    /// call it off the UI thread. Returns null when BASS can't decode the file.
    /// </summary>
    public static Waveform? Decode(byte[] song, int peaksPerSecond = DefaultPeaksPerSecond, CancellationToken cancel = default)
    {
        int stream = Bass.CreateStream(song, 0, song.Length, BassFlags.Decode | BassFlags.Float); // no Prescan: reading straight through needs no seeking
        if (stream == 0) return null;
        try
        {
            var info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels), framesPerPeak = Math.Max(1, info.Frequency / peaksPerSecond);
            var peaks = new List<float>();
            var buffer = new float[framesPerPeak * channels * 64];
            float current = 0;
            int inPeak = 0;
            while (!cancel.IsCancellationRequested)
            {
                int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0) break;
                int frames = bytes / sizeof(float) / channels;
                for (int f = 0; f < frames; f++)
                {
                    for (int c = 0; c < channels; c++) current = Math.Max(current, Math.Abs(buffer[f * channels + c]));
                    if (++inPeak == framesPerPeak)
                    {
                        peaks.Add(Math.Min(current, 1));
                        (current, inPeak) = (0, 0);
                    }
                }
            }

            if (inPeak > 0) peaks.Add(Math.Min(current, 1));
            return cancel.IsCancellationRequested ? null : new Waveform([.. peaks], peaksPerSecond);
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }
}
