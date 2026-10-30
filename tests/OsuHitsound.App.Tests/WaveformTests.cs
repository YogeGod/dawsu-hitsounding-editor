using OsuHitsound.Audio;

namespace OsuHitsound.App.Tests;

public class WaveformTests
{
    [Fact]
    public void Peaks_follow_the_loudness_of_each_slice()
    {
        // 1 s of stereo at 1000 Hz sampling: silent for 500 ms, then a 0.5 amplitude tone with a 0.8 spike on the right.
        const int rate = 1000;
        var samples = new float[rate * 2];
        for (int f = 500; f < rate; f++)
        {
            samples[f * 2] = 0.5f * (f % 2 == 0 ? 1 : -1);
            samples[f * 2 + 1] = f == 750 ? -0.8f : 0;
        }

        var wave = Waveform.FromSamples(samples, channels: 2, sampleRate: rate, peaksPerSecond: 100);

        Assert.Equal(100, wave.Peaks.Length);
        Assert.Equal(1000, wave.Duration);
        Assert.Equal(0, wave.Max(0, 490));
        Assert.Equal(0.5f, wave.Max(500, 700));
        Assert.Equal(0.8f, wave.Max(740, 760));
        Assert.Equal(0, wave.Max(2000, 3000)); // past the end
    }
}
