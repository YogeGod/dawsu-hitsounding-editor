namespace OsuHitsound.Lanes;

/// <summary>Recognizes osu!'s empty PCM sample, including files with trailing tags.</summary>
public static class BlankSample
{
    public static bool IsEmptyWav(ReadOnlySpan<byte> bytes) => bytes.StartsWith((ReadOnlySpan<byte>)[
        0x52, 0x49, 0x46, 0x46, 0x24, 0, 0, 0, 0x57, 0x41, 0x56, 0x45,
        0x66, 0x6d, 0x74, 0x20, 0x10, 0, 0, 0, 1, 0, 2, 0,
        0x44, 0xac, 0, 0, 0x10, 0xb1, 2, 0, 4, 0, 0x10, 0,
        0x64, 0x61, 0x74, 0x61, 0, 0, 0, 0,
    ]);
}
