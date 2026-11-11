using System.Text;

namespace OsuHitsound.App.Tests;

public class OsuStableReaderTests
{
    /// <summary>A made-up 32-bit process: bytes at addresses, and code where the two patterns are found.</summary>
    private sealed class FakeMemory : IProcessMemory
    {
        public readonly Dictionary<uint, byte> Bytes = [];
        public uint? BaseAt, StatusAt;
        public void Int(uint address, int value) { var b = BitConverter.GetBytes(value); for (int i = 0; i < 4; i++) Bytes[address + (uint)i] = b[i]; }
        public void DotNetString(uint obj, string text)
        {
            Int(obj, 0x12345678); // method table
            Int(obj + 4, text.Length);
            var chars = Encoding.Unicode.GetBytes(text);
            for (int i = 0; i < chars.Length; i++) Bytes[obj + 8 + (uint)i] = chars[i];
        }
        public bool Read(uint address, Span<byte> into)
        {
            for (int i = 0; i < into.Length; i++)
                if (!Bytes.TryGetValue(address + (uint)i, out into[i])) return false;
            return true;
        }
        public uint?[] Find(params byte[][] patterns) =>
            [.. patterns.Select(p => p.SequenceEqual(OsuStableReader.BasePattern) ? BaseAt : p.SequenceEqual(OsuStableReader.StatusPattern) ? StatusAt : null)];
    }

    private static FakeMemory Stable(int status, bool beatmap = true)
    {
        var m = new FakeMemory { BaseAt = 0x1000_0000, StatusAt = 0x2000_0000 };
        m.Int(0x2000_0000 - 4, 0x3000_0000); m.Int(0x3000_0000, status);
        m.Int(0x1000_0000 - 0xC, 0x4000_0000);                // → the variable holding the current beatmap
        m.Int(0x4000_0000, beatmap ? 0x5000_0000 : 0);       // → the beatmap object
        m.Int(0x5000_0000 + OsuStableReader.FolderOffset, 0x6000_0000);
        m.Int(0x5000_0000 + OsuStableReader.OsuFileOffset, 0x6100_0000);
        m.DotNetString(0x6000_0000, "1016852 GALNERYUS - TEAR OFF YOUR CHAIN");
        m.DotNetString(0x6100_0000, "GALNERYUS - TEAR OFF YOUR CHAIN (Bibbity Bill) [Insane].osu");
        return m;
    }

    [Fact]
    public void The_current_beatmap_and_state_are_read_through_the_pointers()
    {
        var state = new OsuStableReader(Stable(OsuStableState.Editor)).Read()!;
        Assert.True(state.InEditor);
        Assert.Equal("1016852 GALNERYUS - TEAR OFF YOUR CHAIN", state.Folder);
        Assert.Equal("GALNERYUS - TEAR OFF YOUR CHAIN (Bibbity Bill) [Insane].osu", state.OsuFile);
    }

    [Fact]
    public void No_loaded_beatmap_reads_as_none()
    {
        var state = new OsuStableReader(Stable(status: 0, beatmap: false)).Read()!;
        Assert.False(state.InEditor);
        Assert.Null(state.OsuFile);
    }

    [Fact]
    public void A_build_with_another_layout_reads_as_unreadable_not_as_a_wrong_map()
    {
        var moved = Stable(OsuStableState.Editor);
        moved.BaseAt = null; // the pattern isn't in this build
        Assert.Null(new OsuStableReader(moved).Read());
        var broken = Stable(OsuStableState.Editor);
        broken.Bytes.Remove(0x1000_0000 - 0xC); // the pointer can't be followed
        Assert.Null(new OsuStableReader(broken).Read());
    }

    [Fact]
    public void Implausible_strings_are_refused()
    {
        var m = Stable(OsuStableState.Editor);
        m.Int(0x6100_0000 + 4, 1_000_000); // a "string" a million characters long is not a file name
        Assert.Null(new OsuStableReader(m).Read()!.OsuFile);
    }
}

public class MtipcTests
{
    [Fact]
    public void The_beatmap_reply_gives_its_folder_and_file()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(2.6); writer.Write(1.0);
            writer.Write(9.9f); writer.Write(4f); writer.Write(5f); writer.Write(8f);
            writer.Write("dawsu - Arch Echo - SUPER SUDDEN DEATH (Yoge God) (41) [492FD131740E]");
            writer.Write("Arch Echo - SUPER SUDDEN DEATH (Yoge God) [300%].osu");
            writer.Write(186999); writer.Write(0f); writer.Write(0f);
        }
        var (folder, file) = Mtipc.ParseBeatmap(stream.ToArray());
        Assert.Equal("dawsu - Arch Echo - SUPER SUDDEN DEATH (Yoge God) (41) [492FD131740E]", folder);
        Assert.Equal("Arch Echo - SUPER SUDDEN DEATH (Yoge God) [300%].osu", file);
    }

    [Fact]
    public void A_short_reply_is_refused() => Assert.ThrowsAny<EndOfStreamException>(() => Mtipc.ParseBeatmap([1, 2, 3]));
}
