using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App.Tests;

public class KeysoundPianoTests
{
    private const int Rate = PlaybackMixer.Rate;
    private const double MsPerFrame = 1000.0 / Rate;

    private static string PianoSfz => TestPaths.RepoFile("src", "OsuHitsound.Audio", "Instruments", "UprightPianoKW", "UprightPianoKW.sfz");

    // A stereo ramp: frame f holds f / frames on both channels, so the output shows which frame was read.
    private static float[] Ramp(int frames) => [.. Enumerable.Range(0, frames).SelectMany(f => new[] { f / (float)frames, f / (float)frames })];

    private static SfzInstrument Instrument(SfzRegion region, float[] data) => new([region], _ => data);

    private static float[] Render(KeysoundPlayer player, int frames, double start = 0, bool playing = true, double msPerFrame = MsPerFrame)
    {
        var block = new float[frames * 2];
        player.Render(block, frames, start, msPerFrame, playing);
        return block;
    }

    [Fact]
    public void The_bundled_piano_sfz_maps_every_key_in_two_velocity_layers()
    {
        var regions = SfzInstrument.Parse(File.ReadAllText(PianoSfz));
        Assert.Equal(66, regions.Count);
        Assert.All(regions, r => Assert.EndsWith(".ogg", r.Sample));
        Assert.All(regions, r => Assert.Equal(0.6, r.Release));
        for (int key = 21; key <= 108; key++)
        {
            Assert.Single(regions, r => r.Plays(key, 80)); // soft layer up to 80
            Assert.Single(regions, r => r.Plays(key, 81)); // loud layer from 81
        }

        var b0 = Assert.Single(regions, r => r.Sample == "samples/B0vH.ogg"); // key=23 sets the range and the centre
        Assert.Equal((23, 23, 23), (b0.LoKey, b0.HiKey, b0.KeyCenter));
        Assert.True(regions.Single(r => r.Sample == "samples/A0vL.ogg").HasLoop);
        Assert.False(regions.Single(r => r.Sample == "samples/C8vL.ogg").HasLoop); // the high group has no loops
        Assert.Contains(regions, r => r.Cutoff == 2500);
    }

    [Fact]
    public void Sfz_parsing_reads_headers_comments_and_sample_paths_with_spaces()
    {
        var regions = SfzInstrument.Parse("""
            // a comment
            <global> ampeg_release=1.5
            <group> lovel=10 hivel=20 loop_mode=loop_continuous
            <region> sample=my piano/a 1.ogg lokey=60 hikey=62 pitch_keycenter=61 loop_start=5 loop_end=9
            <region> key=70 sample=b.ogg fil_type=lpf_2p cutoff=900 ampeg_release=0.2
            """);
        Assert.Equal(2, regions.Count);
        Assert.Equal(new SfzRegion("my piano/a 1.ogg", 60, 62, 61, 10, 20) { LoopStart = 5, LoopEnd = 9, Release = 1.5 }, regions[0]);
        Assert.Equal((70, 70, 70, 900.0, 0.2), (regions[1].LoKey, regions[1].HiKey, regions[1].KeyCenter, regions[1].Cutoff, regions[1].Release));
    }

    [Fact]
    public void A_sample_that_fails_to_load_reports_its_own_error()
    {
        var regions = new[] { new SfzRegion("a.ogg", 0, 60, 30, 1, 127), new SfzRegion("b.ogg", 61, 127, 90, 1, 127) };
        Assert.Throws<IOException>(() => new SfzInstrument(regions, name => name == "b.ogg" ? throw new IOException("gone") : [0, 0]));
        Assert.Throws<InvalidDataException>(() => new SfzInstrument(regions, _ => null));
    }

    [Fact]
    public void A_key_outside_every_region_uses_the_nearest_in_its_velocity_layer()
    {
        var low = new SfzRegion("a", 40, 50, 45, 1, 127);
        var high = new SfzRegion("b", 70, 80, 75, 1, 127);
        var piano = new SfzInstrument([low, high], _ => [0, 0]);
        Assert.Same(high, piano.RegionFor(75, 64));
        Assert.Same(high, piano.RegionFor(100, 64));
        Assert.Same(low, piano.RegionFor(10, 64));
    }

    [Fact]
    public void Notes_are_repitched_from_their_key_centre()
    {
        var data = Ramp(1000);
        var player = new KeysoundPlayer(Instrument(new SfzRegion("r", 0, 127, 60, 1, 127), data));
        player.Audition(72, 127, ms: 1000); // an octave up reads every second frame
        var output = Render(player, 10);
        float amp = 0.7f; // full velocity
        Assert.Equal(data[2 * 2] * amp, output[1 * 2], 5);
        Assert.Equal(data[8 * 2] * amp, output[4 * 2], 5);
    }

