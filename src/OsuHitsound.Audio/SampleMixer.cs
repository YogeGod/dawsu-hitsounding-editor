using ManagedBass;

namespace OsuHitsound.Audio;

/// <summary>
/// Mixes hitsound samples into one 16-bit stereo 44.1 kHz WAV for Generate mode: each layer at its volume, summed and
/// clipped. Decoding uses BASS (initialised by the audio engine); the summing and WAV writing are pure for tests.
/// </summary>
public static class SampleMixer
{
    public const int Rate = 44100;

    /// <summary>Decodes a sample file to interleaved stereo floats at 44.1 kHz, or null when BASS can't read it.</summary>
    public static float[]? Decode(byte[] file)
    {
        int stream = Bass.CreateStream(file, 0, file.Length, BassFlags.Decode | BassFlags.Float);
        if (stream == 0) return null;
        try
        {
            var info = Bass.ChannelGetInfo(stream);
            int channels = Math.Max(1, info.Channels);
            var samples = new List<float>();
            var buffer = new float[16384];
            int got;
            while ((got = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float))) > 0)
                samples.AddRange(buffer.AsSpan(0, got / sizeof(float)).ToArray());
            return ToStereo(samples.ToArray(), channels, info.Frequency);
        }
        finally
        {
            Bass.StreamFree(stream);
        }
    }

    /// <summary>Interleaved samples with any channel count and rate → stereo at 44.1 kHz (linear resampling).</summary>
    public static float[] ToStereo(float[] interleaved, int channels, int rate)
    {
        if (channels == 2 && rate == Rate) return interleaved; // already in the mix format (the usual case)
        return Resample(interleaved, channels, rate);
    }

    private static float[] Resample(float[] interleaved, int channels, int rate)
    {
        int frames = interleaved.Length / channels;
        int outFrames = (int)Math.Ceiling(frames * (double)Rate / rate);
        var output = new float[outFrames * 2];
        for (int f = 0; f < outFrames; f++)
        {
            double src = f * (double)rate / Rate;
            int a = Math.Min((int)src, frames - 1), b = Math.Min(a + 1, frames - 1);
            float frac = (float)(src - a);
            for (int c = 0; c < 2; c++)
            {
                int ch = Math.Min(c, channels - 1);
                output[f * 2 + c] = interleaved[a * channels + ch] * (1 - frac) + interleaved[b * channels + ch] * frac;
            }
        }

        return output;
    }

    /// <summary>Sums stereo 44.1 kHz layers at their gains (volume / 100) into a 16-bit WAV, clipped to full scale.</summary>
    public static byte[] Mix(IReadOnlyList<(float[] Stereo, float Gain)> layers)
    {
        int length = layers.Count == 0 ? 0 : layers.Max(l => l.Stereo.Length);
        length = Math.Max(length, 2 * Rate / 40); // at least 25 ms (ranking criteria)
        var sum = new float[length];
        foreach (var (stereo, gain) in layers)
            for (int i = 0; i < stereo.Length; i++)
                sum[i] += stereo[i] * gain;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = sum.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)2);
        w.Write(Rate);
        w.Write(Rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (float s in sum)
            w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
