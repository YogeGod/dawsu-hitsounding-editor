namespace OsuHitsound.Format;

/// <summary>
/// A [TimingPoints] line: time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects.
/// Old format versions omit trailing fields, so those properties are null when absent.
/// </summary>
public sealed class TimingPoint(OsuLine line) : FieldLine(line)
{
    public double Time => Double(0);

    public double BeatLength => Double(1);

    /// <summary>Beats per bar (red lines). Absent or not positive means 4.</summary>
    public int Meter => OptionalInt(2) is > 0 and var m ? m : 4;

    /// <summary>Kiai time is on from this point (effects bit 1).</summary>
    public bool Kiai => ((OptionalInt(7) ?? 0) & 1) != 0;

    /// <summary>Red line. Absent means uninherited.</summary>
    public bool Uninherited => OptionalInt(6) is null or 1;

    public SampleSet? SampleSet
    {
        get => (SampleSet?)OptionalInt(3);
        set => SetRequired(3, (int?)value);
    }

    public int? SampleIndex
    {
        get => OptionalInt(4);
        set => SetRequired(4, value);
    }

    public int? Volume
    {
        get => OptionalInt(5);
        set => SetRequired(5, value);
    }

    // meter, sampleSet (0 = the map's default), sampleIndex, volume, uninherited, effects.
    protected override string DefaultField(int index) => index switch
    {
        2 => "4",
        3 or 4 or 7 => "0",
        5 => "100",
        6 => "1",
        _ => throw new InvalidOperationException($"Timing point field {index} has no default: {Line.Text}"),
    };

    private void SetRequired(int index, int? value) =>
        SetInt(index, value ?? throw new ArgumentNullException(nameof(value), "Timing point fields can't be removed."));
}
