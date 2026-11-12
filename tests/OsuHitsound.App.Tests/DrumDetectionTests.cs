using System.Numerics;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

// Expected values come from ADTOF's own pipeline: madmom features and NotePeakPickingProcessor with the
// Frame_RNN thresholds, run on the same ONNX model (see src/OsuHitsound.App/DrumModel/README.md).
public class DrumDetectionTests
{
    [Fact]
    public void Fft_matches_a_direct_dft()
    {
        var random = new Random(3);
        var input = Enumerable.Range(0, 64).Select(_ => new Complex(random.NextDouble() - .5, 0)).ToArray();
        var fast = (Complex[])input.Clone();
        DrumDetection.Fft(fast);
        for (int k = 0; k < 64; k++)
        {
            Complex sum = 0;
            for (int n = 0; n < 64; n++) sum += input[n] * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * n / 64);
            Assert.True((sum - fast[k]).Magnitude < 1e-9, $"bin {k}");
        }
    }

    [Fact]
    public void Frames_are_centred_on_every_441st_sample_like_madmom()
    {
        var mono = new short[441 * 5 + 1];
        mono[441 * 3] = short.MaxValue;   // an impulse in the middle of frame 3
        var spectrogram = DrumDetection.Spectrogram(mono);
        Assert.Equal(6, spectrogram.Length / DrumDetection.Bins);   // ceil(2206 / 441)
        // An impulse at the window centre has a flat spectrum equal to the window value there.
        double centre = 0.5 - 0.5 * Math.Cos(2 * Math.PI * 1024 / 2047);
        Assert.All(Enumerable.Range(0, DrumDetection.Bins), k =>
            Assert.Equal(centre, spectrogram[3 * DrumDetection.Bins + k], 4));
    }

    [Fact]
    public void Peak_picking_matches_madmom()
    {
        var curve = new float[120];
        foreach (var (i, v) in new[] { (0, .9), (5, .5), (6, .5), (20, .8), (22, .85), (40, .3), (41, .9), (43, .95), (60, .26), (80, .7), (119, .6) })
            curve[i] = (float)v;
        for (int i = 0; i < 10; i++) curve[90 + i] = (float)(.2 + i * (.4 / 9));
        Assert.Equal([0, 5, 20, 41, 60, 80, 99, 119], DrumDetection.PickPeaks(curve, 0.22));
        Assert.Equal([0, 20, 41, 80, 119], DrumDetection.PickPeaks(curve, 0.5));
    }

    [Fact]
    public void Detects_the_synthetic_kicks_and_snares_like_the_reference()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "drums-synthetic.wav"));
        var mono = new short[(bytes.Length - 44) / 2];
        Buffer.BlockCopy(bytes, 44, mono, 0, mono.Length * 2);
        var found = DrumDetection.Detect(mono, 1.0, CancellationToken.None);
        (string, double, double)[] expected =
        [
            ("kick", 490, 0.80748), ("snare", 990, 0.856808), ("kick", 1490, 0.788806), ("snare", 1990, 0.84535),
            ("kick", 2490, 0.775893), ("snare", 2990, 0.836118), ("kick", 3490, 0.70943), ("snare", 3740, 0.840936),
        ];
        Assert.Equal(expected.Length, found.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, found[i].Drum);
            Assert.Equal(expected[i].Item2, found[i].TimeMs);
            Assert.Equal(expected[i].Item3, found[i].Score, 4);
        }
    }

    [Fact]
    public void Audition_puts_the_audio_left_and_cues_right()
    {
        var mono = Enumerable.Range(0, 44100).Select(i => (short)(i % 100)).ToArray();
        var wav = DrumDetection.Audition(mono, [new("kick", 100, .9)]);
        Assert.Equal(44 + mono.Length * 4, wav.Length);
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));
        Assert.Equal(mono[5000], BitConverter.ToInt16(wav, 44 + 5000 * 4));
        Assert.Equal(0, BitConverter.ToInt16(wav, 44 + 1000 * 4 + 2));          // no cue before 100 ms
        Assert.NotEqual(0, BitConverter.ToInt16(wav, 44 + 4500 * 4 + 2));       // kick cue shortly after
    }
}
