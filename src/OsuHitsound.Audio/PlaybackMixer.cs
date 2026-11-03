using System.Diagnostics;
using NAudio.Wave;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>The song as the mixer pulls it: interleaved stereo frames at <see cref="PlaybackMixer.Rate"/>, at the playback speed.</summary>
public interface ISongSource
{
    /// <summary>The song's length in ms (song time, before any speed change).</summary>
    double LengthMs { get; }

    /// <summary>Moves to a song time in ms.</summary>
    void Seek(double songMs);

    /// <summary>Playback speed (1 = normal); the pitch stays the same.</summary>
    void SetSpeed(double speed);

    /// <summary>Fills interleaved stereo frames and returns how many it wrote (fewer at the end of the song).</summary>
    int Read(Span<float> stereo);
}

/// <summary>
/// Mixes everything that plays into one stereo stream for the output device: the song, each hitsound started at its
/// exact sample, and previews. Times are milliseconds on the timeline (song time + offset). The output pulls blocks
/// with <see cref="Read"/>; the clock is the audio that has been rendered, less the output's latency, so the playhead
/// follows what is heard. Pure apart from the song source, so tests can drive it without a sound card.
/// </summary>
public sealed class PlaybackMixer : ISampleProvider
{
    public const int Rate = 44100;
    private const int MaxVoices = 256; // a dense map plus previews; the oldest is dropped past this
    private const double MsPerFrame = 1000.0 / Rate;

    private readonly object gate = new();
    private readonly Dictionary<Lane, float[]> sounds = [];
    private readonly List<Voice> voices = [];
    private float[] songBuffer = [];

    private ISongSource? song;
    private HitScheduler scheduler = new([]);
    private bool playing, songRunning;
    private double timeline, cueFrom, speed = 1, songOffset, length;
    private float songVolume = 1, masterVolume = 1;
    private bool songMuted;

    // The clock: the timeline at the end of the last block, when that block was rendered, and the last time reported.
    private double rendered;
    private long renderedAt;
    private double reported = double.NegativeInfinity;

    // Where and when playback last (re)started: the playhead can't run ahead of the real time since.
    private double startedAt;
    private long startedTicks;

    private sealed class Voice(float[] data, int frame, float gain)
    {
        public readonly float[] Data = data;
        public int Frame = frame; // negative: frames until it starts
        public readonly float Gain = gain;
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    /// <summary>How far behind the rendered audio the speakers are, in ms of real time (the output's latency).</summary>
    public Func<double> LatencyMs { get; set; } = () => 0;

    /// <summary>For tests: the time "now", in Stopwatch ticks.</summary>
    internal Func<long> Now { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Mute and solo: false for a lane that shouldn't sound. Called on the audio thread.</summary>
    public Func<Lane, bool> IsAudible { get; set; } = _ => true;

    public bool IsPlaying
    {
        get { lock (gate) return playing; }
    }

    /// <summary>When set, playback jumps back to Start on reaching End (End before Start skips the stretch between).</summary>
    public (double Start, double End)? Loop { get; set; }

    /// <summary>Where playback stops: the song's end, or past the last hitsound.</summary>
    public double Length
    {
        get { lock (gate) return length; }
        set { lock (gate) length = Math.Max(0, value); }
    }

    public void SetSong(ISongSource? source)
    {
        lock (gate)
        {
            song = source;
            song?.SetSpeed(speed);
            songRunning = false;
        }
    }

    public void SetSound(Lane lane, float[] stereo)
    {
        lock (gate) sounds[lane] = stereo;
    }

    public void ClearSounds()
    {
        lock (gate)
        {
            sounds.Clear();
            voices.Clear();
        }
    }

    public void SetCues(HitScheduler cues)
    {
        lock (gate) scheduler = cues;
    }

    public double Speed
    {
        get { lock (gate) return speed; }
        set
        {
            lock (gate)
            {
                Reanchor();
                speed = Math.Clamp(value, 0.25, 2);
                song?.SetSpeed(speed);
            }
        }
    }

    /// <summary>Song offset in ms: positive plays the song later against the hitsounds.</summary>
    public double SongOffset
    {
        get { lock (gate) return songOffset; }
        set
        {
            lock (gate)
            {
                songOffset = Math.Clamp(value, -10000, 10000);
                songRunning = false; // re-placed against the hitsounds at the next block
            }
        }
    }

    public float SongVolume
    {
        get { lock (gate) return songVolume; }
        set { lock (gate) songVolume = Math.Clamp(value, 0, 1); }
    }

    public bool SongMuted
    {
        get { lock (gate) return songMuted; }
        set { lock (gate) songMuted = value; }
    }

    /// <summary>Scales everything (song, hitsounds, previews) together.</summary>
    public float MasterVolume
    {
        get { lock (gate) return masterVolume; }
        set { lock (gate) masterVolume = Math.Clamp(value, 0, 1); }
    }

    /// <summary>The playhead in ms: what is being heard now.</summary>
    public double Position
    {
        get
        {
            lock (gate)
            {
                if (!playing) return timeline;
                double elapsed = (Now() - renderedAt) * 1000.0 / Stopwatch.Frequency;
                double heard = Math.Min(rendered, rendered - LatencyMs() * speed + elapsed * speed);
                // Never faster than real time either: some drivers (virtual ones, and any at start-up) pull ahead.
                heard = Math.Min(heard, startedAt + (Now() - startedTicks) * 1000.0 / Stopwatch.Frequency * speed);
                // Never backwards between blocks, except across a jump (which resets it).
                reported = Math.Max(reported, heard);
                return Math.Max(0, reported);
            }
        }
    }

    public void Play()
    {
        lock (gate)
        {
            if (playing) return;
            if (timeline >= length) timeline = 0;
            playing = true;
            StartAt(timeline);
        }
    }

    /// <summary>Stops where the playhead is (what is heard, not what was rendered ahead).</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (!playing) return;
            double at = Position;
            playing = false;
            timeline = Math.Min(at, length);
        }
    }

