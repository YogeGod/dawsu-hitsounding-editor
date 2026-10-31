using System.Text;
using System.Text.Unicode;

namespace OsuHitsound.Format;

/// <summary>
/// One line of a .osu/.osb file. Keeps the original bytes and line terminator so an untouched line
/// is written back exactly as it was read, whatever its encoding.
/// </summary>
public sealed class OsuLine
{
    private readonly byte[] original;
    private string text;

    internal OsuLine(byte[] content, string decoded, byte[] terminator, string section)
    {
        original = content;
        text = decoded;
        Terminator = terminator;
        Section = section;
    }

    /// <summary>"\r\n", "\n", "\r", or empty for a last line with no newline.</summary>
    public byte[] Terminator { get; }

    /// <summary>The section this line belongs to, without brackets. Empty before the first section header.</summary>
    public string Section { get; }

    public bool IsEdited { get; private set; }

    /// <summary>A line the editor added; it counts as edited and is written from its text.</summary>
    internal void MarkInserted() => IsEdited = true;

    public string Text
    {
        get => text;
        set
        {
            if (value == text) return;
            // Text was decoded as UTF-8; re-encoding a line that wasn't would replace its other bytes with U+FFFD.
            if (!IsEdited && !Utf8.IsValid(original))
                throw new InvalidOperationException($"Line isn't valid UTF-8, so editing it would corrupt it: {text}");
            text = value;
            IsEdited = true;
        }
    }

    internal void WriteTo(Stream output)
    {
        output.Write(IsEdited ? Encoding.UTF8.GetBytes(text) : original);
        output.Write(Terminator);
    }
}
