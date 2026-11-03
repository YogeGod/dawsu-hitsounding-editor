using ManagedBass;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>
/// Mixes hitsound samples into stereo 44.1 kHz WAV for Generate mode. Uses 16-bit PCM when the mix fits, and 32-bit
/// float when it needs headroom. Decoding uses BASS; the summing and WAV writing are pure for tests.
/// </summary>
public static class SampleMixer
{
    public const int Rate = 44100;

    /// <summary>Render a planned mix only when every required sample can be decoded.</summary>
    public static byte[] Mix(MixFile mix, Func<Lane, float[]?> decode)
    {
        var layers = new List<(float[] Stereo, float Gain)>();
        foreach (var (lane, volume) in mix.Layers)
        {
            var data = decode(lane);
            if (data is not { Length: > 0 }) throw new IOException($"Couldn't make {mix.FileName}: {lane.Name} could not be decoded.");
            layers.Add((data, volume / 100f));
        }
        foreach (var (lane, volume, duration) in mix.Loops)
        {
            var data = decode(lane);
            if (data is not { Length: > 1 }) throw new IOException($"Couldn't make {mix.FileName}: {lane.Name} could not be decoded.");
            int frames = checked((int)Math.Ceiling(duration * Rate / 1000));
            var repeated = new float[checked(frames * 2)];
            for (int i = 0; i < repeated.Length; i++) repeated[i] = data[i % data.Length];
            layers.Add((repeated, volume / 100f));
        }
        if (layers.Count == 0) throw new IOException($"Couldn't make {mix.FileName}: no sounds were supplied.");
        return Mix(layers);
    }

    /// <summary>Decodes a sample file to interleaved stereo floats at 44.1 kHz, or null when BASS can't read it.</summary>
    public static float[]? Decode(byte[] file)
    {
        // osu!'s official blank.wav is a valid PCM WAV with an empty data chunk. BASS refuses to open it, but it
        // deliberately represents silence, including when other software has appended tags after the RIFF payload.
        if (BlankSample.IsEmptyWav(file)) return [0, 0];
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

    /// <summary>Sums stereo 44.1 kHz layers at their gains (volume / 100), preserving over-full-scale values as float WAV.</summary>
    public static byte[] Mix(IReadOnlyList<(float[] Stereo, float Gain)> layers)
    {
        int length = layers.Count == 0 ? 0 : layers.Max(l => l.Stereo.Length);
        int minFrames = (int)Math.Ceiling(Rate * 0.025); // at least 25 ms, in complete stereo frames
        length = Math.Max((length + 1) / 2, minFrames) * 2;
        var sum = new float[length];
        foreach (var (stereo, gain) in layers)
            for (int i = 0; i < stereo.Length; i++)
                sum[i] += stereo[i] * gain;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        bool floating = sum.Any(s => s > 1f || s < -1f);
        int bytesPerSample = floating ? 4 : 2;
        int dataBytes = checked(sum.Length * bytesPerSample);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)(floating ? 3 : 1)); // IEEE float or integer PCM
        w.Write((short)2);
        w.Write(Rate);
        w.Write(Rate * 2 * bytesPerSample);
        w.Write((short)(2 * bytesPerSample));
        w.Write((short)(bytesPerSample * 8));
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (float s in sum)
        {
            if (floating) w.Write(s);
            else w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}
