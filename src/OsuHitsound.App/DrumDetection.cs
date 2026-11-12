using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OsuHitsound.App;

// Kick and snare detection with the ADTOF Frame_RNN model (Zehren, Alunno and Bientinesi, CC BY-NC-SA 4.0,
// github.com/MZehren/ADTOF), converted to ONNX. The pre- and post-processing follow ADTOF's own pipeline,
// which uses madmom: see DrumModel/README.md for the conversion and the parity checks.
internal static class DrumDetection
{
    public const int SampleRate = 44100, FrameSize = 2048, Hop = 441, Bins = FrameSize / 2, Fps = 100;
    public const string ModelFile = "adtof-frame-rnn.onnx";

    // ADTOF's Frame_RNN peak-picking thresholds for BD and SD (hyperparameters.py), and their output columns.
    private static readonly (string Drum, int Column, double Threshold)[] Drums =
        [("kick", 0, 0.22000000000000003), ("snare", 1, 0.24000000000000002)];

    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "DrumModel", ModelFile);

    /// <summary>Kick and snare onsets in a mono 44.1 kHz 16-bit signal, in ms from its first sample.</summary>
    public static IReadOnlyList<DrumHitCandidate> Detect(short[] mono, double thresholdScale, CancellationToken token)
    {
        if (!File.Exists(ModelPath)) throw new IOException("The drum detection model is missing from this install. Reinstall dawsu!.");
        var spectrogram = Spectrogram(mono, token);
        int frames = spectrogram.Length / Bins;
        var activations = Activations(spectrogram, frames, token);
        double durationMs = mono.Length * 1000.0 / SampleRate;
        var events = new List<DrumHitCandidate>();
        foreach (var (drum, column, threshold) in Drums)
        {
            var curve = new float[frames];
            for (int f = 0; f < frames; f++) curve[f] = activations[f * 5 + column];
            foreach (int frame in PickPeaks(curve, threshold * thresholdScale))
            {
                double ms = frame * 1000.0 / Fps;
                if (ms >= durationMs) continue;
                events.Add(new(drum, Math.Round(ms, 4), Math.Round(curve[frame], 6)));
            }
        }
        return events.OrderBy(e => e.TimeMs).ThenBy(e => e.Drum, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Magnitude spectrogram framed like madmom's FramedSignal (frame i centred on sample i * 441, zero padded,
    /// ceil(n / 441) frames) with a symmetric Hann window (np.hanning) and the int16 scaling madmom applies.
    /// Returns frames * 1024 values, row-major.
    /// </summary>
    public static float[] Spectrogram(short[] mono, CancellationToken token = default)
    {
        int frames = (mono.Length + Hop - 1) / Hop;
        var window = new double[FrameSize];
        for (int n = 0; n < FrameSize; n++)
            window[n] = (0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (FrameSize - 1))) / short.MaxValue;
        var result = new float[(long)frames * Bins];
        Parallel.For(0, frames, new ParallelOptions { CancellationToken = token }, () => new Complex[FrameSize], (f, _, buffer) =>
        {
            int start = f * Hop - FrameSize / 2;
            for (int n = 0; n < FrameSize; n++)
            {
                int i = start + n;
                buffer[n] = new Complex(i >= 0 && i < mono.Length ? mono[i] * window[n] : 0, 0);
            }
            Fft(buffer);
            long row = (long)f * Bins;
            for (int k = 0; k < Bins; k++) result[row + k] = (float)buffer[k].Magnitude;
            return buffer;
        }, _ => { });
        return result;
    }

    /// <summary>In-place iterative radix-2 FFT; the length must be a power of two.</summary>
    public static void Fft(Complex[] x)
    {
        int n = x.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (x[i], x[j]) = (x[j], x[i]);
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            for (int i = 0; i < n; i += length)
                for (int k = 0; k < length / 2; k++)
                {
                    var w = Complex.FromPolarCoordinates(1, angle * k);
                    var u = x[i + k];
                    var v = x[i + k + length / 2] * w;
                    x[i + k] = u + v;
                    x[i + k + length / 2] = u - v;
                }
        }
    }

    // ADTOF's predict(): one pass for up to 60,000 frames (10 min); longer songs run in overlapping windows
    // with a warm-up of one training sequence (412 frames) on each side.
    private const int Window = 60000, Warmup = 412;

    private static float[] Activations(float[] spectrogram, int frames, CancellationToken token)
    {
        using var options = new SessionOptions { InterOpNumThreads = 1 };
        using var session = new InferenceSession(ModelPath, options);
        var output = new float[(long)frames * 5];
        int step = Window - 2 * Warmup;
        var starts = new List<int>();
        for (int s = 0; s < Math.Max(1, frames - Warmup); s += step) starts.Add(s);
        foreach (int start in starts)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(Window, frames - start);
            var input = new DenseTensor<float>(spectrogram.AsMemory(start * Bins, length * Bins), [1, length, Bins]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor("spectrogram", input)]);
            var values = results[0].AsTensor<float>().ToArray();
            int from = starts.Count == 1 || start == 0 ? 0 : Warmup;
            int to = starts.Count == 1 || start == starts[^1] ? length : Math.Min(length, from == 0 ? Window - Warmup : Warmup + step);
            Array.Copy(values, from * 5, output, (long)(start + from) * 5, (to - from) * 5);
        }
        return output;
    }

    /// <summary>
    /// madmom's NotePeakPickingProcessor with ADTOF's settings at 100 fps: smooth 0, a moving average over
    /// 10 frames before and 1 after, a moving maximum over 2 before and 1 after (zero padded), then onsets
    /// within 20 ms of the previous kept onset are dropped. Returns frame indices.
    /// </summary>
    public static IReadOnlyList<int> PickPeaks(float[] activation, double threshold)
    {
        const int preAvg = 10, postAvg = 1, preMax = 2, postMax = 1;
        int n = activation.Length;
        var detections = new float[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = i - preAvg; j <= i + postAvg; j++) if (j >= 0 && j < n) sum += activation[j];
            float average = (float)(sum / (preAvg + postAvg + 1));
            detections[i] = activation[i] >= average + (float)threshold ? activation[i] : 0;
        }
        var peaks = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (detections[i] == 0) continue;
            float max = 0;
            for (int j = i - preMax; j <= i + postMax; j++) if (j >= 0 && j < n) max = Math.Max(max, detections[j]);
            if (detections[i] == max) peaks.Add(i);
        }
        // combine_events(onsets, 0.02, 'left'): keep the first onset, drop later ones within 20 ms of it.
        var kept = new List<int>();
        foreach (int peak in peaks)
            if (kept.Count == 0 || peak / (double)Fps - kept[^1] / (double)Fps > 0.02 + 1e-12) kept.Add(peak);
        return kept;
    }

    /// <summary>A stereo 16-bit wav: the analysed audio on the left, short diagnostic cues on the right.</summary>
    public static byte[] Audition(short[] mono, IReadOnlyList<DrumHitCandidate> events)
    {
        var cues = new double[mono.Length];
        var random = new Random(0);
        foreach (var e in events)
        {
            int start = (int)Math.Round(e.TimeMs / 1000 * SampleRate);
            int length = Math.Min((int)Math.Round(.08 * SampleRate), mono.Length - start);
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)SampleRate;
                double tone = e.Drum == "snare" ? random.NextDouble() * 2 - 1 : Math.Sin(2 * Math.PI * 75 * t);
                cues[start + i] += .4 * tone * Math.Exp(-t * 60);
            }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int size = checked(mono.Length * 4);
        writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(SampleRate);
        writer.Write(SampleRate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(size);
        for (int i = 0; i < mono.Length; i++)
        {
            writer.Write(mono[i]);
            writer.Write((short)Math.Round(Math.Clamp(cues[i], -1, 1) * short.MaxValue));
        }
        writer.Flush();
        return stream.ToArray();
    }
}