    [Fact]
    public void A_held_note_loops_and_a_released_one_fades_out_within_its_release()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127) { LoopStart = 50, LoopEnd = 79, Release = 0.01 };
        var player = new KeysoundPlayer(Instrument(region, Ramp(100)));
        player.Audition(60, 100, ms: 1000);
        Render(player, 20_000); // far past the sample's 100 frames: still sounding
        Assert.Equal(1, player.ActiveVoices);

        Render(player, 44_100 - 20_000); // the note ends at 1 s
        var tail = Render(player, 500);
        Assert.Equal(0, player.ActiveVoices); // gone within the 441-frame release
        Assert.Equal(0, tail[^2]);
    }

    [Fact]
    public void A_loop_that_ends_on_the_last_frame_keeps_sounding()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127) { LoopStart = 10, LoopEnd = 99 };
        var player = new KeysoundPlayer(Instrument(region, Ramp(100)));
        player.Audition(61, 100, ms: 1000); // a semitone up: positions land between frames, also at the loop's end
        Render(player, 5000);
        Assert.Equal(1, player.ActiveVoices);
    }

    [Fact]
    public void A_part_s_pitch_bend_changes_how_fast_its_notes_read_the_sample()
    {
        var data = Ramp(10_000);
        var player = new KeysoundPlayer(Instrument(new SfzRegion("r", 0, 127, 60, 1, 127), data));
        player.SetNotes([new PlayedNote(0, 1000, 60, 127, Part: 0)], [[new PlayedBend(0, 12)]]); // an octave up from the start
        var output = Render(player, 100, start: 0);
        Assert.Equal(data[20 * 2] * 0.7f, output[10 * 2], 5); // two frames per output frame

        var plain = new KeysoundPlayer(Instrument(new SfzRegion("r", 0, 127, 60, 1, 127), data));
        plain.SetNotes([new PlayedNote(0, 1000, 60, 127)], [[new PlayedBend(0, 12)]]); // part -1: no bend
        Assert.Equal(data[10 * 2] * 0.7f, Render(plain, 100, start: 0)[10 * 2], 5);
    }

    [Fact]
    public void A_live_key_sounds_until_released_and_the_pedal_holds_it()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127) { LoopStart = 0, LoopEnd = 999, Release = 0.005 };
        var player = new KeysoundPlayer(Instrument(region, Ramp(1000)));
        player.LiveNoteOn(60, 100);
        Render(player, 20_000, playing: false);
        Assert.Equal(1, player.ActiveVoices); // held: still sounding long after an audition would have ended

        player.ReleaseAll(); // stopping playback doesn't cut a key that is still held
        Render(player, 1000, playing: false);
        Assert.Equal(1, player.ActiveVoices);

        player.LiveSustain(true);
        player.LiveNoteOff(60);
        Render(player, 2000, playing: false);
        Assert.Equal(1, player.ActiveVoices); // the pedal keeps it
        player.LiveSustain(false);
        Render(player, 2000, playing: false);
        Assert.Equal(0, player.ActiveVoices);
    }

    [Fact]
    public void Live_pitch_bend_bends_live_notes()
    {
        var data = Ramp(10_000);
        var instrument = new SfzInstrument([new SfzRegion("r", 0, 127, 60, 1, 127)], _ => data) { PitchBendRange = 12 };
        var player = new KeysoundPlayer(instrument);
        player.LiveBend(8191); // nearly an octave up
        player.LiveNoteOn(60, 127);
        var output = Render(player, 100, playing: false);
        Assert.Equal(data[20 * 2] * 0.7f, output[10 * 2], 2);
    }

    [Fact]
    public void Metronome_clicks_play_on_their_frames_only_while_playing()
    {
        var player = new KeysoundPlayer(Instrument(new SfzRegion("r", 0, 127, 60, 1, 127), Ramp(10)));
        player.SetClicks([(10, true), (500, false)]);
        Assert.All(Render(player, 1000, playing: false), v => Assert.Equal(0, v));
        var block = Render(player, 1000, start: 0);
        Assert.InRange(Array.FindIndex(block, v => v != 0) / 2, (int)Math.Round(10 / MsPerFrame) - 1, (int)Math.Round(10 / MsPerFrame) + 1);
        player.SetClicks(null); // the 500 ms click is never scheduled; the first one's 40 ms tail runs out
        Render(player, 4410, start: 1000 * MsPerFrame);
        Assert.All(Render(player, 44_100, start: 5410 * MsPerFrame), v => Assert.Equal(0, v));
    }

    [Fact]
    public void Keyboard_messages_decode_on_any_channel()
    {
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.NoteOn, 60, 100), MidiKeyboard.Decode(0x90 | 60 << 8 | 100 << 16));
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.NoteOff, 60, 0), MidiKeyboard.Decode(0x93 | 60 << 8)); // velocity 0, channel 4
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.NoteOff, 61, 0), MidiKeyboard.Decode(0x8F | 61 << 8 | 40 << 16));
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.Controller, 0, 127, ControllerKind.Sustain), MidiKeyboard.Decode(0xB0 | 64 << 8 | 127 << 16));
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.Controller, 0, 9, ControllerKind.Modulation), MidiKeyboard.Decode(0xB2 | 1 << 8 | 9 << 16));
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.Controller, 0, 8191, ControllerKind.PitchBend), MidiKeyboard.Decode(0xE0 | 0x7F << 8 | 0x7F << 16));
        Assert.Equal(new KeyboardEvent(KeyboardEventKind.Controller, 0, -8192, ControllerKind.PitchBend), MidiKeyboard.Decode(0xE0));
        Assert.Null(MidiKeyboard.Decode(0xB0 | 7 << 8 | 100 << 16)); // volume: not used
        Assert.Null(MidiKeyboard.Decode(0xF8)); // clock
    }

    [Fact]
    public void A_scheduled_note_starts_on_its_frame_only_while_playing()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127);
        var player = new KeysoundPlayer(Instrument(region, [.. Enumerable.Repeat(1f, 20_000)]));
        player.SetNotes([new PlayedNote(10, 50, 60, 127)]);

        Assert.All(Render(player, 1000, playing: false), s => Assert.Equal(0, s)); // stopped: not triggered
        var block = Render(player, 1000, start: 0);
        int first = Array.FindIndex(block, s => s != 0) / 2;
        Assert.Equal((int)Math.Round(10 / MsPerFrame), first);
        Assert.Equal(1, player.ActiveVoices);

        Render(player, 1000, start: 1000 * MsPerFrame); // the next block doesn't start it again
        Assert.Equal(1, player.ActiveVoices);
    }

    [Fact]
    public void Release_all_fades_every_sounding_note()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127) { Release = 0.005 };
        var player = new KeysoundPlayer(Instrument(region, [.. Enumerable.Repeat(1f, 200_000)]));
        player.SetNotes([new PlayedNote(0, 5000, 60, 100), new PlayedNote(5, 5000, 64, 100)]);
        Render(player, 1000, start: 0);
        Assert.Equal(2, player.ActiveVoices);

        player.ReleaseAll();
        Render(player, 1000, playing: false); // 23 ms: past the 5 ms release
        Assert.Equal(0, player.ActiveVoices);
    }

    [Fact]
    public void The_mixer_plays_the_live_part_with_the_song_and_releases_it_on_pause()
    {
        var region = new SfzRegion("r", 0, 127, 60, 1, 127);
        var player = new KeysoundPlayer(Instrument(region, [.. Enumerable.Repeat(0.5f, 400_000)]));
        player.SetNotes([new PlayedNote(0, 5000, 60, 127)]);
        var mixer = new PlaybackMixer { Length = 10_000 };
        mixer.SetLiveParts([player]);
        mixer.Play();
        var block = new float[882];
        mixer.Read(block);
        Assert.Contains(block, s => s != 0);

        mixer.Pause();
        for (int i = 0; i < 200; i++) mixer.Read(block); // 2 s: well past the 0.3 s release
        Assert.Equal(0, player.ActiveVoices);
        Assert.All(block, s => Assert.Equal(0, s));
    }

    [Fact]
    public void The_bundled_piano_decodes_and_every_loop_fits_its_sample()
    {
        ManagedBass.Bass.Init(0); // the "no sound" device: decoding only (already initialised is fine)
        var piano = SfzInstrument.Load(PianoSfz);
        long bytes = 0;
        foreach (var region in piano.Regions)
        {
            var data = piano.Sample(region);
            bytes += data.Length * sizeof(float);
            if (region.HasLoop) Assert.True(region.LoopEnd < data.Length / 2, $"{region.Sample} loops past its end");
        }

        Assert.InRange(bytes, 1, 150L * 1024 * 1024); // what a session keeps in memory
        var player = new KeysoundPlayer(piano);
        player.Audition(60, 100);
        Assert.Contains(Render(player, 4410), s => Math.Abs(s) > 0.001f);
    }
}
