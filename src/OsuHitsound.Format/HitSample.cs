namespace OsuHitsound.Format;

public enum SampleSet
{
    /// <summary>On an object: inherit from the timing point. On a timing point: the map's default.</summary>
    None = 0,
    Normal = 1,
    Soft = 2,
    Drum = 3,
}

[Flags]
public enum HitSound
{
    None = 0,
    Normal = 1,
    Whistle = 2,
    Finish = 4,
    Clap = 8,
}

/// <summary>
/// The hitSample field, normalSet:additionSet:index:volume:filename. Missing parts read as 0 / null,
/// matching lazer's parser, so "1:0" and "1:0:0:0:" compare equal.
/// </summary>
public readonly record struct HitSample(SampleSet NormalSet, SampleSet AdditionSet, int Index, int Volume, string? Filename)
{
    public static HitSample Parse(string s)
    {
        var parts = s.Split(':');
        int Part(int i) => i < parts.Length && parts[i].Length > 0 ? OsuNumber.ParseInt(parts[i]) : 0;
        string? filename = parts.Length > 4 && parts[4].Length > 0 ? parts[4] : null;
        return new((SampleSet)Part(0), (SampleSet)Part(1), Part(2), Part(3), filename);
    }

    /// <summary>The wiki's canonical form, with the trailing colon.</summary>
    public override string ToString() => $"{(int)NormalSet}:{(int)AdditionSet}:{Index}:{Volume}:{Filename}";
}
