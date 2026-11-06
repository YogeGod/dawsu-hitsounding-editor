using OsuHitsound.Audio;

namespace OsuHitsound.App.Tests;

/// <summary>The out-of-process VST3 client against tools/FakeVstHost, which speaks the host protocol (tone, crash, slow, fail).</summary>
public class IsolatedVst3Tests
{
    private const double MsPerFrame = 1000.0 / PlaybackMixer.Rate;

    private static string FakeHost()
    {
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var exe = TestPaths.RepoFile("tools", "FakeVstHost", "bin", config, "net10.0", "FakeVstHost.exe");
        Assert.True(File.Exists(exe), $"Build tools/FakeVstHost first ({exe}).");
        return exe;
    }

    private static Task<IsolatedVst3> Start(string mode) => IsolatedVst3.StartAsync(Path.Combine(Path.GetTempPath(), $"{mode}.vst3"), hostPath: FakeHost(), timeout: TimeSpan.FromSeconds(30));

    private static float[] Render(ILivePart part, int frames = 441, double start = 0, bool playing = true)
    {
        var block = new float[frames * 2];
        part.Render(block, frames, start, MsPerFrame, playing);
        return block;
    }

    [Fact]
    public async Task Scheduled_and_live_notes_reach_the_instrument_and_stop_releases_them()
    {
        using var vst = await Start("tone");
        Assert.Equal("Fake tone", vst.Name);
        vst.SetEvents([new TimedMidi(0, 0x90, 60, 100), new TimedMidi(5000, 0x80, 60, 0)]);
        Assert.Equal(0.25f, Render(vst)[100]);

        vst.ReleaseAll(); // playback stopped: the sounding note gets its note-off
        Assert.Equal(0f, Render(vst, playing: false)[100]);

        vst.Live(0x90, 64, 90);
        Assert.Equal(0.25f, Render(vst, playing: false)[100]);
        vst.Live(0x80, 64, 0);
        Assert.Equal(0f, Render(vst, playing: false)[100]);
        Assert.Equal(0, vst.LateBlocks);
    }

    [Fact]
    public async Task State_goes_to_the_host_and_back()
    {
        using var vst = await Start("tone");
        await vst.SetStateAsync("AXNhdmVk");
        Assert.Equal("AXNhdmVk", await vst.GetStateAsync());
        await vst.ShowEditorAsync();
    }

    [Fact]
    public async Task A_crashing_instrument_goes_silent_without_taking_the_caller_down()
    {
        using var vst = await Start("crash");
        var crashed = new TaskCompletionSource();
        vst.Crashed += () => crashed.TrySetResult();
        vst.SetEvents([new TimedMidi(0, 0x90, 60, 100)]);
        Assert.All(Render(vst), v => Assert.Equal(0, v));
        await crashed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vst.IsCrashed);
        Assert.All(Render(vst), v => Assert.Equal(0, v)); // still no exception, still silent
        await Assert.ThrowsAsync<InvalidOperationException>(vst.GetStateAsync);
    }

    [Fact]
    public async Task A_slow_instrument_gives_silent_blocks_instead_of_stalling()
    {
        using var vst = await Start("slow");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) Assert.All(Render(vst), v => Assert.Equal(0, v));
        Assert.True(watch.ElapsedMilliseconds < 250, $"rendering waited {watch.ElapsedMilliseconds} ms");
        Assert.True(vst.LateBlocks >= 5);
    }

    [Fact]
    public async Task An_instrument_that_fails_to_load_reports_why()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Start("fail"));
        Assert.Contains("refuses to load", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => IsolatedVst3.StartAsync(Path.Combine(Path.GetTempPath(), "x.vst3"), hostPath: Path.Combine(Path.GetTempPath(), "no-such-host.exe")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Common Files\VST3\Synth.vst3", true)]
    [InlineData(@"\\server\share\Synth.vst3", false)] // a project can't make dawsu! run a plug-in from a network share
    [InlineData("//server/share/Synth.vst3", false)]
    [InlineData(@"Synth.vst3", false)]
    [InlineData(@"C:\Temp\evil.dll", false)]
    public async Task Only_a_local_vst3_is_started(string path, bool loadable)
    {
        Assert.Equal(loadable, IsolatedVst3.IsLoadablePath(path));
        if (!loadable) await Assert.ThrowsAsync<InvalidOperationException>(() => IsolatedVst3.StartAsync(path, hostPath: FakeHost()));
    }

    [Fact]
    public async Task Events_of_blocks_skipped_while_late_are_sent_with_the_next_block()
    {
        using var vst = await Start("tone");
        vst.SetEvents([new TimedMidi(0, 0x90, 60, 100)]);
        Render(vst);                                   // note on
        vst.SetEvents([new TimedMidi(20, 0x80, 60, 0)]);
        // Pretend the host is late for the block holding the note-off: the client skips it but keeps its events.
        typeof(IsolatedVst3).GetField("waiting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vst, true);
        Render(vst, start: 15);                        // skipped (done not signalled): its note-off at 20 ms is carried
        typeof(IsolatedVst3).GetField("waiting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vst, false);
        Assert.Equal(0f, Render(vst, start: 25)[100]); // sent with this block: no hanging note
    }

    [Fact]
    public async Task A_late_reply_never_answers_another_command()
    {
        using var vst = await Start("tone");
        // Commands answered out of order still reach their own callers (replies carry the request's number).
        var a = vst.GetStateAsync();
        var b = vst.SetStateAsync("AXg=");
        var c = vst.GetStateAsync();
        await Task.WhenAll(a, b, c);
        Assert.Equal("AXg=", await c);
    }

    [Fact]
    public async Task The_mixer_plays_several_instrument_parts_together()
    {
        using var a = await Start("tone");
        using var b = await Start("tone");
        a.Live(0x90, 60, 100);
        b.Live(0x90, 62, 100);
        var mixer = new PlaybackMixer { Length = 10_000 };
        mixer.SetLiveParts([a, b]);
        var block = new float[882];
        mixer.Read(block);
        Assert.Equal(0.5f, block[200], 4);
    }
}
