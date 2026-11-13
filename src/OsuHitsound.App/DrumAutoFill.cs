using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed record DrumHitCandidate(string Drum, double TimeMs, double Score);
/// <param name="GridDivisors">Beat divisions a hit must sit on (for example 4 and 3 for 1/4 plus triplets).
/// Empty or null keeps off-grid hits.</param>
internal sealed record DrumAutoFillOptions(Lane? Kick, Lane? Snare, bool SelectedRange,
    double ThresholdScale, double ToleranceMs, bool IncludeUnmatched, string? Stem, IReadOnlyList<int>? GridDivisors = null);

/// <summary>The map's beat grid: the nearest grid line for a beat divisor, and that division's length in ms
/// (infinite where the map has no red line, so nothing there is treated as off-grid).</summary>
internal delegate (double Snapped, double Step) DrumGridSnap(double time, int divisor);

internal sealed record DrumAutoFillPlan(IReadOnlyList<EditHit> Hits, int Unmatched, int Duplicates, int OffGrid = 0, double GridShiftMs = 0)
{
    /// <summary>How far a detection may sit from a grid line and still count as on the grid. Narrowed to a
    /// third of the grid step for fast tempos or fine divisors, so the filter still separates lines.</summary>
    public const double GridToleranceMs = 20;

    /// <summary>Largest constant offset between the audio and the map's timing that is compensated.</summary>
    public const double MaxGridShiftMs = 50;

    /// <summary>The constant lag of detections behind the map's grid. A map's timing and its decoded audio often
    /// disagree by a fixed 10–30 ms (decoder delay, the mapper's offset), which would push real hits outside the
    /// grid tolerance. Robust: the median of the deltas, then twice more over the deltas within 25 ms of it.</summary>
    public static double GridShift(IEnumerable<double> times, DrumGridSnap snap, int divisor)
    {
        var deltas = new List<double>();
        foreach (double t in times)
        {
            var (snapped, step) = snap(t, divisor);
            if (double.IsFinite(step) && step > 0) deltas.Add(t - snapped);
        }
        if (deltas.Count < 8) return 0;
        static double Median(List<double> v) { v.Sort(); return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2; }
        double shift = Median([.. deltas]);
        for (int i = 0; i < 2; i++)
        {
            var near = deltas.Where(x => Math.Abs(x - shift) <= 25).ToList();
            if (near.Count < 8) break;
            shift = Median(near);
        }
        return Math.Abs(shift) <= MaxGridShiftMs ? shift : 0;
    }

    /// <summary>The nearest line of any allowed division, or null when the detection is off the grid.
    /// <paramref name="shift"/> is the detections' lag behind the grid (see <see cref="GridShift"/>).</summary>
    public static double? OnGrid(double time, DrumGridSnap snap, IReadOnlyList<int> divisors, double shift = 0)
    {
        double? best = null;
        time -= shift;
        foreach (int divisor in divisors)
        {
            var (snapped, step) = snap(time, divisor);
            double tolerance = double.IsFinite(step) && step > 0 ? Math.Min(GridToleranceMs, step / 3) : double.PositiveInfinity;
            if (Math.Abs(snapped - time) <= tolerance && (best is null || Math.Abs(snapped - time) < Math.Abs(best.Value - time)))
                best = snapped;
        }
        return best;
    }

