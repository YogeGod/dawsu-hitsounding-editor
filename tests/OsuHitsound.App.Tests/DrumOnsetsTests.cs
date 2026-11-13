using OsuHitsound.App;
using OsuHitsound.Audio;

namespace OsuHitsound.App.Tests;

public class DrumOnsetsTests
{
    private const int Rate = 44100;

    // 120 BPM, 8 bars: kick on beats 1 and 3, snare on 2 and 4, closed hi-hat on every eighth note.
    private static (float[] Audio, double[] Kicks, double[] Snares, double[] HatsOnly) Pattern(bool withHats = true, double gain = 1)
    {
        var audio = new float[Rate * 17];
        var random = new Random(7);
        var kicks = new List<double>(); var snares = new List<double>(); var hatsOnly = new List<double>();
        void Add(double ms, Func<double, double> voice, double seconds)
        {
            int at = (int)(ms * Rate / 1000);
            for (int i = 0; i < seconds * Rate && at + i < audio.Length; i++) audio[at + i] += (float)(voice(i / (double)Rate) * gain);
        }
        double Kick(double t) => .9 * Math.Sin(2 * Math.PI * (50 * t + 90 * (1 - Math.Exp(-t * 35)) / 35)) * Math.Exp(-t * 18);
        double Snare(double t) => (.5 * (random.NextDouble() * 2 - 1) + .4 * Math.Sin(2 * Math.PI * 200 * t)) * Math.Exp(-t * 22);
        double last = 0;
        double Hat(double t) { double n = random.NextDouble() * 2 - 1, v = n - last; last = n; return .12 * v * Math.Exp(-t * 60); }
        for (int beat = 0; beat < 32; beat++)
        {
            double ms = 500 + beat * 500;
            if (beat % 2 == 0) { Add(ms, Kick, .4); kicks.Add(ms); } else { Add(ms, Snare, .3); snares.Add(ms); }
            if (withHats) { Add(ms, Hat, .08); Add(ms + 250, Hat, .08); hatsOnly.Add(ms + 250); }
        }
        return (audio, kicks.ToArray(), snares.ToArray(), hatsOnly.ToArray());
    }

    private static int Matched(IEnumerable<DrumOnset> found, string drum, double[] expected, double tolerance = 25) =>
        expected.Count(t => found.Any(o => o.Drum == drum && Math.Abs(o.TimeMs - t) <= tolerance));

    [Fact]
    public void Finds_kicks_and_snares_at_their_times()
    {
        var (audio, kicks, snares, _) = Pattern();
        var found = DrumOnsets.Detect(audio, Rate, 1.25);
        Assert.Equal(kicks.Length, Matched(found, "kick", kicks));
        Assert.Equal(snares.Length, Matched(found, "snare", snares));
    }

    [Fact]
    public void Does_not_label_hi_hats_or_kicks_as_snares()
    {
        var (audio, kicks, _, hats) = Pattern();
        var found = DrumOnsets.Detect(audio, Rate, 1.25);
        Assert.Equal(0, Matched(found, "snare", hats, 40));
        Assert.Equal(0, Matched(found, "snare", kicks, 40));
        Assert.Equal(0, Matched(found, "kick", hats, 40));
    }

    [Fact]
    public void Results_do_not_depend_on_overall_level()
    {
        var loud = DrumOnsets.Detect(Pattern(gain: 1).Audio, Rate, 1.25);
        var quiet = DrumOnsets.Detect(Pattern(gain: .05).Audio, Rate, 1.25);
        Assert.Equal(loud.Select(o => (o.Drum, o.TimeMs)), quiet.Select(o => (o.Drum, o.TimeMs)));
    }

    [Fact]
    public void Silence_and_tiny_input_give_no_hits()
    {
        Assert.Empty(DrumOnsets.Detect(new float[Rate * 3], Rate));
        Assert.Empty(DrumOnsets.Detect(new float[100], Rate));
    }

    [Fact]
    public void Rejects_invalid_arguments_and_honours_cancellation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DrumOnsets.Detect(new float[Rate], Rate, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DrumOnsets.Detect(new float[Rate], 0));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DrumOnsets.Detect(Pattern().Audio, Rate, 1, cancel.Token));
    }

    [Fact]
    public void Audition_puts_song_left_and_cues_right()
    {
        var mono = new float[Rate];
        mono[10] = .5f;
        var wav = DrumAutoFillDetector.AuditionWav(mono, Rate, [new DrumOnset("kick", 500, 1)]);
        Assert.Equal(44 + Rate * 4, wav.Length);
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));
        short Left(int frame) => BitConverter.ToInt16(wav, 44 + frame * 4);
        short Right(int frame) => BitConverter.ToInt16(wav, 46 + frame * 4);
        Assert.Equal((short)Math.Round(.5 * 32767), Left(10));
        Assert.Equal(0, Right(10));
        Assert.Contains(Enumerable.Range(Rate / 2, 2000), f => Math.Abs(Right(f)) > 1000);
    }
}
