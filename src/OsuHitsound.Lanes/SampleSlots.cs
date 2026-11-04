using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>Editor slots keep their family/index independently from safe backing sample filenames.</summary>
public static class SampleSlots
{
    public static Lane Next(IEnumerable<Lane> slots, Lane family)
    {
        var sample = family.Sample ?? throw new ArgumentException("Choose a sampleset family.", nameof(family));
        int last = slots.Select(l => l.Sample).Where(s => s is { } v && v.Set == sample.Set && v.Sound == sample.Sound)
            .Select(s => s!.Value.Index).DefaultIfEmpty(1).Max();
        long next = Math.Max(1L, last) + 1;
        if (next > int.MaxValue) throw new IOException("No more indexes are available in this sample family.");
        return new(LaneSource.BeatmapFile, $"{HitsoundRules.SampleName(sample.Set, sample.Sound)}{next}.wav");
    }

    public static string BackingFile(MapFolder folder, Lane slot, string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!HitsoundRules.SampleExtensions.Contains(extension)) throw new ArgumentException("Choose a WAV, OGG or MP3 sample.", nameof(extension));
        var sample = slot.Sample ?? (SampleSet.Soft, "hitnormal", 1);
        int preferred = Math.Max(1, sample.Item3);
        var used = HitsoundDiff.IndexesUsed(folder, except: null);
        string stem = HitsoundRules.SampleName(sample.Item1, sample.Item2);
        // A previously missing sample could already be falling back to the skin in another difficulty.
        if (folder.FindSample(stem + (preferred == 1 ? "" : preferred.ToString(System.Globalization.CultureInfo.InvariantCulture))) is null && !used.Contains(preferred))
            return stem + (preferred == 1 ? "" : preferred.ToString(System.Globalization.CultureInfo.InvariantCulture)) + extension;
        long fresh = Math.Max((long)HitsoundDiff.MaxFileIndex(folder.Files), used.DefaultIfEmpty(1).Max()) + 1;
        if (fresh > int.MaxValue) throw new IOException("No safe sample index is available in this mapset.");
        return stem + fresh.ToString(System.Globalization.CultureInfo.InvariantCulture) + extension;
    }
}
