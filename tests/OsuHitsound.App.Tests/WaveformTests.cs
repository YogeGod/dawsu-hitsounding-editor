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
        // 3 s, so the tone's abrupt start and end (whose slices spread energy over every band) are a tiny share of the
        // slices, as in real audio: each band is scaled to the song, ignoring only its loudest 0.5 %.
        var wave = Waveform.FromSamples(Tone(hz, 44100, 3), channels: 1, sampleRate: 44100);
        var (low, mid, high) = wave.Bands(1400, 1600); // the middle of the tone, away from its edges
        float[] bands = [low, mid, high];

        Assert.Equal(band, Array.IndexOf(bands, bands.Max()));
        Assert.True(bands[band] > 0.5f);
    }

    [Theory]
    [InlineData(44100)] // 220.5 frames per 5 ms slice: a rounded slice length drifted the waveform later
    [InlineData(48000)]
    public void A_click_two_minutes_in_is_drawn_at_its_own_time(int rate)
    {
        var samples = new float[rate * 121];
        int at = rate * 120; // exactly 120 000 ms
        for (int i = 0; i < 64; i++) samples[at + i] = 1;

        var wave = Waveform.FromSamples(samples, channels: 1, sampleRate: rate);

        Assert.Equal(1, wave.Max(119_999, 120_001));
        Assert.Equal(0, wave.Max(119_900, 119_990)); // nothing before it
        Assert.Equal(121_000, wave.Duration, 0);
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
