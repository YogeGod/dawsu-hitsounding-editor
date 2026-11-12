using System.Buffers.Binary;

namespace OsuHitsound.Lanes;

/// <summary>A MIDI note in timeline milliseconds. Channels are numbered 1–16, tracks from zero.</summary>
public sealed record MidiNote(int Track, int Channel, int Note, int Velocity, int ReleaseVelocity, double Time, double Duration)
{
    public int? StartOrder { get; init; }
    public int? EndOrder { get; init; }
}

/// <summary>A tempo change in timeline milliseconds and microseconds per quarter note.</summary>
public sealed record MidiTempo(double Time, int MicrosecondsPerQuarter)
{
    public int Track { get; init; }
    public int? Order { get; init; }
}

/// <summary>A non-note, non-tempo event, including its status but excluding its delta time.</summary>
public sealed record MidiPreservedEvent(int Track, double Time, byte[] Data)
{
    public int? Order { get; init; }
}

/// <summary>Notes, tempo and other events decoded from a MIDI file.</summary>
public sealed record MidiDocument(IReadOnlyList<MidiNote> Notes, IReadOnlyList<MidiTempo> Tempos, int TicksPerQuarter)
{
    public int? SourceFormat { get; init; }
    public int SourceTrackCount { get; init; }
    public IReadOnlyList<MidiPreservedEvent> OtherEvents { get; init; } = [];
}

/// <summary>Standard MIDI files with metrical timing (formats 0 and 1). No osu! data is changed.</summary>
public static class MidiFile
{
    private const int MaxFileSize = 32 * 1024 * 1024;
    private const int MaxEvents = 1_000_000;
    private const int Division = 1000; // 120 BPM: two ticks per millisecond

    private sealed record RawNote(int Track, int Channel, int Note, int Velocity, int ReleaseVelocity, long Start, long End, int StartOrder, int EndOrder);
    private sealed record Tempo(long Tick, int Microseconds, int Order, int Track);
    private sealed record RawEvent(int Track, long Tick, byte[] Data, int Order);

