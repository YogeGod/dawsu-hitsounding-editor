using System.Collections.Immutable;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>One track a MIDI file offers for import: a track's notes on one channel.</summary>
public sealed record MidiImportPart(string Name, int Channel, IReadOnlyList<MidiNote> Notes, IReadOnlyList<MidiController> Controllers)
{
    /// <summary>MIDI channel 10 (index 9) is General MIDI percussion.</summary>
    public bool Percussion => Channel == 9;
}

/// <summary>
/// Turns a MIDI file into Keysound tracks. Notes keep their beat positions: the file's beat 1 lands on a chosen bar and
/// beat of the reference, and from there notes follow the reference's red lines, not the file's tempo.
/// </summary>
public static class MidiImport
{
    /// <summary>The parts to offer: each track split by channel (format 0 files keep everything on one track).</summary>
    public static List<MidiImportPart> Parts(MidiFile file)
    {
        var parts = new List<MidiImportPart>();
        foreach (var track in file.Tracks)
        {
            var channels = track.Notes.Select(n => n.Channel).Distinct().Order().ToList();
            foreach (int channel in channels)
            {
                string name = channels.Count > 1 ? $"{track.Name} (ch. {channel + 1})" : track.Name;
                parts.Add(new MidiImportPart(name, channel, [.. track.Notes.Where(n => n.Channel == channel)],
                    [.. track.Controllers.Where(c => c.Channel == channel)]));
            }
        }

        return parts;
    }

    /// <summary>File ticks to project ticks (<see cref="TempoMap.Ppq"/> per beat), placed at <paramref name="origin"/>.</summary>
    public static long ToProjectTick(long fileTick, int ticksPerBeat, long origin) =>
        origin + (long)Math.Round(fileTick * (double)TempoMap.Ppq / ticksPerBeat);

    /// <summary>The Keysound tracks for the chosen parts, with the bundled piano, Include in Generate on.</summary>
    public static List<KeyTrack> Tracks(IEnumerable<MidiImportPart> parts, int ticksPerBeat, long origin) =>
    [
        .. parts.Select(part => KeyTrack.Create(part.Name.Length > 256 ? part.Name[..256] : part.Name) with
        {
            // Anything outside the project's tick range (a damaged or extreme file) is left out, so the project still saves.
            Notes = [.. part.Notes.Select(n =>
            {
                long start = ToProjectTick(n.Tick, ticksPerBeat, origin);
                long length = Math.Max(1, ToProjectTick(n.Tick + n.Length, ticksPerBeat, origin) - start);
                return new KeyNote(Guid.NewGuid(), start, length, n.Pitch, Math.Clamp(n.Velocity, KeyNote.MinVelocity, KeyNote.MaxVelocity));
            }).Where(n => n.IsValid)],
            Controllers = [.. part.Controllers.Select(c => new KeyController(ToProjectTick(c.Tick, ticksPerBeat, origin), c.Kind, c.Value))
                .Where(c => c.IsValid).OrderBy(c => c.Tick)],
            Percussion = part.Percussion,
        }),
    ];

}
