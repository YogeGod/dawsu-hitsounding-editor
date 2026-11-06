using OsuHitsound.Audio;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.App.Tests;

public class KeyNoteRenderingTests
{
    private static string FakeHost()
    {
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var exe = TestPaths.RepoFile("tools", "FakeVstHost", "bin", config, "net10.0", "FakeVstHost.exe");
        Assert.True(File.Exists(exe), $"Build tools/FakeVstHost first ({exe}).");
        return exe;
    }

    private static KeyRenderJob Job(double length, double held = -1, params KeyRenderController[] extra) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 60, 100, length, held < 0 ? length : held,
            [new(0, ControllerKind.Sustain, 0), new(0, ControllerKind.Modulation, 0), new(0, ControllerKind.PitchBend, 0), .. extra]);

    [Fact]
    public async Task An_offline_host_renders_a_note_and_its_tail()
    {
        using var vst = await IsolatedVst3.StartAsync(Path.Combine(Path.GetTempPath(), "tone.vst3"), hostPath: FakeHost(), offline: true);
        using var renderer = new VstNoteRenderer(vst);
        var audio = await renderer.RenderAsync(Job(100), new RenderLimits());
        Assert.False(audio.Truncated);
        Assert.Equal(4410 + 1000, audio.Frames); // the key for 100 ms, then the fake tail
        Assert.Equal(0.25f, audio.Stereo[2 * 100]);
        Assert.Equal(0.01f, audio.Stereo[2 * 5000]);

        var cut = await renderer.RenderAsync(Job(100), new RenderLimits(MaxTailMs: 5)); // 220 frames of tail allowed
        Assert.True(cut.Truncated);
        Assert.Equal(4410 + 220, cut.Frames);
    }

    [Fact]
    public async Task A_live_host_refuses_to_render()
    {
        using var vst = await IsolatedVst3.StartAsync(Path.Combine(Path.GetTempPath(), "tone.vst3"), hostPath: FakeHost());
        await Assert.ThrowsAsync<InvalidOperationException>(() => vst.RenderAsync([], new RenderLimits(), 0));
    }

    [Fact]
    public void A_note_becomes_controllers_first_then_the_note_and_its_later_changes()
    {
        var job = Job(500, 800, new KeyRenderController(800, ControllerKind.Sustain, 0)) with
        {
            Controllers = [new(0, ControllerKind.Sustain, 127), new(0, ControllerKind.Modulation, 10), new(0, ControllerKind.PitchBend, 0), new(800, ControllerKind.Sustain, 0)],
        };
        var (events, last) = VstNoteRenderer.Events(job);
        Assert.Equal(
        [
            (0L, (byte)0xB0, (byte)64, (byte)127), (0L, (byte)0xB0, (byte)1, (byte)10), (0L, (byte)0xE0, (byte)0, (byte)64),
            (0L, (byte)0x90, (byte)60, (byte)100), (22050L, (byte)0x80, (byte)60, (byte)0), (35280L, (byte)0xB0, (byte)64, (byte)0),
        ], events);
        Assert.Equal(35280, last); // the pedal lets go after the key
    }

    [Fact]
    public async Task The_piano_renders_each_note_alone_until_its_release_fades()
    {
        // A constant sample with a short release: the note sounds for its length plus the release, then falls quiet.
        var region = new SfzRegion("x.wav", 0, 127, 60, 1, 127) { Release = 0.05 };
        var data = Enumerable.Repeat(0.5f, 2 * PlaybackMixer.Rate).ToArray();
        using var piano = new PianoNoteRenderer(new SfzInstrument([region], _ => data));
        var a = await piano.RenderAsync(Job(100), new RenderLimits());
        var b = await piano.RenderAsync(Job(100), new RenderLimits());
        Assert.False(a.Truncated);
        Assert.Equal(a.Stereo, b.Stereo); // nothing carries over between notes
        Assert.InRange(a.Frames, 4410, 4410 + PlaybackMixer.Rate / 2); // the key, then a release well under the limit
        Assert.True(a.Stereo[2 * 2000] > 0.01f);

        var held = await piano.RenderAsync(Job(100, held: 400), new RenderLimits());
        Assert.True(held.Frames > a.Frames + 10_000); // the pedal holds it 300 ms longer
    }

    [Fact]
    public void Wav_files_are_16_bit_stereo_and_clip_at_full_scale()
    {
        var wav = KeyWav.Encode16([0.5f, -0.5f, 2f, -2f], gain: 1);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + 8, wav.Length);
        Assert.Equal((short)2, BitConverter.ToInt16(wav, 22)); // channels
        Assert.Equal(PlaybackMixer.Rate, BitConverter.ToInt32(wav, 24));
        Assert.Equal((short)16, BitConverter.ToInt16(wav, 34));
        Assert.Equal([16384, -16384, 32767, -32767], Enumerable.Range(0, 4).Select(i => (int)BitConverter.ToInt16(wav, 44 + i * 2)));
        Assert.Equal(KeyWav.Hash(wav), KeyWav.Hash(KeyWav.Encode16([0.5f, -0.5f, 2f, -2f])));
        Assert.Equal([0.75f, 0.25f, 0.5f], KeyWav.Mix([([0.5f, 0.5f, 0.5f], 1f), ([0.5f, -0.5f], 0.5f)]));
    }
}