    public static DrumAutoFillPlan Create(EditSession session, IEnumerable<DrumHitCandidate> candidates,
        DrumAutoFillOptions options, double from = double.NegativeInfinity, double to = double.PositiveInfinity,
        DrumGridSnap? grid = null)
    {
        if (!double.IsFinite(options.ToleranceMs) || options.ToleranceMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var divisors = options.GridDivisors is { Count: > 0 } d && grid is not null ? d : null;
        if (divisors is not null && divisors.Any(x => x <= 0)) throw new ArgumentOutOfRangeException(nameof(options));
        var hits = new List<EditHit>();
        int unmatched = 0, duplicates = 0, offGrid = 0;
        var moments = session.Moments.Where(m => m.Moment.Time >= from && m.Moment.Time < to)
            .OrderBy(m => m.Moment.Time).ThenBy(m => m.Key.ObjectIndex).ThenBy(m => m.Key.Node).ToArray();
        var list = candidates.ToArray();
        double shift = divisors is null ? 0 : GridShift(list.Where(c => double.IsFinite(c.TimeMs) && c.TimeMs >= from && c.TimeMs < to)
            .Select(c => c.TimeMs), grid!, divisors[0]);
        foreach (var candidate in list.OrderByDescending(c => c.Score).ThenBy(c => c.TimeMs))
        {
            if (!double.IsFinite(candidate.TimeMs) || !double.IsFinite(candidate.Score) || candidate.TimeMs < 0 ||
                candidate.TimeMs < from || candidate.TimeMs >= to) continue;
            var lane = candidate.Drum switch { "kick" => options.Kick, "snare" => options.Snare, _ => null };
            if (lane is null) continue;
            double time = candidate.TimeMs;
            if (divisors is not null)
            {
                // Real drum hits land on the song's beat divisions; guitar and bass attacks the detector
                // mistakes for drums often do not. On-grid hits move onto their line.
                if (OnGrid(time, grid!, divisors, shift) is not { } snapped) { offGrid++; continue; }
                time = snapped;
            }
            var nearest = moments.Length > 0 ? moments.MinBy(m => Math.Abs(m.Moment.Time - time)) : default;
            EditHit hit;
            if (nearest.Moment is not null && Math.Abs(nearest.Moment.Time - time) <= options.ToleranceMs)
                hit = new(lane, nearest.Moment.Time, nearest.Key);
            else
            {
                unmatched++;
                if (!options.IncludeUnmatched) continue;
                hit = session.HitAt(lane, time);
            }
            if (session.State.Hits.Concat(hits).Any(h => h.Lane == lane && Math.Abs(h.Time - hit.Time) <= EditSession.AnchorTolerance))
            { duplicates++; continue; }
            hits.Add(hit);
        }
        return new(hits.OrderBy(h => h.Time).ToArray(), unmatched, duplicates, offGrid, shift);
    }
}

internal sealed record DrumDetectionResult(IReadOnlyList<DrumHitCandidate> Events, string AuditionPath);

/// <summary>Runs the in-process signal-processing detector (<see cref="DrumOnsets"/>) on the song or stem.
/// Nothing leaves the machine and no model or external runtime is needed.</summary>
internal static class DrumAutoFillDetector
{
    public static async Task<DrumDetectionResult> Detect(byte[] source, double offset, double? from, double? to,
        double thresholdScale, CancellationToken token)
    {
        string job = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsuHitsoundEditor", "DrumAutoFill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        string audition = Path.Combine(job, "audition.wav");
        var events = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!ManagedBass.Bass.Init(0) && ManagedBass.Bass.LastError != ManagedBass.Errors.Already)
                throw new IOException("The audio decoder could not be started.");
            var stereo = SampleMixer.Decode(source) ?? throw new IOException("The song or drum stem could not be decoded.");
            int frames = stereo.Length / 2;
            int a = (int)Math.Clamp(Math.Ceiling(((from ?? offset) - offset) * SampleMixer.Rate / 1000), 0, frames);
            int b = (int)Math.Clamp(Math.Ceiling(((to ?? (frames * 1000.0 / SampleMixer.Rate + offset)) - offset) * SampleMixer.Rate / 1000), 0, frames);
            if (b <= a) throw new IOException("The selected range contains no song audio.");
            var mono = new float[b - a];
            for (int i = 0; i < mono.Length; i++) mono[i] = (stereo[(a + i) * 2] + stereo[(a + i) * 2 + 1]) * .5f;
            token.ThrowIfCancellationRequested();
            var onsets = DrumOnsets.Detect(mono, SampleMixer.Rate, thresholdScale, token);
            File.WriteAllBytes(audition, AuditionWav(mono, SampleMixer.Rate, onsets));
            double start = a * 1000.0 / SampleMixer.Rate + offset;
            return onsets.Select(o => new DrumHitCandidate(o.Drum, Math.Round(o.TimeMs + start, 4), o.Score)).ToArray();
        }, token);
        return new(events, audition);
    }

    /// <summary>Stereo 16-bit WAV: the analysed audio on the left, synthesised cues at the detected times on
    /// the right (a low thump for kick, a noise burst for snare). The cues are not the user's samples.</summary>
    internal static byte[] AuditionWav(float[] mono, int rate, IEnumerable<DrumOnset> onsets)
    {
        var cues = new float[mono.Length];
        var random = new Random(1);
        foreach (var onset in onsets)
        {
            int at = (int)Math.Round(onset.TimeMs * rate / 1000);
            bool kick = onset.Drum == "kick";
            int length = rate * (kick ? 150 : 90) / 1000;
            for (int i = 0; i < length && at + i < cues.Length; i++)
            {
                if (at + i < 0) continue;
                double t = i / (double)rate;
                double v = kick
                    ? Math.Sin(2 * Math.PI * (55 * t + 60 * (1 - Math.Exp(-t * 30)) / 30)) * Math.Exp(-t * 25)
                    : ((random.NextDouble() * 2 - 1) * .7 + Math.Sin(2 * Math.PI * 190 * t) * .3) * Math.Exp(-t * 40);
                cues[at + i] += (float)(v * .8);
            }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int size = checked(mono.Length * 4);
        writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate);
        writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(size);
        static short Pcm(float v) => (short)Math.Clamp(Math.Round(v * 32767), -32768, 32767);
        for (int i = 0; i < mono.Length; i++) { writer.Write(Pcm(mono[i])); writer.Write(Pcm(cues[i])); }
        writer.Flush();
        return stream.ToArray();
    }
}
