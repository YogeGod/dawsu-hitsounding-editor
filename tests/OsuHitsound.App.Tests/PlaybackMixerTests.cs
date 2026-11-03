using System.Diagnostics;
using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class PlaybackMixerTests
{
    private const int Rate = PlaybackMixer.Rate;
    private static readonly Lane Clap = new(LaneSource.Skin, "soft-hitclap");

    /// <summary>A song of constant 0.5 that remembers where it was sought to.</summary>
    private sealed class FlatSong(double lengthMs) : ISongSource
    {
        public List<double> Seeks { get; } = [];
        public double LengthMs => lengthMs;
        public void Seek(double songMs) => Seeks.Add(songMs);
        public void SetSpeed(double speed) { }

        public int Read(Span<float> stereo)
        {
            stereo.Fill(0.5f);
            return stereo.Length / 2;
        }
    }

    // One frame of full scale, then silence: where it lands is where the hit starts.
    private static readonly float[] Click = [1, 1, 0, 0, 0, 0];

    private static PlaybackMixer Mixer(params Cue[] cues)
    {
        var mixer = new PlaybackMixer { Length = 10000 };
        mixer.SetSound(Clap, Click);
        mixer.SetCues(new HitScheduler(cues));
        return mixer;
    }

    // Renders `ms` of playback in 10 ms blocks (as an output would pull it) and returns the left channel.
    private static float[] Render(PlaybackMixer mixer, double ms)
    {
        int frames = (int)(ms * Rate / 1000);
        var left = new float[frames];
        var block = new float[441 * 2];
        for (int done = 0; done < frames; done += 441)
        {
            mixer.Read(block);
            for (int i = 0; i < 441 && done + i < frames; i++) left[done + i] = block[i * 2];
        }

        return left;
    }

    private static int[] Clicks(float[] left) => [.. left.Select((v, i) => (v, i)).Where(x => x.v > 0.9f).Select(x => x.i)];

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void Slider_samples_loop_until_the_segment_end_at_each_speed(double speed)
    {
        var mixer = Mixer(new Cue(20, Clap, 0.5f, 60));
        mixer.SetSound(Clap, [0.5f, 0.5f]); // one-frame loop, longer than the sample
        mixer.Speed = speed;
        mixer.Play();
        var left = Render(mixer, 150);
        int from = (int)Math.Round(20 / speed * Rate / 1000), to = (int)Math.Ceiling(60 / speed * Rate / 1000);

        Assert.All(left[..from], v => Assert.Equal(0, v));
        Assert.All(left[from..to], v => Assert.Equal(0.25f, v));
        Assert.All(left[to..], v => Assert.Equal(0, v));
    }

    [Fact]
    public void Slider_samples_resume_after_seeking_but_stop_on_pause_and_respect_mutes()
    {
        var mixer = Mixer(new Cue(0, Clap, 1, 100));
        mixer.SetSound(Clap, [0.5f, 0.5f]);
        mixer.Seek(50);
        mixer.Play();
        Assert.All(Render(mixer, 10), v => Assert.Equal(0.5f, v));
        mixer.Pause();
        Assert.All(Render(mixer, 10), v => Assert.Equal(0, v));
        mixer.Play();
        mixer.IsAudible = _ => false;
        Assert.All(Render(mixer, 10), v => Assert.Equal(0, v));
        mixer.IsAudible = _ => true;
        Assert.All(Render(mixer, 10), v => Assert.Equal(0.5f, v));
    }

    [Fact]
    public void Slider_sample_changes_replace_the_loop_and_cycles_restart_it()
    {
        var second = new Lane(LaneSource.BeatmapFile, "soft-sliderslide100.wav");
        var mixer = Mixer(new Cue(0, Clap, 1, 50), new Cue(50, second, 1, 100));
        mixer.SetSound(Clap, [0.25f, 0.25f]);
        mixer.SetSound(second, [0.5f, 0.5f]);
        mixer.Loop = (0, 100);
        mixer.Play();
        var left = Render(mixer, 200);

        Assert.All(left[..2205], v => Assert.Equal(0.25f, v));
        Assert.All(left[2205..4410], v => Assert.Equal(0.5f, v));
        Assert.Equal(left[..4410], left[4410..]);
    }

    [Fact]
    public void Slider_loops_keep_sample_phase_across_audio_blocks_and_stop_when_cues_are_replaced()
    {
        var mixer = Mixer(new Cue(0, Clap, 1, 100));
        mixer.Play();
        var block = new float[1000]; // 500 frames, not a multiple of the three-frame sample
        mixer.Read(block);
        mixer.Read(block);
        Assert.Equal(0, block[0]);
        Assert.Equal(1, block[2]);
        Assert.Equal(0, block[4]);

        mixer.SetCues(new HitScheduler([]));
        mixer.Read(block);
        Assert.All(block, v => Assert.Equal(0, v));
    }

    [Theory]
    [InlineData(-1e20)]
    [InlineData(double.NegativeInfinity)]
    public void Extreme_loop_start_times_cannot_overflow_a_sample_index(double start)
    {
        var mixer = Mixer(new Cue(start, Clap, 1, 100));
        mixer.Play();
        Assert.All(Render(mixer, 10), v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void A_hit_starts_on_its_exact_sample()
    {
        var mixer = Mixer(new Cue(100, Clap, 1), new Cue(123.4, Clap, 1));
        mixer.Play();

        // 100 ms is frame 4410 exactly; 123.4 ms rounds to frame 5442 (a block boundary falls in between).
        Assert.Equal([4410, 5442], Clicks(Render(mixer, 300)));
    }

    [Fact]
    public void At_double_speed_hits_come_twice_as_soon()
    {
        var mixer = Mixer(new Cue(100, Clap, 1));
        mixer.Speed = 2;
        mixer.Play();

        Assert.Equal([2205], Clicks(Render(mixer, 300)));
    }

    [Fact]
    public void A_cue_right_at_the_start_plays_and_a_muted_lane_does_not()
    {
        var mixer = Mixer(new Cue(0, Clap, 1));
        mixer.Play();
        Assert.Equal([0], Clicks(Render(mixer, 50)));

        var muted = Mixer(new Cue(0, Clap, 1));
        muted.IsAudible = _ => false;
        muted.Play();
        Assert.Empty(Clicks(Render(muted, 50)));
    }

    [Fact]
    public void The_song_comes_in_at_its_offset_and_at_its_volume()
    {
        var mixer = Mixer();
        var song = new FlatSong(5000);
        mixer.SetSong(song);
        mixer.SongOffset = 50; // the song starts 50 ms into the timeline
        mixer.SongVolume = 0.5f;
        mixer.Play();

        var left = Render(mixer, 100);
        Assert.Equal(0, left[2204]);
        Assert.Equal(0.25f, left[2205]); // 0.5 × 0.5 from frame 2205 (50 ms) on
        Assert.Equal([0.0], song.Seeks);
    }

    [Fact]
    public void Volumes_scale_the_hit_and_the_master_volume_scales_everything()
    {
        var mixer = Mixer(new Cue(0, Clap, 0.5f));
        mixer.MasterVolume = 0.5f;
        mixer.Play();

        Assert.Equal(0.25f, Render(mixer, 20)[0]);
    }

    [Fact]
    public void A_cycle_repeats_its_range_with_its_hits()
    {
        var mixer = Mixer(new Cue(50, Clap, 1));
        mixer.Loop = (0, 100);
        mixer.Play();

        // 50 ms, then again 100 ms later after the jump back, and again.
        Assert.Equal([2205, 6615, 11025], Clicks(Render(mixer, 300)));
    }

    [Fact]
    public void Previews_play_while_stopped()
    {
        var mixer = Mixer();
        mixer.Preview(Clap, 1);

        Assert.Equal([0], Clicks(Render(mixer, 20)));
    }

    [Fact]
    public void The_playhead_is_what_is_heard_the_rendered_audio_less_the_latency()
    {
        long now = 0, tenMs = Stopwatch.Frequency / 100;
        var mixer = Mixer();
        mixer.Now = () => now;
        mixer.LatencyMs = () => 20;
        mixer.Play();

        // A device pulls a 10 ms block every 10 ms: 100 ms rendered by now, 20 ms of it still in the output's buffer.
        var block = new float[441 * 2];
        for (int i = 0; i < 10; i++)
        {
            now += tenMs;
            mixer.Read(block);
        }

        Assert.Equal(80, mixer.Position, 3);

        now += tenMs; // 10 ms later, before the next block
        Assert.Equal(90, mixer.Position, 3);

        mixer.Pause();
        Assert.Equal(90, mixer.Position, 3); // stops where it was heard
    }

    [Fact]
    public void A_driver_that_pulls_faster_than_real_time_doesnt_run_the_playhead_ahead()
    {
        long now = 0;
        var mixer = Mixer();
        mixer.Now = () => now;
        mixer.Play();

        Render(mixer, 500); // half a second rendered at once (a virtual driver, or the start-up fill)
        now += Stopwatch.Frequency / 10; // only 100 ms of real time

        Assert.Equal(100, mixer.Position, 3);
    }

    [Fact]
    public void Playback_stops_at_the_end()
    {
        var mixer = Mixer();
        mixer.Length = 50;
        mixer.Play();
        Render(mixer, 100);

        Assert.False(mixer.IsPlaying);
        Assert.Equal(50, mixer.Position, 0);
    }
}
