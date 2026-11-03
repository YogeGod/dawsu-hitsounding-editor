using ManagedBass;
using ManagedBass.Fx;

namespace OsuHitsound.Audio;

/// <summary>
/// A song decoded by BASS for the mixer: a decoding stream inside a BASS_FX tempo stream (speed changes keep the pitch),
/// read as stereo floats at <see cref="PlaybackMixer.Rate"/>. A song at another rate is resampled linearly (rare: osu!
/// songs are almost always 44.1 kHz). BASS only decodes here; it never opens a device.
/// </summary>
public sealed class BassSong : ISongSource, IDisposable
{
    private readonly int stream;
    private readonly int channels, rate;
    private float[] raw = [];

    // Resampling state: the position between the last two source frames, and those frames.
    private double phase;
    private float prevL, prevR, nextL, nextR;
    private bool primed;

    private BassSong(int stream, int channels, int rate, double lengthMs) =>
        (this.stream, this.channels, this.rate, LengthMs) = (stream, channels, rate, lengthMs);

    /// <summary>Opens a song file's bytes, or returns null when BASS can't decode it. BASS must be initialised.</summary>
    public static BassSong? Open(byte[] bytes)
    {
        int decoder = Bass.CreateStream(bytes, 0, bytes.Length, BassFlags.Prescan | BassFlags.Decode | BassFlags.Float);
        if (decoder == 0) return null;
        int tempo = BassFx.TempoCreate(decoder, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            Bass.StreamFree(decoder);
            return null;
        }

        var info = Bass.ChannelGetInfo(tempo);
        double length = Bass.ChannelBytes2Seconds(tempo, Bass.ChannelGetLength(tempo)) * 1000;
        return new BassSong(tempo, Math.Max(1, info.Channels), info.Frequency, length);
    }

    public double LengthMs { get; }

    public void Seek(double songMs)
    {
        Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, Math.Max(0, songMs) / 1000));
        (phase, primed) = (0, false);
    }

    public void SetSpeed(double speed) => Bass.ChannelSetAttribute(stream, ChannelAttribute.Tempo, (speed - 1) * 100);

    public int Read(Span<float> stereo)
    {
        int frames = stereo.Length / 2;
        if (rate == PlaybackMixer.Rate) return ReadSource(stereo, frames);

        // Linear resampling: step through the source at rate / 44100 source frames per output frame.
        double step = (double)rate / PlaybackMixer.Rate;
        Span<float> one = stackalloc float[2];
        for (int f = 0; f < frames; f++)
        {
            while (!primed || phase >= 1)
            {
                if (ReadSource(one, 1) == 0) return f;
                (prevL, prevR) = primed ? (nextL, nextR) : (one[0], one[1]);
                (nextL, nextR) = (one[0], one[1]);
                if (primed) phase -= 1;
                primed = true;
            }

            stereo[f * 2] = prevL + (nextL - prevL) * (float)phase;
            stereo[f * 2 + 1] = prevR + (nextR - prevR) * (float)phase;
            phase += step;
        }

        return frames;
    }

    // Source frames as stereo (mono doubled, extra channels dropped).
    private int ReadSource(Span<float> stereo, int frames)
    {
        int want = frames * channels;
        if (raw.Length < want) raw = new float[want];
        int got = Bass.ChannelGetData(stream, raw, want * sizeof(float));
        if (got <= 0) return 0;
        int read = got / sizeof(float) / channels;
        for (int f = 0; f < read; f++)
        {
            stereo[f * 2] = raw[f * channels];
            stereo[f * 2 + 1] = raw[f * channels + Math.Min(1, channels - 1)];
        }

        return read;
    }

    public void Dispose() => Bass.StreamFree(stream);
}