    public static MidiDocument Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxFileSize) throw new InvalidDataException("MIDI file is too large.");
        var reader = new Reader(bytes.ToArray());
        if (reader.Text(4) != "MThd") throw new InvalidDataException("Missing MIDI header.");
        uint headerLength = reader.UInt32();
        if (headerLength < 6 || headerLength > reader.Remaining) throw new InvalidDataException("Invalid MIDI header length.");
        int format = reader.UInt16(), trackCount = reader.UInt16(), division = reader.UInt16();
        reader.Skip((int)headerLength - 6);
        if (format is not (0 or 1) || trackCount == 0 || (format == 0 && trackCount != 1))
            throw new InvalidDataException("Only MIDI formats 0 and 1 are supported.");
        if (division == 0 || (division & 0x8000) != 0) throw new InvalidDataException("SMPTE MIDI timing is not supported.");
        var tempos = new List<Tempo> { new(0, 500_000, -1, 0) };
        var notes = new List<RawNote>();
        var otherEvents = new List<RawEvent>();
        int order = 0;
        for (int track = 0; track < trackCount; track++)
        {
            if (reader.Text(4) != "MTrk") throw new InvalidDataException("Missing MIDI track.");
            uint length = reader.UInt32();
            if (length > reader.Remaining) throw new InvalidDataException("Truncated MIDI track.");
            var data = new Reader(reader.Bytes((int)length));
            var active = new Dictionary<(int Channel, int Note), Queue<(long Tick, int Velocity, int Order)>>();
            long tick = 0;
            int running = 0;
            while (data.Remaining > 0)
            {
                if (++order > MaxEvents) throw new InvalidDataException("MIDI file has too many events.");
                tick = checked(tick + data.Variable());
                int first = data.Byte();
                int status;
                int? firstData = null;
                if (first < 0x80)
                {
                    if (running == 0) throw new InvalidDataException("MIDI running status has no prior channel event.");
                    status = running;
                    firstData = first;
                }
                else
                {
                    status = first;
                    running = status is >= 0x80 and <= 0xef ? status : 0;
                }
                if (status == 0xff)
                {
                    int type = data.Byte();
                    uint size = data.Variable();
                    if (size > data.Remaining) throw new InvalidDataException("Truncated MIDI meta event.");
                    var payload = data.Bytes((int)size);
                    if (type == 0x51)
                    {
                        if (payload.Length != 3) throw new InvalidDataException("Invalid MIDI tempo.");
                        int tempo = (payload[0] << 16) | (payload[1] << 8) | payload[2];
                        if (tempo == 0) throw new InvalidDataException("Invalid MIDI tempo.");
                        tempos.Add(new Tempo(tick, tempo, order, track));
                    }
                    else if (type != 0x2f)
                    {
                        using var preserved = new MemoryStream();
                        preserved.WriteByte(0xff); preserved.WriteByte((byte)type); WriteVariable(preserved, size); preserved.Write(payload);
                        otherEvents.Add(new RawEvent(track, tick, preserved.ToArray(), order));
                    }
                    else if (size != 0 || data.Remaining != 0)
                        throw new InvalidDataException("Invalid MIDI end-of-track event.");
                    continue;
                }
                if (status is 0xf0 or 0xf7)
                {
                    uint size = data.Variable();
                    if (size > data.Remaining) throw new InvalidDataException("Truncated MIDI system event.");
                    var payload = data.Bytes((int)size);
                    using var preserved = new MemoryStream();
                    preserved.WriteByte((byte)status); WriteVariable(preserved, size); preserved.Write(payload);
                    otherEvents.Add(new RawEvent(track, tick, preserved.ToArray(), order));
                    continue;
                }
                if (status is < 0x80 or > 0xef) throw new InvalidDataException("Unsupported MIDI system event.");
                int kind = status >> 4, channel = (status & 15) + 1;
                int a = firstData ?? data.DataByte();
                int b = kind is 0xc or 0xd ? 0 : data.DataByte();
                if (kind == 9 && b > 0)
                {
                    var key = (channel, a);
                    if (!active.TryGetValue(key, out var queue)) active[key] = queue = new();
                    queue.Enqueue((tick, b, order));
                }
                else if (kind == 8 || kind == 9)
                {
                    var key = (channel, a);
                    if (!active.TryGetValue(key, out var queue) || queue.Count == 0)
                        throw new InvalidDataException("MIDI note-off has no matching note-on.");
                    var start = queue.Dequeue();
                    notes.Add(new RawNote(track, channel, a, start.Velocity, kind == 8 ? b : 0, start.Tick, tick, start.Order, order));
                }
                else
                {
                    byte[] encoded = kind is 0xc or 0xd ? [(byte)status, (byte)a] : [(byte)status, (byte)a, (byte)b];
                    otherEvents.Add(new RawEvent(track, tick, encoded, order));
                }
            }
            if (active.Values.Any(q => q.Count != 0)) throw new InvalidDataException("MIDI note-on has no matching note-off.");
        }
        // The default tempo is only needed when the file has no tempo at tick zero.
        // Do not materialize another default on every read/write cycle.
        if (tempos.Any(t => t.Tick == 0 && t.Order >= 0)) tempos.RemoveAt(0);
        var sorted = tempos.OrderBy(t => t.Tick).ThenBy(t => t.Order).ToArray();
        var elapsed = new double[sorted.Length];
        for (int i = 1; i < sorted.Length; i++)
            elapsed[i] = elapsed[i - 1] + (sorted[i].Tick - sorted[i - 1].Tick) * (double)sorted[i - 1].Microseconds / division;
        double ToMilliseconds(long target)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sorted[mid].Tick <= target) lo = mid + 1; else hi = mid;
            }
            int i = lo - 1;
            return (elapsed[i] + (target - sorted[i].Tick) * (double)sorted[i].Microseconds / division) / 1000;
        }
        return new MidiDocument([.. notes.Select(n =>
        {
            double start = ToMilliseconds(n.Start), end = ToMilliseconds(n.End);
            return new MidiNote(n.Track, n.Channel, n.Note, n.Velocity, n.ReleaseVelocity, start, end - start) { StartOrder = n.StartOrder, EndOrder = n.EndOrder };
        }).OrderBy(n => n.Time).ThenBy(n => n.Track)],
            [.. sorted.Select(t => new MidiTempo(ToMilliseconds(t.Tick), t.Microseconds) { Track = t.Track, Order = t.Order < 0 ? null : t.Order })], division)
        {
            SourceFormat = format,
            SourceTrackCount = trackCount,
            OtherEvents = [.. otherEvents.Select(e => new MidiPreservedEvent(e.Track, ToMilliseconds(e.Tick), e.Data) { Order = e.Order })]
        };
    }

    /// <summary>Writes a format 0 MIDI at 120 BPM.</summary>
    public static byte[] Write(IEnumerable<MidiNote> notes) => Write(new MidiDocument([.. notes], [new MidiTempo(0, 500_000)], Division));

    /// <summary>Writes notes and tempo changes, retaining source tracks and other events when available.</summary>
    public static byte[] Write(MidiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.TicksPerQuarter is < 1 or > 0x7fff) throw new ArgumentException("Invalid MIDI ticks per quarter.", nameof(document));
        bool retainTracks = document.SourceFormat is 0 or 1 && document.SourceTrackCount > 0;
        if (document.SourceFormat is not null && (!retainTracks || document.SourceTrackCount > ushort.MaxValue)) throw new ArgumentException("Invalid MIDI source layout.", nameof(document));
        var tempos = document.Tempos.OrderBy(t => t.Time).ToArray();
        if (tempos.Length > MaxEvents || tempos.Any(t => !double.IsFinite(t.Time) || t.Time < 0 || t.Time > int.MaxValue / 2.0 || t.MicrosecondsPerQuarter is < 1 or > 0xffffff || t.Track is < 0 or >= ushort.MaxValue))
            throw new ArgumentException("Invalid MIDI tempo.", nameof(document));
        var tempoTicks = new double[tempos.Length];
        for (int i = 0; i < tempos.Length; i++)
        {
            double previousTime = i == 0 ? 0 : tempos[i - 1].Time;
            int previousTempo = i == 0 ? 500_000 : tempos[i - 1].MicrosecondsPerQuarter;
            tempoTicks[i] = (i == 0 ? 0 : tempoTicks[i - 1]) + (tempos[i].Time - previousTime) * 1000 * document.TicksPerQuarter / previousTempo;
        }
        double ToTicks(double time)
        {
            int lo = 0, hi = tempos.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (tempos[mid].Time <= time) lo = mid + 1; else hi = mid;
            }
            int i = lo - 1;
            return i < 0 ? time * 1000 * document.TicksPerQuarter / 500_000
                : tempoTicks[i] + (time - tempos[i].Time) * 1000 * document.TicksPerQuarter / tempos[i].MicrosecondsPerQuarter;
        }
        long TickAt(double time)
        {
            if (!double.IsFinite(time) || time < 0 || time > int.MaxValue / 2.0)
                throw new ArgumentException("Invalid MIDI event time.", nameof(document));
            double tick = ToTicks(time);
            if (tick > int.MaxValue) throw new ArgumentException("MIDI event is too late.", nameof(document));
            return (long)Math.Round(tick);
        }
        var events = new List<(int Track, long Tick, long Order, byte[] Data)>();
        foreach (var t in tempos)
        {
            events.Add((retainTracks ? t.Track : 0, TickAt(t.Time), t.Order ?? (long)int.MaxValue + 1, [0xff, 0x51, 3, (byte)(t.MicrosecondsPerQuarter >> 16), (byte)(t.MicrosecondsPerQuarter >> 8), (byte)t.MicrosecondsPerQuarter]));
        }
        foreach (var e in document.OtherEvents)
        {
            if (e.Track is < 0 or >= ushort.MaxValue || !IsPreservableEvent(e.Data)) throw new ArgumentException("Invalid preserved MIDI event.", nameof(document));
            events.Add((retainTracks ? e.Track : 0, TickAt(e.Time), e.Order ?? (long)int.MaxValue + 3, e.Data));
            if (events.Count > MaxEvents) throw new ArgumentException("Too many MIDI events.", nameof(document));
        }
        var materialized = document.Notes.ToArray();
        foreach (var n in materialized.OrderBy(n => n.Time))
        {
            if (n.Track is < 0 or >= ushort.MaxValue || n.Channel is < 1 or > 16 || n.Note is < 0 or > 127 || n.Velocity is < 1 or > 127 || n.ReleaseVelocity is < 0 or > 127 ||
                !double.IsFinite(n.Time) || !double.IsFinite(n.Duration) || n.Time < 0 || n.Duration < 0 || n.Time + n.Duration > int.MaxValue / 2.0)
                throw new ArgumentException("Invalid MIDI note.", nameof(document));
            long start = TickAt(n.Time), end = TickAt(n.Time + n.Duration);
            // Ensure even a zero-length note has an observable note-on before its note-off.
            if (end <= start) end = start + 1;
            int track = retainTracks ? n.Track : 0;
            events.Add((track, start, n.StartOrder ?? (long)int.MaxValue + 4, [(byte)(0x90 | (n.Channel - 1)), (byte)n.Note, (byte)n.Velocity]));
            events.Add((track, end, n.EndOrder ?? (long)int.MaxValue + 2, [(byte)(0x80 | (n.Channel - 1)), (byte)n.Note, (byte)n.ReleaseVelocity]));
            if (events.Count > MaxEvents) throw new ArgumentException("Too many MIDI events.", nameof(document));
        }
        foreach (var group in materialized.GroupBy(n => (Track: retainTracks ? n.Track : 0, n.Channel, n.Note)))
        {
            long end = -1;
            foreach (var note in group.OrderBy(n => n.Time))
            {
                long start = (long)Math.Round(ToTicks(note.Time));
                long noteEnd = Math.Max(start + 1, (long)Math.Round(ToTicks(note.Time + note.Duration)));
                if (start < end && noteEnd < end) throw new ArgumentException("Nested notes with the same track, channel and pitch cannot be exported unambiguously.", nameof(document));
                end = noteEnd;
            }
        }
        int trackCount = retainTracks ? Math.Max(document.SourceTrackCount, events.Count == 0 ? 0 : events.Max(e => e.Track) + 1) : 1;
        if (trackCount is < 1 or > ushort.MaxValue) throw new ArgumentException("Invalid MIDI track count.", nameof(document));
        int format = retainTracks && (document.SourceFormat == 1 || trackCount > 1) ? 1 : 0;
        using var result = new MemoryStream();
        result.Write("MThd"u8); WriteUInt32(result, 6); WriteUInt16(result, format); WriteUInt16(result, trackCount); WriteUInt16(result, document.TicksPerQuarter);
        var byTrack = events.GroupBy(e => e.Track).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Tick).ThenBy(e => e.Order).ToArray());
        for (int i = 0; i < trackCount; i++)
        {
            using var track = new MemoryStream();
            long previous = 0;
            if (byTrack.TryGetValue(i, out var trackEvents))
                foreach (var e in trackEvents)
                {
                    long delta = e.Tick - previous;
                    if (delta > 0x0fffffff) throw new ArgumentException("MIDI event gap is too long.", nameof(document));
                    WriteVariable(track, (uint)delta); track.Write(e.Data); previous = e.Tick;
                }
            track.Write([0, 0xff, 0x2f, 0]);
            result.Write("MTrk"u8); WriteUInt32(result, (uint)track.Length); track.Position = 0; track.CopyTo(result);
        }
        return result.ToArray();
    }

    private static bool IsPreservableEvent(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return false;
        try
        {
            var reader = new Reader(bytes);
            int status = reader.Byte();
            if (status == 0xff)
            {
                int type = reader.Byte();
                if (type is 0x2f or 0x51) return false;
                uint size = reader.Variable();
                return size == reader.Remaining;
            }
            if (status is 0xf0 or 0xf7)
            {
                uint size = reader.Variable();
                return size == reader.Remaining;
            }
            int kind = status >> 4;
            if (kind is < 0xa or > 0xe) return false;
            reader.DataByte();
            if (kind is not (0xc or 0xd)) reader.DataByte();
            return reader.Remaining == 0;
        }
        catch (InvalidDataException) { return false; }
    }

    private static void WriteUInt16(Stream stream, int value) { stream.WriteByte((byte)(value >> 8)); stream.WriteByte((byte)value); }
    private static void WriteUInt32(Stream stream, uint value) { WriteUInt16(stream, (int)(value >> 16)); WriteUInt16(stream, (int)value); }
    private static void WriteVariable(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        int i = 3; buffer[i] = (byte)(value & 0x7f);
        while ((value >>= 7) != 0) buffer[--i] = (byte)((value & 0x7f) | 0x80);
        stream.Write(buffer[i..]);
    }

    private sealed class Reader(byte[] data)
    {
        private int position;
        public int Remaining => data.Length - position;
        public byte Byte() => position < data.Length ? data[position++] : throw new InvalidDataException("Truncated MIDI file.");
        public int DataByte() { int b = Byte(); return b < 128 ? b : throw new InvalidDataException("Invalid MIDI data byte."); }
        public ushort UInt16() { var b = Bytes(2); return BinaryPrimitives.ReadUInt16BigEndian(b); }
        public uint UInt32() { var b = Bytes(4); return BinaryPrimitives.ReadUInt32BigEndian(b); }
        public string Text(int count) => System.Text.Encoding.ASCII.GetString(Bytes(count));
        public byte[] Bytes(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated MIDI file."); var b = data.AsSpan(position, count).ToArray(); position += count; return b; }
        public void Skip(int count) { if (count < 0 || count > Remaining) throw new InvalidDataException("Truncated MIDI file."); position += count; }
        public uint Variable()
        {
            uint result = 0;
            for (int i = 0; i < 4; i++) { int b = Byte(); result = (result << 7) | (uint)(b & 0x7f); if ((b & 0x80) == 0) return result; }
            throw new InvalidDataException("Invalid MIDI variable-length number.");
        }
    }
}
