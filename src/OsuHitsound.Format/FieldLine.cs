using System.Globalization;

namespace OsuHitsound.Format;

/// <summary>
/// A comma-separated data line. Fields are read and written as their original text, so setting one
/// field leaves every other character of the line untouched, and setting a field to the value it
/// already has changes nothing.
/// </summary>
public abstract class FieldLine
{
    private string? splitText;
    private string[] fields = [];

    protected FieldLine(OsuLine line) => Line = line;

    public OsuLine Line { get; }

    // Split once per text version: the line's Text only changes when a field is set.
    private string[] Fields
    {
        get
        {
            var text = Line.Text;
            if (!ReferenceEquals(text, splitText))
            {
                fields = text.Split(',');
                splitText = text;
            }
            return fields;
        }
    }

    protected string? Field(int index) => index < Fields.Length ? Fields[index] : null;

    /// <summary>
    /// Sets one field. Older format versions omit trailing fields, so missing fields before
    /// <paramref name="index"/> are filled with <see cref="DefaultField"/>.
    /// </summary>
    protected void SetField(int index, string value)
    {
        var current = Fields;
        var updated = new string[Math.Max(current.Length, index + 1)];
        for (int i = 0; i < updated.Length; i++)
            updated[i] = i == index ? value : i < current.Length ? current[i] : DefaultField(i);
        Line.Text = string.Join(',', updated);
    }

    /// <summary>The text osu! assumes for a field the line omits.</summary>
    protected abstract string DefaultField(int index);

    protected int Int(int index) => OsuNumber.ParseInt(Field(index) ?? throw Missing(index));

    protected int? OptionalInt(int index) => Field(index) is { } f ? OsuNumber.ParseInt(f) : null;

    protected double Double(int index) => OsuNumber.ParseDouble(Field(index) ?? throw Missing(index));

    protected void SetInt(int index, int value)
    {
        if (OptionalInt(index) != value) SetField(index, value.ToString(CultureInfo.InvariantCulture));
    }

    private FormatException Missing(int index) => new($"Field {index} missing: {Line.Text}");
}
