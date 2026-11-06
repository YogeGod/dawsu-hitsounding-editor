using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Lanes.Tests;

public class MidiImportTests
{
    // A minimal Standard MIDI File writer for the tests.
    private static byte[] Smf(int format, int ticksPerBeat, params byte[][] tracks)
    {
        var bytes = new List<byte>();
        void U16(int v) { bytes.Add((byte)(v >> 8)); bytes.Add((byte)v); }
        void U32(int v) { U16(v >> 16); U16(v & 0xFFFF); }
        bytes.AddRange("MThd"u8.ToArray()); U32(6); U16(format); U16(tracks.Length); U16(ticksPerBeat);
        foreach (var t in tracks) { bytes.AddRange("MTrk"u8.ToArray()); U32(t.Length); bytes.AddRange(t); }
        return [.. bytes];
    }

    private static byte[] Vlq(int value)
    {
        var groups = new List<byte> { (byte)(value & 0x7F) };
        while ((value >>= 7) > 0) groups.Insert(0, (byte)(0x80 | (value & 0x7F)));
        return [.. groups];
    }

    private static byte[] Ev(int delta, params byte[] data) => [.. Vlq(delta), .. data];
    private static byte[] Name(string name) => Ev(0, [0xFF, 0x03, (byte)name.Length, .. System.Text.Encoding.ASCII.GetBytes(name)]);
    private static readonly byte[] End = Ev(0, 0xFF, 0x2F, 0x00);

    [Fact]
    public void Notes_running_status_zero_velocity_offs_and_controllers_are_read()
    {
        byte[] track =
        [
            .. Name("Lead"),
            .. Ev(0, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20), // 500000 µs per beat: 120 BPM
            .. Ev(0, 0x90, 60, 100),       // C4 on
            .. Ev(0, 64, 90),              // running status: E4 on
            .. Ev(480, 60, 0),             // running status, velocity 0: C4 off
            .. Ev(0, 0xB0, 64, 127),       // sustain down
            .. Ev(0, 0xE0, 0x00, 0x60),    // pitch bend +4096
            .. Ev(240, 0x80, 64, 0),       // E4 off
            .. Ev(0, 0xB0, 1, 30),         // modulation
            .. Ev(0, 0xB0, 7, 100),        // volume: not kept
            .. End,
        ];
        var file = MidiFile.Parse(Smf(1, 480, track));
        Assert.Equal((1, 480, 120.0), (file.Format, file.TicksPerBeat, file.FirstTempoBpm!.Value));
        var t = Assert.Single(file.Tracks);
        Assert.Equal("Lead", t.Name);
        Assert.Equal([new MidiNote(0, 480, 0, 60, 100), new MidiNote(0, 720, 0, 64, 90)], t.Notes);
        Assert.Equal([new MidiController(480, 0, ControllerKind.Sustain, 127), new MidiController(480, 0, ControllerKind.PitchBend, 4096),
            new MidiController(720, 0, ControllerKind.Modulation, 30)], t.Controllers);
    }

    [Fact]
    public void Overlapping_notes_of_one_pitch_end_first_in_first_out_and_held_notes_end_with_the_track()
    {
        byte[] track = [.. Ev(0, 0x90, 60, 80), .. Ev(100, 0x90, 60, 90), .. Ev(100, 0x80, 60, 0), .. Ev(100, 0x90, 62, 70), .. Ev(200, 0xFF, 0x2F, 0x00)];
        var notes = MidiFile.Parse(Smf(0, 96, track)).Tracks[0].Notes;
        Assert.Equal([new MidiNote(0, 200, 0, 60, 80), new MidiNote(100, 400, 0, 60, 90), new MidiNote(300, 200, 0, 62, 70)], notes);
    }

    [Fact]
    public void Format_0_tracks_split_by_channel_and_channel_10_is_percussion()
    {
        byte[] track = [.. Name("Song"), .. Ev(0, 0x90, 60, 100), .. Ev(0, 0x99, 36, 110), .. Ev(96, 0x80, 60, 0), .. Ev(0, 0x89, 36, 0), .. End];
        var parts = MidiImport.Parts(MidiFile.Parse(Smf(0, 96, track)));
        Assert.Equal(["Song (ch. 1)", "Song (ch. 10)"], parts.Select(p => p.Name));
        Assert.Equal([false, true], parts.Select(p => p.Percussion));
    }

