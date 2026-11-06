using OsuHitsound.Audio;
using OsuHitsound.Lanes;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

namespace OsuHitsound.App.Tests;

public class LiveDrumTests
{
    private static readonly Lane Kick = new(LaneSource.Skin, "normal-hitnormal");
    private static readonly Lane Other = new(LaneSource.BeatmapFile, "other.wav");
    private sealed class Instrument : IDrumInstrument
    {
        public List<(long Frame, MidiEvent Midi)> Events = [];
        public List<int> Blocks = [];
        public long Frame;
        public bool Fail;
        public bool Process(float[] stereo, int frames, MidiEvent[] events, int count)
        {
            Blocks.Add(frames); Array.Clear(stereo);
            foreach (var item in events.Take(count))
            {
                Events.Add((Frame + item.Offset, item));
                if (item.Status == 0x99) stereo[item.Offset * 2] = stereo[item.Offset * 2 + 1] = item.Data2 / 127f;
            }
            Frame += frames;
            return !Fail;
        }
    }
    private static PlaybackMixer Mixer(Instrument instrument, params Cue[] cues)
    {
        var mixer = new PlaybackMixer { Length = 1000 };
        mixer.SetSound(Kick, [0.9f, 0.9f]);
        mixer.SetSound(Other, [0.2f, 0.2f]);
        mixer.SetCues(new(cues));
        mixer.SetDrumInstrument(instrument, new Dictionary<Lane, int> { [Kick] = 36 });
        return mixer;
    }
    [Fact]
    public void Mapped_hit_replaces_sample_and_uses_sample_offset_and_velocity()
    {
        var instrument = new Instrument();
        var mixer = Mixer(instrument, new Cue(1000.0 * 100 / 44100, Kick, 0.5f), new Cue(0, Other, 1));
        var audio = new float[2048]; mixer.Play(); mixer.Read(audio);
        Assert.Equal(0.2f, audio[0]);
        Assert.Equal(64 / 127f, audio[200]);
        var hit = Assert.Single(instrument.Events, e => e.Midi.Status == 0x99);
        Assert.Equal(100, hit.Frame); Assert.Equal(36, hit.Midi.Data1); Assert.Equal(64, hit.Midi.Data2);
        Assert.All(instrument.Blocks, n => Assert.InRange(n, 1, 512));
    }
    [Fact]
    public void Exact_block_boundary_is_scheduled_once_at_the_next_block()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument, new Cue(512 * 1000.0 / 44100, Kick, 1));
        mixer.Play(); mixer.Read(new float[2048]);
        var hit = Assert.Single(instrument.Events, e => e.Midi.Status == 0x99);
        Assert.Equal(512, hit.Frame);
    }
    [Fact]
    public void Mute_and_solo_filter_live_timeline_and_lane_audition()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument, new Cue(0, Kick, 1));
        mixer.IsAudible = _ => false; mixer.Preview(Kick, 1); mixer.Play(); mixer.Read(new float[1024]);
        Assert.DoesNotContain(instrument.Events, e => e.Midi.Status == 0x99);
        mixer.IsAudible = _ => true; mixer.Preview(Kick, 0.25f); mixer.Read(new float[1024]);
        Assert.Equal(32, Assert.Single(instrument.Events, e => e.Midi.Status == 0x99).Midi.Data2);
    }
    [Fact]
    public void Seek_and_pause_send_all_sound_off_and_clear_queued_previews()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument);
        mixer.Preview(Kick, 1); mixer.Seek(10); mixer.Read(new float[1024]);
        Assert.DoesNotContain(instrument.Events, e => e.Midi.Status == 0x99);
        instrument.Events.Clear(); mixer.Play(); mixer.Read(new float[1024]); mixer.Pause(); mixer.Read(new float[1024]);
        Assert.Contains(instrument.Events, e => e.Midi.Status == 0xb9 && e.Midi.Data1 == 120);
    }
    [Fact]
    public void Note_off_is_sent_after_a_hundred_milliseconds_even_when_stopped()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument);
        mixer.Preview(Kick, 1); mixer.Read(new float[10000]);
        Assert.Contains(instrument.Events, e => e.Frame == 4410 && e.Midi.Status == 0x89 && e.Midi.Data1 == 36);
    }
    [Fact]
    public void Detaching_restores_sample_playback_and_stops_instrument_calls()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument);
        mixer.SetDrumInstrument(null); mixer.Preview(Kick, 1); var audio = new float[32]; mixer.Read(audio);
        Assert.Equal(0.9f, audio[0]); Assert.Empty(instrument.Blocks);
    }
    [Fact]
    public void Processing_failure_is_reported_without_crashing_the_audio_callback()
    {
        var instrument = new Instrument { Fail = true }; var mixer = Mixer(instrument);
        mixer.Read(new float[4096]); Assert.NotNull(mixer.DrumInstrumentError); Assert.Single(instrument.Blocks);
    }
    [Fact]
    public void Live_hits_follow_speed_without_changing_midi_pitch()
    {
        var instrument = new Instrument(); var mixer = Mixer(instrument, new Cue(20, Kick, 1));
        mixer.Speed = 2; mixer.Play(); mixer.Read(new float[2048]);
        var hit = Assert.Single(instrument.Events, e => e.Midi.Status == 0x99);
        Assert.Equal(441, hit.Frame); Assert.Equal(36, hit.Midi.Data1);
    }
}
