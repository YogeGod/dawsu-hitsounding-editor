namespace OsuHitsound.App;

/// <summary>A snap step measured in osu! beats, independent of the measure's time signature.</summary>
internal readonly record struct SnapSpacing
{
    public int Beats { get; }
    public int Divisions { get; }
    public bool IsLong => Beats > 1;

    public SnapSpacing(int beats, int divisions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beats);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisions);
        if (beats > 1 && divisions > 1) throw new ArgumentException("Use whole beats or a fraction of one beat.");
        Beats = beats;
        Divisions = divisions;
    }

    public double Step(double beatLength) => beatLength * Beats / Divisions;
    public override string ToString() => $"{Beats}/{Divisions}";
}
