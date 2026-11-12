using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Format.Tests;

/// <summary>
/// The committed save pack (tests/fixtures/save-pack): files that must survive a save with every unedited line
/// byte-identical (ADR 0002). Unlike the corpus, it runs on every clone.
/// </summary>
public class SavePackTests
{
    // One case per edge case the pack covers. A new file in the pack must be added here (see Every_pack_file_is_listed).
    public static TheoryData<string> Files => new(Names);

    private static readonly string[] Names =
    [
        "synthetic/bom-crlf-v14.osu",
        "synthetic/lf-no-final-newline.osu",
        "synthetic/mixed-line-endings.osu",
        "synthetic/v3-short-fields.osu",
        "synthetic/v7-old-hitsample.osu",
        "synthetic/v128-lazer-style.osu",
        "synthetic/invalid-utf8-lines.osu",
        "synthetic/storyboard-and-colours.osu",
        "synthetic/unknown-sections-and-comments.osu",
        "synthetic/custom-samples-and-edges.osu",
        "synthetic/mania-4k-holds-keysounds.osu",
        "synthetic/taiko-don-kat-finishers.osu",
        "synthetic/catch-fruits-juice-bananas.osu",
        "owner/catalyst-impetus-std.osu",
        "owner/catalyst-hitsound-diff.osu",
        "owner/mukduk-lazer-v128-bom.osu",
        "owner/satellites-mania-18k-keysounds.osu",
    ];

    [Theory]
    [MemberData(nameof(Files))]
    public void Unedited_file_writes_back_identical_bytes(string file)
    {
        var problems = RoundTripCheck.RunFile(PathOf(file));
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Editing_one_hit_object_changes_only_its_line(string file)
    {
        var original = File.ReadAllBytes(PathOf(file));
        var osu = OsuFile.Parse(original);
        var first = osu.HitObjects.First();
        first.HitSound ^= HitSound.Whistle;

        var before = SplitLines(original);
        var after = SplitLines(osu.ToBytes());

        Assert.Equal(before.Count, after.Count);
        var changed = Enumerable.Range(0, before.Count).Where(i => !before[i].SequenceEqual(after[i])).ToList();
        Assert.Single(changed);
        Assert.Equal(first.Line.Text, Encoding.UTF8.GetString(after[changed[0]]).TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Every_pack_file_is_listed()
    {
        var onDisk = RoundTripCheck.FindFiles(Folder)
            .Select(f => Path.GetRelativePath(Folder, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
        Assert.Equal(Names.Order(StringComparer.Ordinal), onDisk);
    }

    private static string Folder => Path.Combine(CorpusFactAttribute.RepoRoot(), "tests", "fixtures", "save-pack");

    private static string PathOf(string file) => Path.Combine(Folder, file);

    // Each line with its own terminator (CRLF, LF or CR), so a changed line ending also counts as a change.
    private static List<byte[]> SplitLines(byte[] bytes)
    {
        var lines = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') i++;
            else if (bytes[i] != (byte)'\r' && bytes[i] != (byte)'\n') continue;
            lines.Add(bytes[start..(i + 1)]);
            start = i + 1;
        }

        if (start < bytes.Length) lines.Add(bytes[start..]);
        return lines;
    }
}
