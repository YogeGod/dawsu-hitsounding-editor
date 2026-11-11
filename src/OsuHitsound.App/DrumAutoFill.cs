using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
    private sealed record Configuration(string Python, string Script);
    private sealed record Detection(int SchemaVersion, DrumHitCandidate[] Events);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<DrumDetectionResult> Detect(byte[] source, double offset, double? from, double? to,
        double thresholdScale, CancellationToken token)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "drum-autofill.json");
        if (!File.Exists(configPath)) throw new IOException("The local drum detector is not configured in this build. Use the Drum Auto-fill test copy.");
        var config = JsonSerializer.Deserialize<Configuration>(await File.ReadAllTextAsync(configPath, token), Json)
            ?? throw new IOException("The drum detector configuration could not be read.");
        if (string.IsNullOrWhiteSpace(config.Python) || string.IsNullOrWhiteSpace(config.Script) ||
            !Path.IsPathFullyQualified(config.Python) || !Path.IsPathFullyQualified(config.Script) ||
            !File.Exists(config.Python) || !File.Exists(config.Script))
            throw new IOException("The local drum detector could not be found. Rebuild the test copy.");
        string job = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OsuHitsoundEditor", "DrumAutoFill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        string wav = Path.Combine(job, "excerpt.wav"), output = Path.Combine(job, "result");
        double start = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!ManagedBass.Bass.Init(0) && ManagedBass.Bass.LastError != ManagedBass.Errors.Already)
                throw new IOException("The audio decoder could not be started.");
            var stereo = SampleMixer.Decode(source) ?? throw new IOException("The song or drum stem could not be decoded.");
            int frames = stereo.Length / 2;
            int a = (int)Math.Clamp(Math.Ceiling(((from ?? offset) - offset) * SampleMixer.Rate / 1000), 0, frames);
            int b = (int)Math.Clamp(Math.Ceiling(((to ?? (frames * 1000.0 / SampleMixer.Rate + offset)) - offset) * SampleMixer.Rate / 1000), 0, frames);
            if (b <= a) throw new IOException("The selected range contains no song audio.");
            token.ThrowIfCancellationRequested();
            using var writer = new BinaryWriter(File.Create(wav));
            int size = checked((b - a) * 2);
            writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(SampleMixer.Rate);
            writer.Write(SampleMixer.Rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(size);
            for (int frame = a; frame < b; frame++)
            {
                if (frame % 44100 == 0) token.ThrowIfCancellationRequested();
                writer.Write((short)Math.Clamp(Math.Round((stereo[frame * 2] + stereo[frame * 2 + 1]) * .5 * 32767), -32768, 32767));
            }
            return a * 1000.0 / SampleMixer.Rate;
        }, token);
        var info = new ProcessStartInfo(config.Python)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = job };
        foreach (string arg in new[] { config.Script, "--audio", wav, "--output", output,
            "--audio-start-ms", start.ToString("R", CultureInfo.InvariantCulture),
            "--song-offset-ms", offset.ToString("R", CultureInfo.InvariantCulture),
            "--threshold-scale", thresholdScale.ToString("R", CultureInfo.InvariantCulture) }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("The local drum detector could not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            string log = await stdout + "\n" + await stderr;
            await File.WriteAllTextAsync(Path.Combine(job, "detector.log"), log, token);
            if (process.ExitCode != 0) throw new IOException($"Drum analysis failed. Details: {Path.Combine(job, "detector.log")}");
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var result = JsonSerializer.Deserialize<Detection>(await File.ReadAllTextAsync(Path.Combine(output, "candidates.json"), token), Json);
        if (result is not { SchemaVersion: 2, Events: not null } || result.Events.Any(e => e.Drum is not ("kick" or "snare")))
            throw new IOException("The detector returned an incompatible result; no hits were applied.");
        return new(result.Events, Path.Combine(output, "audition.wav"));
    }
}
