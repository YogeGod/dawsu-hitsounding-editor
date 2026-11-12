using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

/// <summary>Which mapset samples count as silent for "Never create silent hitnormals" (issue #28).</summary>
public class SilentSamplesTests
{
    // A mono 16-bit 44.1 kHz WAV with these samples.
    private static byte[] Wav(short[] samples)
    {
        var data = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(44100); w.Write(88200); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    private static short[] Tone() => [.. Enumerable.Range(0, 4410).Select(i => (short)(8000 * Math.Sin(i * 0.06)))];

    [Fact]
    public void Zeros_and_osus_empty_blank_wav_are_silent()
    {
        Assert.True(SilentSamples.IsSilent(Wav(new short[4410])));
        Assert.True(SilentSamples.IsSilent(BlankSample.Bytes));
    }

    [Fact]
    public void A_tone_and_an_unreadable_file_are_not_silent()
    {
        Assert.False(SilentSamples.IsSilent(Wav(Tone())));
        Assert.False(SilentSamples.IsSilent([1, 2, 3, 4])); // can't be decoded: counts as audible
    }

    [Fact]
    public void The_check_looks_in_each_folder_in_order_and_a_missing_file_is_audible()
    {
        var source = new MapFolder(["soft-hitnormal101.wav"], _ => Wav(new short[100]));
        var targets = new MapFolder(["soft-hitnormal102.wav"], _ => Wav(Tone()));
        var silent = SilentSamples.Check(source, targets);

        Assert.True(silent(new Lane(LaneSource.BeatmapFile, "soft-hitnormal101.wav")));
        Assert.False(silent(new Lane(LaneSource.BeatmapFile, "soft-hitnormal102.wav")));
        Assert.False(silent(new Lane(LaneSource.BeatmapFile, "soft-hitnormal103.wav")));
    }
}
