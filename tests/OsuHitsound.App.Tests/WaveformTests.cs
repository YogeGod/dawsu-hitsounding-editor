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

    private static float[] Tone(double hz, int rate, double seconds) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate)))];

    [Theory]
    [InlineData(60, 0)]     // a kick's fundamental: low
    [InlineData(800, 1)]    // a snare's body: mid
    [InlineData(5000, 2)]   // a hi-hat: high
    public void A_tone_shows_in_its_own_frequency_band(double hz, int band)
    {
        var wave = Waveform.FromSamples(Tone(hz, 44100, 1), channels: 1, sampleRate: 44100);
        var (low, mid, high) = wave.Bands(400, 600); // the middle of the tone, away from its edges
        float[] bands = [low, mid, high];

        Assert.Equal(band, Array.IndexOf(bands, bands.Max()));
        Assert.True(bands[band] > 0.5f);
    }

    [Fact]
    public void A_click_colours_the_slice_it_is_in_not_an_earlier_or_later_one()
    {
        // Silence with one short burst of 5 kHz at 500 ms: the high band peaks within a slice (5 ms) of 500 ms.
        const int rate = 44100;
        var samples = new float[rate];
        for (int i = 0; i < 64; i++) samples[rate / 2 + i] = (float)Math.Sin(2 * Math.PI * 5000 * i / rate);

        var wave = Waveform.FromSamples(samples, channels: 1, sampleRate: rate);
        int loudest = Array.IndexOf(wave.High!, wave.High!.Max());

        Assert.InRange(loudest * 1000.0 / wave.PeaksPerSecond, 495, 505);
        Assert.Equal(wave.Peaks.Length, wave.High!.Length);
    }
}
