namespace OsuHitsound.Audio;

/// <summary>A detected drum hit. <see cref="TimeMs"/> is relative to the start of the analysed audio.
/// <see cref="Score"/> is the peak height of the normalised onset curve (about 1 for a typical strong hit);
/// it is a strength, not a calibrated probability.</summary>
public sealed record DrumOnset(string Drum, double TimeMs, double Score);

/// <summary>Kick and snare onset detection with plain signal processing (no trained model):
/// STFT → harmonic/percussive separation by median filtering → band-limited log spectral flux →
/// adaptive peak picking → simple band-ratio rules. Kick uses 30–120 Hz. Snare needs its body
/// (150–400 Hz) and wire noise (1.5–5 kHz) to rise together, and is dropped when a coinciding kick
/// explains it. Constants were set against the two annotated MusicDelta clips (see docs/drum-auto-fill.md).</summary>
public static class DrumOnsets
{
    public const int Window = 2048;
    private const int MedianHalf = 8;          // 17-point median filters for HPSS
    private const double Compression = 3;     // log(1 + γ·|X|)
    private const int PeakHalf = 3;            // ±30 ms local-maximum window
    private const int MeanBefore = 10, MeanAfter = 3;
    private const double Delta = .12, Floor = .3; // above the local mean / absolute, at threshold scale 1
    private const double MinGapMs = 60;
    private const double NormPercentile = .9;
    // Centred frames see an attack a little before it lands; measured on annotated recordings.
    private const double BiasMs = 4;
    // Snare candidates that a coinciding kick explains (see Detect).
    private const double SnareUnderKick = .75, StrongSnare = 1, BodyOverKick = 1.3, HatOverKick = .5;

