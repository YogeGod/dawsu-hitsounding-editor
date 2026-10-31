namespace OsuHitsound.Format;

/// <summary>
/// The null round-trip check: a file read and written back with no edits gives the same bytes, every hitsound
/// field parses, and setting each field to the value it already holds changes nothing.
/// </summary>
public static class RoundTripCheck
{
    /// <summary>Every .osu and .osb file under <paramref name="folder"/>, recursively.</summary>
    public static IEnumerable<string> FindFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => IsOsu(f) || Path.GetExtension(f).Equals(".osb", StringComparison.OrdinalIgnoreCase));

    public static List<string> RunFile(string path) => Run(File.ReadAllBytes(path), IsOsu(path));

    public static List<string> Run(byte[] original, bool isOsu)
    {
        var problems = new List<string>();
        var file = OsuFile.Parse(original);

        if (!file.ToBytes().AsSpan().SequenceEqual(original))
            problems.Add("bytes differ after read + write");

        if (!isOsu) return problems;

        foreach (var tp in file.TimingPoints)
        {
            try
            {
                _ = tp.Time + tp.BeatLength;
                _ = tp.Uninherited;
                if (tp.SampleSet is { } set) tp.SampleSet = set;
                if (tp.SampleIndex is { } index) tp.SampleIndex = index;
                if (tp.Volume is { } volume) tp.Volume = volume;
            }
            catch (FormatException e)
            {
                problems.Add($"timing point: {e.Message}");
            }
        }

        foreach (var ho in file.HitObjects)
        {
            try
            {
                _ = ho.X + ho.Y + ho.Time;
                ho.HitSound = ho.HitSound;
                if (ho.HitSample is { } sample) ho.HitSample = sample;
                switch (ho.Kind)
                {
                    case HitObjectKind.Slider:
                        _ = ho.Slides + ho.Length;
                        _ = ho.EdgeSounds;
                        _ = ho.EdgeSets;
                        break;
                    case HitObjectKind.Spinner:
                        _ = ho.SpinnerEndTime;
                        break;
                }
            }
            catch (FormatException e)
            {
                problems.Add($"hit object: {e.Message}");
            }
        }

        foreach (var line in file.Lines.Where(l => l.IsEdited))
            problems.Add($"same-value write changed a line: {line.Text}");

        return problems;
    }

    private static bool IsOsu(string path) => Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase);
}