    [Fact]
    public void Import_keeps_beat_positions_from_the_chosen_bar()
    {
        var part = new MidiImportPart("Lead", 0, [new MidiNote(96, 48, 0, 60, 100)], [new MidiController(96, 0, ControllerKind.Sustain, 127)]);
        var map = new TempoMap([(1000, 500, 4)]);
        Assert.Equal(2, map.FirstBar); // as Hitsound mode counts: one whole bar fits before the red line at 1000 ms
        long origin = (long)map.TickOfBarBeat(4, 1); // the file's beat 1 lands on bar 4, two bars after the red line
        var track = Assert.Single(MidiImport.Tracks([part], ticksPerBeat: 96, origin));
        var note = Assert.Single(track.Notes);
        Assert.Equal((8L * 960 + 960, 480L), (note.Start, note.Length)); // bar 4 = 8 beats in, then one more beat; half a beat long
        Assert.Equal(1000 + 9 * 500, map.TimeAt(note.Start), 6);
        Assert.Equal(note.Start, track.Controllers.Single().Tick);
        Assert.Equal(KeyInstrument.Piano, track.Instrument);
        Assert.True(track.IncludeInGenerate);
    }

    [Fact]
    public void Bar_and_beat_ticks_restart_at_each_red_line()
    {
        var map = new TempoMap([(0, 500, 4), (1100, 600, 3)]);
        Assert.Equal(0, map.TickOfBarBeat(1, 1));
        Assert.Equal(3 * 960, map.TickOfBarBeat(1, 4));
        Assert.Equal(map.TickAt(1100), map.TickOfBarBeat(2, 1), 6); // the partial bar counts as one
        Assert.Equal(map.TickAt(1100 + 3 * 600), map.TickOfBarBeat(3, 1), 6);
        Assert.Equal(-4 * 960, map.TickOfBarBeat(0, 1));
        Assert.Equal((2, 1), map.BarBeatAt(map.TimeAt(map.TickOfBarBeat(2, 1))));
    }

    [Fact]
    public void The_sustain_pedal_holds_notes_to_its_release()
    {
        KeyController S(long tick, int value) => new(tick, ControllerKind.Sustain, value);
        var pedal = new[] { S(100, 127), S(500, 0), S(800, 127) };
        Assert.Equal(50, KeyControllers.SustainedEnd(pedal, 50));    // before the pedal
        Assert.Equal(500, KeyControllers.SustainedEnd(pedal, 200));  // held to the release
        Assert.Equal(600, KeyControllers.SustainedEnd(pedal, 600));  // after the release
        Assert.Equal(900, KeyControllers.SustainedEnd(pedal, 900));  // never released: not held forever
    }

    [Fact]
    public void Damaged_and_unsupported_files_are_refused()
    {
        Assert.Throws<FormatException>(() => MidiFile.Parse("RIFF0000"u8));
        Assert.Throws<FormatException>(() => MidiFile.Parse(Smf(2, 96, End)));
        Assert.Throws<FormatException>(() => MidiFile.Parse(Smf(1, 0xE728, End))); // SMPTE division
        var cut = Smf(1, 96, [.. Ev(0, 0x90, 60, 100), .. End]);
        Assert.Throws<FormatException>(() => MidiFile.Parse(cut.AsSpan(0, cut.Length - 3)));
        Assert.Throws<FormatException>(() => MidiFile.Parse(Smf(1, 96, [0x00, 60, 100]))); // data byte with no status
        Assert.Throws<FormatException>(() => MidiFile.Parse(Smf(1, 96, [0xFF, 0xFF, 0xFF, 0xFF, 0x7F])));  // 5-byte length
    }

    [Fact]
    public void Notes_outside_the_project_range_are_left_out_so_the_project_still_saves()
    {
        var part = new MidiImportPart("Odd", 0, [new MidiNote(0, 1, 0, 60, 100), new MidiNote(int.MaxValue, 1, 0, 62, 100)],
            [new MidiController(int.MaxValue, 0, ControllerKind.Sustain, 127)]);
        var track = MidiImport.Tracks([part], ticksPerBeat: 1, origin: 0)[0];
        Assert.Single(track.Notes);
        Assert.Empty(track.Controllers);
        KeysoundStore.Validate(new KeysoundProjectFile { Reference = "a.osu", Tracks = [track] });
    }

    [Fact]
    public void Imported_controllers_and_the_percussion_flag_are_saved_with_the_project()
    {
        var track = MidiImport.Tracks([new MidiImportPart("Drums", 9, [new MidiNote(0, 96, 9, 36, 100)], [new MidiController(0, 9, ControllerKind.PitchBend, -8192)])], 96, 0)[0];
        var project = new KeysoundProjectFile { Reference = "a.osu", Tracks = [track] };
        KeysoundStore.Validate(project);
        var json = System.Text.Json.JsonSerializer.Serialize(project);
        var back = System.Text.Json.JsonSerializer.Deserialize<KeysoundProjectFile>(json)!;
        Assert.True(back.Tracks[0].Percussion);
        Assert.Equal(track.Controllers, back.Tracks[0].Controllers);
        Assert.Throws<FormatException>(() => KeysoundStore.Validate(project with { Tracks = [track with { Controllers = [new KeyController(0, ControllerKind.Sustain, 200)] }] }));
    }
}
