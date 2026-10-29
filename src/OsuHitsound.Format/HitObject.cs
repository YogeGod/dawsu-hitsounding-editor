namespace OsuHitsound.Format;

public enum HitObjectKind
{
    Circle,
    Slider,
    Spinner,
    Hold,
}

/// <summary>
/// A [HitObjects] line: x,y,time,type,hitSound,objectParams...,hitSample.
/// </summary>
public sealed class HitObject(OsuLine line) : FieldLine(line)
{
    // Field positions of the parts this editor reads or writes.
    private const int TypeField = 3, HitSoundField = 4, SpinnerEndField = 5, SlidesField = 6, LengthField = 7, EdgeSoundsField = 8, EdgeSetsField = 9;

    public double X => Double(0);

    public double Y => Double(1);

    public double Time => Double(2);

    public int Type => Int(TypeField);

    public HitObjectKind Kind =>
        (Type & 2) != 0 ? HitObjectKind.Slider
        : (Type & 8) != 0 ? HitObjectKind.Spinner
        : (Type & 128) != 0 ? HitObjectKind.Hold
        : HitObjectKind.Circle;

    public HitSound HitSound
    {
        get => (HitSound)Int(HitSoundField);
        set => SetInt(HitSoundField, (int)value);
    }

    /// <summary>Null when the line has no hitSample field (it then behaves as 0:0:0:0:), and for mania holds.</summary>
    public HitSample? HitSample
    {
        get => HitSampleField is int i && Field(i) is { } s ? Format.HitSample.Parse(s) : null;
        set
        {
            if (value is not { } v) throw new ArgumentNullException(nameof(value), "hitSample can't be removed.");
            if ((HitSample ?? default) == v) return;
            SetField(HitSampleField ?? throw new InvalidOperationException("Mania holds keep hitSample inside the end-time field."), v.ToString());
        }
    }

    public double SpinnerEndTime
    {
        get { Require(HitObjectKind.Spinner); return Double(SpinnerEndField); }
    }

    /// <summary>Number of slider spans: 1 means no repeats.</summary>
    public int Slides
    {
        get { Require(HitObjectKind.Slider); return Int(SlidesField); }
    }

    public double Length
    {
        get { Require(HitObjectKind.Slider); return Double(LengthField); }
    }

    /// <summary>Additions per slider node (head, repeats, tail). Empty when the line omits them.</summary>
    public HitSound[] EdgeSounds
    {
        get
        {
            Require(HitObjectKind.Slider);
            return Field(EdgeSoundsField) is { Length: > 0 } s ? [.. s.Split('|').Select(p => (HitSound)OsuNumber.ParseInt(p))] : [];
        }
    }

    /// <summary>normalSet:additionSet per slider node. Empty when the line omits them.</summary>
    public (SampleSet Normal, SampleSet Addition)[] EdgeSets
    {
        get
        {
            Require(HitObjectKind.Slider);
            return Field(EdgeSetsField) is { Length: > 0 } s
                ? [.. s.Split('|').Select(Format.HitSample.Parse).Select(h => (h.NormalSet, h.AdditionSet))]
                : [];
        }
    }

    private int? HitSampleField => Kind switch
    {
        HitObjectKind.Circle => 5,
        HitObjectKind.Spinner => 6,
        HitObjectKind.Slider => 10,
        _ => null,
    };

    // Only slider edge fields can be missing before hitSample; they need one entry per node (head, repeats, tail).
    protected override string DefaultField(int index) => (Kind, index) switch
    {
        (HitObjectKind.Slider, EdgeSoundsField) => string.Join('|', Enumerable.Repeat("0", Slides + 1)),
        (HitObjectKind.Slider, EdgeSetsField) => string.Join('|', Enumerable.Repeat("0:0", Slides + 1)),
        _ => throw new InvalidOperationException($"Hit object field {index} has no default: {Line.Text}"),
    };

    private void Require(HitObjectKind kind)
    {
        if (Kind != kind) throw new InvalidOperationException($"Not a {kind}: {Line.Text}");
    }
}
