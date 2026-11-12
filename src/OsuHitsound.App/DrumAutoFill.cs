using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed record DrumHitCandidate(string Drum, double TimeMs, double Score);
internal sealed record DrumAutoFillOptions(Lane? Kick, Lane? Snare, bool SelectedRange,
    double ThresholdScale, double ToleranceMs, bool IncludeUnmatched, string? Stem);
internal sealed record DrumAutoFillPlan(IReadOnlyList<EditHit> Hits, int Unmatched, int Duplicates)
{
    public static DrumAutoFillPlan Create(EditSession session, IEnumerable<DrumHitCandidate> candidates,
        DrumAutoFillOptions options, double from = double.NegativeInfinity, double to = double.PositiveInfinity)
    {
        if (!double.IsFinite(options.ToleranceMs) || options.ToleranceMs < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var hits = new List<EditHit>();
        int unmatched = 0, duplicates = 0;
        var moments = session.Moments.Where(m => m.Moment.Time >= from && m.Moment.Time < to)
            .OrderBy(m => m.Moment.Time).ThenBy(m => m.Key.ObjectIndex).ThenBy(m => m.Key.Node).ToArray();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.TimeMs))
        {
            if (!double.IsFinite(candidate.TimeMs) || !double.IsFinite(candidate.Score) || candidate.TimeMs < 0 ||
                candidate.TimeMs < from || candidate.TimeMs >= to) continue;
            var lane = candidate.Drum switch { "kick" => options.Kick, "snare" => options.Snare, _ => null };
            if (lane is null) continue;
            var nearest = moments.Length > 0 ? moments.MinBy(m => Math.Abs(m.Moment.Time - candidate.TimeMs)) : default;
            EditHit hit;
            if (nearest.Moment is not null && Math.Abs(nearest.Moment.Time - candidate.TimeMs) <= options.ToleranceMs)
                hit = new(lane, nearest.Moment.Time, nearest.Key);
            else
            {
                unmatched++;
                if (!options.IncludeUnmatched) continue;
                hit = session.HitAt(lane, candidate.TimeMs);
            }
            if (session.State.Hits.Concat(hits).Any(h => h.Lane == lane && Math.Abs(h.Time - hit.Time) <= EditSession.AnchorTolerance))
            { duplicates++; continue; }
            hits.Add(hit);
        }
        return new(hits.OrderBy(h => h.Time).ToArray(), unmatched, duplicates);
    }
}

internal sealed record DrumDetectionResult(IReadOnlyList<DrumHitCandidate> Events, string AuditionPath);

internal static class DrumAutoFillDetector
{
    public static async Task<DrumDetectionResult> Detect(byte[] source, double offset, double? from, double? to,
        double thresholdScale, CancellationToken token)
    {
        string job = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsuHitsoundEditor", "DrumAutoFill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!ManagedBass.Bass.Init(0) && ManagedBass.Bass.LastError != ManagedBass.Errors.Already)
                throw new IOException("The audio decoder could not be started.");
            var stereo = SampleMixer.Decode(source) ?? throw new IOException("The song or drum stem could not be decoded.");
            int frames = stereo.Length / 2;
            int a = (int)Math.Clamp(Math.Ceiling(((from ?? offset) - offset) * SampleMixer.Rate / 1000), 0, frames);
            int b = (int)Math.Clamp(Math.Ceiling(((to ?? (frames * 1000.0 / SampleMixer.Rate + offset)) - offset) * SampleMixer.Rate / 1000), 0, frames);
            if (b <= a) throw new IOException("The selected range contains no song audio.");
            // Mono 16-bit, the input ADTOF was trained on (madmom reads 16-bit wav files).
            var mono = new short[b - a];
            for (int frame = a; frame < b; frame++)
                mono[frame - a] = (short)Math.Clamp(Math.Round((stereo[frame * 2] + stereo[frame * 2 + 1]) * .5 * 32767), -32768, 32767);
            var found = DrumDetection.Detect(mono, thresholdScale, token);
            string audition = Path.Combine(job, "audition.wav");
            File.WriteAllBytes(audition, DrumDetection.Audition(mono, found));
            double start = a * 1000.0 / SampleMixer.Rate + offset;
            return new DrumDetectionResult(found.Select(e => e with { TimeMs = Math.Round(e.TimeMs + start, 4) }).ToArray(), audition);
        }, token);
    }
}