    public void Seek(double time)
    {
        lock (gate)
        {
            timeline = Math.Clamp(time, 0, Math.Max(length, 0));
            if (playing) StartAt(timeline);
        }
    }

    /// <summary>Plays a sound now (a lane's ● or a clicked hit), whether or not playback runs.</summary>
    public void Preview(Lane lane, float volume)
    {
        lock (gate)
            if (sounds.TryGetValue(lane, out var data)) AddVoice(new Voice(data, 0, volume));
    }

    // Must hold the gate. Playback (re)starts at a time: the song is placed on the next block, a cue right at the time plays.
    private void StartAt(double time)
    {
        timeline = rendered = startedAt = time;
        renderedAt = startedTicks = Now();
        reported = double.NegativeInfinity;
        cueFrom = Math.BitDecrement(time);
        songRunning = false;
    }

    // Must hold the gate: keep the clock where it is when the speed changes, so the playhead doesn't jump.
    private void Reanchor()
    {
        if (!playing) return;
        double heard = Position;
        rendered = timeline;
        renderedAt = startedTicks = Now();
        reported = startedAt = heard;
    }

    public int Read(Span<float> buffer)
    {
        lock (gate)
        {
            buffer.Clear();
            int frames = buffer.Length / 2, done = 0;
            while (playing && done < frames)
            {
                if (Loop is { } loop && AudioEngine.LoopJump(loop, timeline) is { } jumpNow)
                {
                    StartAt(jumpNow);
                    continue;
                }

                if (timeline >= length)
                {
                    playing = false;
                    break;
                }

                // The block ends early at the loop's end or the song's end; the rest follows from there.
                int n = frames - done;
                double stop = length;
                if (Loop is { } l && timeline < l.End) stop = Math.Min(stop, l.End);
                n = (int)Math.Clamp(Math.Ceiling((stop - timeline) / (MsPerFrame * speed)), 1, n);

                RenderPlaying(buffer.Slice(done * 2, n * 2), n);
                done += n;
            }

            if (done < frames) MixVoices(buffer.Slice(done * 2), frames - done); // previews while stopped

            for (int i = 0; i < buffer.Length; i++) buffer[i] = Math.Clamp(buffer[i] * masterVolume, -1f, 1f);
            rendered = timeline;
            renderedAt = Now();
            return buffer.Length;
        }
    }

    // One stretch of playback: the cues inside it start at their exact frame, the song comes in at its offset.
    private void RenderPlaying(Span<float> block, int n)
    {
        double msPerFrame = MsPerFrame * speed, start = timeline, end = start + n * msPerFrame;

        foreach (var cue in scheduler.Between(cueFrom, end))
            if (sounds.TryGetValue(cue.Lane, out var data) && IsAudible(cue.Lane))
                AddVoice(new Voice(data, -(int)Math.Round((cue.Time - start) / msPerFrame), cue.Volume));
        cueFrom = end;

        if (song is not null)
        {
            double songStart = start - songOffset;
            int from = 0;
            if (!songRunning && songStart + n * msPerFrame > 0 && songStart < song.LengthMs)
            {
                from = songStart < 0 ? (int)Math.Round(-songStart / msPerFrame) : 0;
                song.Seek(Math.Max(0, songStart));
                songRunning = true;
            }

            if (songRunning && from < n)
            {
                int want = (n - from) * 2;
                if (songBuffer.Length < want) songBuffer = new float[want];
                int got = song.Read(songBuffer.AsSpan(0, want)) * 2;
                float gain = songMuted ? 0 : songVolume;
                for (int i = 0; i < got; i++) block[from * 2 + i] += songBuffer[i] * gain;
            }
        }

        MixVoices(block, n);
        timeline = end;
    }

    private void MixVoices(Span<float> block, int n)
    {
        for (int v = voices.Count - 1; v >= 0; v--)
        {
            var voice = voices[v];
            int frames = voice.Data.Length / 2;
            for (int i = Math.Max(0, -voice.Frame); i < n; i++)
            {
                int f = voice.Frame + i;
                if (f >= frames) break;
                block[i * 2] += voice.Data[f * 2] * voice.Gain;
                block[i * 2 + 1] += voice.Data[f * 2 + 1] * voice.Gain;
            }

            voice.Frame += n;
            if (voice.Frame >= frames) voices.RemoveAt(v);
        }
    }

    private void AddVoice(Voice voice)
    {
        if (voices.Count >= MaxVoices) voices.RemoveAt(0);
        voices.Add(voice);
    }
}
