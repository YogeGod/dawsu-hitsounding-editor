using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App.Tests;

public class KeysoundVstTrackTests
{
    [Fact]
    public void A_track_becomes_note_controller_and_bend_messages_in_timeline_ms()
    {
        var tempo = new TempoMap([(1000, 500, 4)]); // 960 ticks a beat from 1000 ms
        var track = KeyTrack.Create("Synth") with
        {
            Notes = [new KeyNote(Guid.NewGuid(), 960, 480, 64, 0)], // velocity 0 would be a note-off: sent as 1
            Controllers =
            [
                new KeyController(0, ControllerKind.Sustain, 127),
                new KeyController(960, ControllerKind.Modulation, 40),
                new KeyController(1920, ControllerKind.PitchBend, -8192),
                new KeyController(1920, ControllerKind.PitchBend, 8191),
            ],
        };

        var events = KeysoundMidi.Events(track, tempo, out double last).OrderBy(e => e.Time).ThenBy(e => e.Status).ToList();
        Assert.Equal(
        [
            new TimedMidi(1000, 0xB0, 64, 127),
            new TimedMidi(1500, 0x90, 64, 1),
            new TimedMidi(1500, 0xB0, 1, 40),
            new TimedMidi(1750, 0x80, 64, 0),
            new TimedMidi(2000, 0xE0, 0, 0),
            new TimedMidi(2000, 0xE0, 0x7F, 0x7F),
        ], events);
        Assert.Equal(2000, last);
    }

    [Fact]
    public void Only_installed_or_chosen_plug_ins_start_by_themselves()
    {
        Assert.True(Vst3Picker.IsTrusted(Path.Combine(Vst3Picker.Root, "Vendor", "Synth.vst3")));
        Assert.False(Vst3Picker.IsTrusted(Path.Combine(Vst3Picker.Root + "Evil", "Synth.vst3"))); // a look-alike folder
        Assert.False(Vst3Picker.IsTrusted(Path.Combine(Vst3Picker.Root, "..", "Synth.vst3")));
        string downloaded = Path.Combine(Path.GetTempPath(), $"dawsu-trust-{Guid.NewGuid():N}", "Synth.vst3");
        Assert.False(Vst3Picker.IsTrusted(downloaded));
        Vst3Picker.Trust(downloaded); // chosen in the instrument picker, or approved
        Assert.True(Vst3Picker.IsTrusted(downloaded.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(@"C:\VST3\Serum2.vst3", false)]
    [InlineData(@"C:\VST3\Vendor\Synth.vst3", false)]
    [InlineData(@"C:\VST3\Serum2.vst3\Contents\x86_64-win\Serum2.vst3", true)] // the bundle's own binary
    public void The_installed_list_shows_a_bundle_once(string path, bool inside) =>
        Assert.Equal(inside, Vst3Picker.InsideBundle(@"C:\VST3", path));
}
