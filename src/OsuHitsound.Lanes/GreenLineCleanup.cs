using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// Clean up green lines (issue #29): finds green (inherited) lines that change nothing, because the settings in force
/// just before them (from the line before in time, red or green) are already the same: sample set, index and volume as
/// they play, slider velocity and kiai. Red lines, and old short lines that don't say they're inherited, are never
/// touched. It is its own command: Save never runs it.
/// </summary>
public static class GreenLineCleanup
{
    /// <summary>The green lines that change nothing, in file order.</summary>
    public static IReadOnlyList<TimingPoint> Redundant(OsuFile file)
    {
        var settings = MapSettings.Read(file);
        var redundant = new List<TimingPoint>();
        Settings? inForce = null;
        // In time order; at one time the later line wins, as osu! reads them.
        foreach (var point in file.TimingPoints.Select((p, i) => (p, i)).Where(x => Readable(x.p)).OrderBy(x => x.p.Time).ThenBy(x => x.i).Select(x => x.p))
        {
            var now = SettingsOf(point, inForce, settings);
            if (!point.Uninherited && inForce == now) redundant.Add(point);
            else inForce = now;
        }

        return redundant;
    }

    /// <summary>Removes the green lines that change nothing; returns how many. A map with none is left unedited.</summary>
    public static int Clean(OsuFile file)
    {
        var redundant = Redundant(file);
        foreach (var point in redundant) file.RemoveGreenLine(point);
        return redundant.Count;
    }

    private sealed record Settings(SampleSet Set, int Index, int Volume, double BeatLength, bool Kiai);

    // What plays from this line on. A red line resets the slider velocity (-100 = 1×); a green one sets it.
    private static Settings SettingsOf(TimingPoint point, Settings? before, MapSettings map)
    {
        var (set, index, volume) = Decompiler.PointState(point, map);
        return new Settings(set, index, volume, point.Uninherited ? -100 : point.BeatLength, point.Kiai);
    }

    private static bool Readable(TimingPoint point)
    {
        try { return double.IsFinite(point.Time) && double.IsFinite(point.BeatLength) && point.Effects >= 0; }
        catch (FormatException) { return false; }
    }
}
