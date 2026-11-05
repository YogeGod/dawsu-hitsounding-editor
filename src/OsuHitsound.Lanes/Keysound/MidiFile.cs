using System.Text;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>A note read from a MIDI file: start and length in the file's ticks.</summary>
public sealed record MidiNote(long Tick, long Length, int Channel, int Pitch, int Velocity);

/// <summary>A controller change read from a MIDI file (sustain, modulation or pitch bend), in the file's ticks.</summary>
public sealed record MidiController(long Tick, int Channel, ControllerKind Kind, int Value);

/// <summary>One track chunk of a MIDI file.</summary>
public sealed record MidiTrackData(string Name, IReadOnlyList<MidiNote> Notes, IReadOnlyList<MidiController> Controllers);

/// <summary>
/// A Standard MIDI File (format 0 or 1) as Keysound import needs it: per track its name, notes, and the sustain,
/// modulation and pitch-bend changes. Tempo and meter are read only to report them: imported notes keep their beat
/// positions and follow the reference beatmap's red lines.
/// </summary>
public sealed record MidiFile(int Format, int TicksPerBeat, IReadOnlyList<MidiTrackData> Tracks, double? FirstTempoBpm)
{
    public const int MaxBytes = 64 * 1024 * 1024, MaxTracks = 4096, MaxEvents = 10_000_000;

    public static MidiFile Read(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new FormatException("The MIDI file is larger than 64 MB.");
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>Reads the file; throws <see cref="FormatException"/> for anything that isn't a readable format 0/1 file.</summary>
    public static MidiFile Parse(ReadOnlySpan<byte> bytes) => Parse(bytes.ToArray());

    public static MidiFile Parse(byte[] bytes)
    {
        var r = new Reader(bytes);
        if (r.Ascii(4) != "MThd") throw new FormatException("This isn't a MIDI file (no MThd header).");
        long headerLength = r.U32();
        if (headerLength < 6 || headerLength > r.Remaining) throw new FormatException("The MIDI header is damaged.");
        int format = r.U16(), count = r.U16(), division = r.U16();
        r.Skip(headerLength - 6);
        if (format > 1) throw new FormatException($"MIDI format {format} (separate songs) isn't supported; use format 0 or 1.");
        if ((division & 0x8000) != 0) throw new FormatException("SMPTE-timed MIDI files aren't supported; export with ticks per beat.");
        if (division == 0) throw new FormatException("The MIDI file has zero ticks per beat.");
        if (count > MaxTracks) throw new FormatException("The MIDI file has too many tracks.");

        var tracks = new List<MidiTrackData>();
        double? tempo = null;
        long events = 0;
        while (tracks.Count < count && r.Remaining >= 8)
        {
            string id = r.Ascii(4);
            long length = r.U32();
            if (length > r.Remaining) throw new FormatException("A MIDI track is cut short.");
            if (id != "MTrk") { r.Skip(length); continue; } // unknown chunks are skipped, as the format allows
            tracks.Add(ReadTrack(new Reader(r.Take(length)), tracks.Count, ref tempo, ref events));
        }

        if (tracks.Count == 0) throw new FormatException("The MIDI file has no tracks.");
        return new MidiFile(format, division, tracks, tempo);
    }

    private static MidiTrackData ReadTrack(Reader r, int index, ref double? tempo, ref long events)
    {
        string name = "";
        var notes = new List<MidiNote>();
        var controllers = new List<MidiController>();
        var open = new Dictionary<(int Channel, int Pitch), Queue<(long Tick, int Velocity)>>();
        long tick = 0;
        int status = 0;

        void NoteOff(int channel, int pitch)
        {
            if (open.TryGetValue((channel, pitch), out var q) && q.Count > 0)
            {
                var (start, velocity) = q.Dequeue(); // overlapping notes of one pitch end first-in, first-out
                notes.Add(new MidiNote(start, Math.Max(1, tick - start), channel, pitch, velocity));
            }
        }

        while (r.Remaining > 0)
        {
            if (++events > MaxEvents) throw new FormatException("The MIDI file has too many events.");
            tick += r.VarLength();
            int b = r.U8();
            if (b == 0xFF)
            {
                int type = r.U8(), length = r.VarLength();
                if (type is not (0x03 or 0x51)) { r.Skip(length); if (type == 0x2F) break; continue; }
                var data = r.Take(length);
                if (type == 0x03 && name.Length == 0) name = Text(data);
                else if (type == 0x51 && data.Length == 3 && tempo is null)
                {
                    int microseconds = (data[0] << 16) | (data[1] << 8) | data[2];
                    if (microseconds > 0) tempo = 60_000_000.0 / microseconds;
                }
                continue;
            }

            if (b is 0xF0 or 0xF7)
            {
                r.Skip(r.VarLength()); // system exclusive: skipped
                status = 0;
                continue;
            }

            int first;
            if (b >= 0x80) { status = b; first = r.U8(); }
            else if (status != 0) first = b; // running status: b is the first data byte
            else throw new FormatException("A MIDI event has no status byte.");

            int kind = status & 0xF0, channel = status & 0x0F;
            int second = kind is 0xC0 or 0xD0 ? 0 : r.U8();
            if (first > 127 || second > 127) throw new FormatException("A MIDI event has an invalid data byte.");
            switch (kind)
            {
                case 0x80 or 0x90 when kind == 0x80 || second == 0: // a note-on with velocity 0 is a note-off
                    NoteOff(channel, first);
                    break;
                case 0x90:
                    if (!open.TryGetValue((channel, first), out var q)) open[(channel, first)] = q = new();
                    q.Enqueue((tick, second));
                    break;
                case 0xB0 when first is 64 or 1:
                    controllers.Add(new MidiController(tick, channel, first == 64 ? ControllerKind.Sustain : ControllerKind.Modulation, second));
                    break;
                case 0xE0:
                    controllers.Add(new MidiController(tick, channel, ControllerKind.PitchBend, (second << 7 | first) - 8192));
                    break;
            }
        }

        // Notes still held when the track ends stop there.
        foreach (var ((channel, pitch), q) in open)
            while (q.Count > 0) NoteOff(channel, pitch);

        notes.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Pitch.CompareTo(b.Pitch));
        return new MidiTrackData(name.Length > 0 ? name : $"Track {index + 1}", notes, controllers);
    }

    // Track names are usually ASCII or Latin-1; UTF-8 when it decodes cleanly.
    private static string Text(byte[] data)
    {
        try { return new UTF8Encoding(false, true).GetString(data).Trim('\0', ' '); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(data).Trim('\0', ' '); }
    }

    private sealed class Reader(byte[] data)
    {
        private int pos;
        public long Remaining => data.Length - pos;

        public int U8() => pos < data.Length ? data[pos++] : throw new FormatException("The MIDI file ends unexpectedly.");
        public int U16() => (U8() << 8) | U8();
        public long U32() => ((long)U16() << 16) | (uint)U16();
        public string Ascii(int n) => Encoding.ASCII.GetString(Take(n));

        public byte[] Take(long n)
        {
            if (n < 0 || n > Remaining) throw new FormatException("The MIDI file ends unexpectedly.");
            var slice = data.AsSpan(pos, (int)n).ToArray();
            pos += (int)n;
            return slice;
        }

        public void Skip(long n)
        {
            if (n < 0 || n > Remaining) throw new FormatException("The MIDI file ends unexpectedly.");
            pos += (int)n;
        }

        /// <summary>A variable-length quantity: at most 4 bytes, as the standard requires.</summary>
        public int VarLength()
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = U8();
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }

            throw new FormatException("A MIDI length is longer than 4 bytes.");
        }
    }
}
