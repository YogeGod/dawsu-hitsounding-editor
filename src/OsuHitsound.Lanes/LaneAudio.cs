namespace OsuHitsound.Lanes;

/// <summary>
/// The audio each lane plays, as bytes, for one job (a save, a Generate, a preview): file lanes from the mapset
/// folder, skin lanes from the default skin. Audio is missing when the file isn't in the folder, the skin has no such
/// sound, the folder lists names only, or the bytes are empty. Other read errors (a locked file) are thrown as they
/// are. Each lane is read once; the bytes handed out are shared, so callers must not change them. One job, one thread
/// at a time: there is no locking.
/// </summary>
public sealed class LaneAudio
{
    // What was read for a lane: its bytes (null when missing), the mapset file they came from, and their hash once asked.
    private sealed class Entry(byte[]? bytes, string? file)
    {
        public byte[]? Bytes { get; } = bytes;
        public string? File { get; } = file;
        public string? Hash => Bytes is null ? null : hash ??= GenerateManifest.Hash(Bytes);
        private string? hash;
    }

    private readonly IReadOnlyDictionary<string, byte[]>? skin;
    private readonly Dictionary<Lane, Entry> read;

    public LaneAudio(MapFolder folder, IReadOnlyDictionary<string, byte[]>? skin = null) : this(folder, skin, []) { }

    private LaneAudio(MapFolder folder, IReadOnlyDictionary<string, byte[]>? skin, Dictionary<Lane, Entry> read)
    {
        Folder = folder;
        this.skin = skin;
        this.read = read;
    }

    /// <summary>The folder listing this job reads from.</summary>
    public MapFolder Folder { get; }

    /// <summary>The same audio over a listing with more names (files about to be written); what was read is kept.</summary>
    public LaneAudio With(IEnumerable<string> moreFiles) => new(Folder.With(moreFiles), skin, read);

    /// <summary>The mapset files handed out so far (name on disk → SHA-256), to check they are unchanged before writing.</summary>
    public IReadOnlyDictionary<string, string> FilesRead => read.Values.Where(e => e is { File: not null, Bytes: not null })
        .DistinctBy(e => e.File, StringComparer.OrdinalIgnoreCase).ToDictionary(e => e.File!, e => e.Hash!, StringComparer.OrdinalIgnoreCase);

    /// <summary>The lane's audio, or null when it is missing.</summary>
    public byte[]? TryRead(Lane lane) => EntryFor(lane).Bytes;

    /// <summary>The lane's audio; throws <see cref="IOException"/> "Missing name." when it is missing.</summary>
    public byte[] Read(Lane lane) => TryRead(lane) ?? throw Missing(lane);

    /// <summary>The SHA-256 of the lane's audio (as <see cref="GenerateManifest.Hash"/>), or null when it is missing.</summary>
    public string? TryHash(Lane lane) => EntryFor(lane).Hash;

    /// <summary>The SHA-256 of the lane's audio; throws like <see cref="Read"/> when it is missing.</summary>
    public string Hash(Lane lane) => TryHash(lane) ?? throw Missing(lane);

    private static IOException Missing(Lane lane) => new($"Missing {lane.Name}.");

    private Entry EntryFor(Lane lane)
    {
        if (read.TryGetValue(lane, out var known)) return known;
        if (lane.Source == LaneSource.Skin) return read[lane] = new(NonEmpty(skin?.GetValueOrDefault(lane.Name)), null);
        if (Folder.Find(lane.Name) is not { } file) return read[lane] = new(null, null);
        byte[]? bytes;
        try { bytes = Folder.ReadBytes(file); }
        catch (InvalidOperationException) { bytes = null; } // a listing of names only
        return read[lane] = new(NonEmpty(bytes), file);
    }

    private static byte[]? NonEmpty(byte[]? bytes) => bytes is { Length: > 0 } ? bytes : null;
}