    public static IReadOnlyList<DrumOnset> Detect(float[] mono, int rate, double thresholdScale = 1,
        CancellationToken cancel = default)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
        if (!double.IsFinite(thresholdScale) || thresholdScale <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdScale));
        int hop = Math.Max(1, rate / 100);
        double hopMs = hop * 1000.0 / rate;
        int Bin(double hz) => Math.Clamp((int)Math.Round(hz * Window / rate), 1, Window / 2);
        int bins = Bin(11000) + 2;
        int frames = mono.Length / hop + 1;
        if (mono.Length < hop * 4) return [];

        // Peak-normalise so the log compression behaves the same for quiet and loud songs.
        float peak = 0;
        foreach (float v in mono) peak = Math.Max(peak, Math.Abs(v));
        if (peak < 1e-6f) return [];
        var x = new float[mono.Length];
        for (int i = 0; i < x.Length; i++) x[i] = mono[i] / peak;
        var mag = Spectrogram(x, hop, frames, bins, cancel);
        var perc = Percussive(mag, frames, bins, cancel);
        cancel.ThrowIfCancellationRequested();

        var kick = Normalise(Flux(perc, frames, bins, Bin(30), Bin(120)));
        var body = Normalise(Flux(perc, frames, bins, Bin(150), Bin(400)));
        var noise = Normalise(Flux(perc, frames, bins, Bin(1500), Bin(5000)));
        var hat = Normalise(Flux(perc, frames, bins, Bin(7000), Bin(11000)));
        var snare = new double[frames];
        for (int t = 0; t < frames; t++) snare[t] = Math.Sqrt(body[t] * noise[t]);
        snare = Normalise(snare);

        double Near(double[] curve, int frame)
        {
            double o = 0;
            for (int u = Math.Max(0, frame - 2); u <= Math.Min(frames - 1, frame + 2); u++) o = Math.Max(o, curve[u]);
            return o;
        }
        // A kick's beater click leaks into the snare bands. Drop a snare candidate when it is much weaker
        // than a coinciding kick (unless strong in its own right), or when its body barely exceeds the kick
        // and it has little of the wires' high noise. A real snare on a kick usually has one or the other.
        bool KickBleed((int Frame, double Score) p)
        {
            double k = Near(kick, p.Frame);
            if (p.Score < StrongSnare && p.Score < SnareUnderKick * k) return true;
            return Near(body, p.Frame) < BodyOverKick * k && Near(hat, p.Frame) < HatOverKick * k;
        }
        var result = new List<DrumOnset>();
        result.AddRange(Peaks(kick, thresholdScale, hopMs).Select(p => Onset("kick", p, hopMs)));
        result.AddRange(Peaks(snare, thresholdScale, hopMs).Where(p => !KickBleed(p)).Select(p => Onset("snare", p, hopMs)));
        return result.OrderBy(e => e.TimeMs).ThenBy(e => e.Drum, StringComparer.Ordinal).ToArray();
    }

    private static float[] Spectrogram(float[] x, int hop, int frames, int bins, CancellationToken cancel)
    {
        var hann = new double[Window];
        double sum = 0;
        for (int i = 0; i < Window; i++) sum += hann[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / Window);
        var mag = new float[frames * bins];
        Parallel.For(0, frames, new ParallelOptions { CancellationToken = cancel },
            () => (re: new double[Window], im: new double[Window]), (t, _, buf) =>
            {
                int start = t * hop - Window / 2;
                for (int i = 0; i < Window; i++)
                {
                    int j = start + i;
                    buf.re[i] = j >= 0 && j < x.Length ? x[j] * hann[i] : 0;
                    buf.im[i] = 0;
                }
                Fft(buf.re, buf.im);
                for (int k = 0; k < bins; k++)
                    mag[t * bins + k] = (float)(Math.Sqrt(buf.re[k] * buf.re[k] + buf.im[k] * buf.im[k]) * 2 / sum);
                return buf;
            }, _ => { });
        return mag;
    }

    /// <summary>Soft-masked percussive part: a median across time keeps sustained (harmonic) partials,
    /// a median across frequency keeps broadband (percussive) frames.</summary>
    private static float[] Percussive(float[] mag, int frames, int bins, CancellationToken cancel)
    {
        var harm = new float[mag.Length];
        var perc = new float[mag.Length];
        var options = new ParallelOptions { CancellationToken = cancel };
        Parallel.For(0, bins, options, () => new float[2 * MedianHalf + 1], (k, _, buf) =>
        {
            for (int t = 0; t < frames; t++) harm[t * bins + k] = Median(mag, t, frames, bins, k, buf, alongTime: true);
            return buf;
        }, _ => { });
        Parallel.For(0, frames, options, () => new float[2 * MedianHalf + 1], (t, _, buf) =>
        {
            for (int k = 0; k < bins; k++)
            {
                int i = t * bins + k;
                double p = Median(mag, t, frames, bins, k, buf, alongTime: false), h = harm[i];
                double p2 = p * p, h2 = h * h;
                perc[i] = (float)(mag[i] * (p2 + h2 > 1e-20 ? p2 / (p2 + h2) : 0));
            }
            return buf;
        }, _ => { });
        return perc;
    }

    private static float Median(float[] mag, int t, int frames, int bins, int k, float[] buf, bool alongTime)
    {
        int n = 0;
        for (int d = -MedianHalf; d <= MedianHalf; d++)
        {
            int tt = alongTime ? t + d : t, kk = alongTime ? k : k + d;
            if (tt < 0 || tt >= frames || kk < 0 || kk >= bins) continue;
            buf[n++] = mag[tt * bins + kk];
        }
        Array.Sort(buf, 0, n);
        return n == 0 ? 0 : buf[n / 2];
    }

    /// <summary>Positive log-magnitude increase in a band, compared with a 3-bin maximum of the previous
    /// frame so that small pitch wobbles do not count as onsets.</summary>
    private static double[] Flux(float[] spec, int frames, int bins, int lo, int hi)
    {
        var flux = new double[frames];
        hi = Math.Min(hi, bins - 2);
        lo = Math.Max(lo, 1);
        if (hi < lo) return flux;
        double L(int t, int k) => Math.Log(1 + Compression * spec[t * bins + k]);
        for (int t = 1; t < frames; t++)
        {
            double s = 0;
            for (int k = lo; k <= hi; k++)
            {
                double prev = Math.Max(L(t - 1, k), Math.Max(L(t - 1, k - 1), L(t - 1, k + 1)));
                s += Math.Max(0, L(t, k) - prev);
            }
            flux[t] = s / (hi - lo + 1);
        }
        return flux;
    }

    /// <summary>Scales a curve so that a typical strong hit (the 90th percentile of its local maxima) is 1.</summary>
    private static double[] Normalise(double[] o)
    {
        // Most local maxima are tiny wiggles; only those within 25 dB of the loudest count towards the scale.
        var maxima = new List<double>();
        for (int t = 1; t < o.Length - 1; t++)
            if (o[t] > 0 && o[t] >= o[t - 1] && o[t] > o[t + 1]) maxima.Add(o[t]);
        if (maxima.Count == 0) return o;
        double top = maxima.Max();
        maxima = maxima.Where(v => v >= top * .056).OrderBy(v => v).ToList();
        double scale = maxima[(int)Math.Min(maxima.Count - 1, Math.Floor(maxima.Count * NormPercentile))];
        if (scale <= 1e-12) return o;
        return o.Select(v => v / scale).ToArray();
    }

    private static DrumOnset Onset(string drum, (int Frame, double Score) p, double hopMs) =>
        new(drum, Math.Max(0, p.Frame * hopMs + BiasMs), Math.Round(p.Score, 4));

    private static List<(int Frame, double Score)> Peaks(double[] o, double scale, double hopMs)
    {
        double delta = Delta * scale, floor = Floor * scale;
        var picked = new List<(int Frame, double Score)>();
        for (int t = 1; t < o.Length - 1; t++)
        {
            double v = o[t];
            if (v < floor) continue;
            bool isMax = true;
            for (int d = -PeakHalf; d <= PeakHalf && isMax; d++)
            {
                int u = t + d;
                if (u < 0 || u >= o.Length || u == t) continue;
                if (o[u] > v || (o[u] == v && u < t)) isMax = false;
            }
            if (!isMax) continue;
            double mean = 0; int n = 0;
            for (int u = Math.Max(0, t - MeanBefore); u <= Math.Min(o.Length - 1, t + MeanAfter); u++) { mean += o[u]; n++; }
            if (v < mean / n + delta) continue;
            picked.Add((t, v));
        }
        // Keep the stronger of two peaks closer than a plausible repeat of the same drum.
        int gap = (int)Math.Ceiling(MinGapMs / hopMs);
        var kept = new List<(int Frame, double Score)>();
        foreach (var p in picked.OrderByDescending(p => p.Score).ThenBy(p => p.Frame))
            if (kept.All(k => Math.Abs(k.Frame - p.Frame) >= gap)) kept.Add(p);
        return kept.OrderBy(p => p.Frame).ToList();
    }

    // In-place iterative radix-2 FFT.
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
