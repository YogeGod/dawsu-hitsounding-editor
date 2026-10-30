using System.Diagnostics;
using System.Runtime.InteropServices;
using ManagedBass;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>
/// Plays a decompiled map through BASS: the song as a stream and every lane as a sample, started by a 1 ms scheduler
/// thread when the playhead passes its cue (the way osu! triggers hitsounds). Times are milliseconds on the
/// timeline. Lanes with no playable sound (missing files, skin sounds that couldn't be found) stay silent.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly Dictionary<Lane, int> samples = [];
    private readonly HashSet<Lane> silent = [];
    private readonly object gate = new();
    private int song;
    private volatile HitScheduler? scheduler; // swapped whole by SetCues while playing; the thread reads it each loop
    private Thread? thread;
    private volatile bool playing;

    // BASS reports the position in steps of its update period, so between steps a stopwatch carries the clock on.
    private readonly Stopwatch sinceStart = new();
    private double startPosition;

    private AudioEngine()
    {
    }

    /// <summary>Opens the default output device. Throws when BASS can't start.</summary>
    public static AudioEngine Create()
    {
        Bass.Configure(Configuration.UpdatePeriod, 5);
        Bass.Configure(Configuration.PlaybackBufferLength, 100);
        if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException($"Couldn't start audio output (BASS error {Bass.LastError}).");
        return new AudioEngine();
    }

    /// <summary>Mute and solo: return false for a lane that shouldn't sound. Called on the scheduler thread.</summary>
    public Func<Lane, bool> IsAudible { get; set; } = _ => true;

    /// <summary>Lanes that show notes but have nothing to play.</summary>
    public IReadOnlySet<Lane> SilentLanes => silent;

    public bool HasSong => song != 0;

    public bool IsPlaying => playing;

    /// <summary>Song length in ms, or a second past the last cue when there's no song.</summary>
    public double Length => songLength > 0 ? Math.Max(songLength + Math.Max(songOffset, 0), cueLength) : cueLength;

    private double songLength, cueLength;

    /// <summary>When set, playback jumps back to Start on reaching End.</summary>
    public (double Start, double End)? Loop { get; set; }

    // Song settings. Timeline time = song time + offset; the clock runs at `speed` (tempo change without pitch change).
    private double speed = 1, songOffset;
    private float songVolume = 1;
    private bool songMuted, songStarted;

    /// <summary>Playback speed, 0.25–2 (1 = normal). Song and hitsounds stay in step; the song keeps its pitch.</summary>
    public double Speed
    {
        get => speed;
        set
        {
            lock (gate)
            {
                // Re-anchor the clock so the playhead doesn't jump when the rate changes.
                if (playing)
                {
                    startPosition = ClockNow();
                    sinceStart.Restart();
                }

                speed = Math.Clamp(value, 0.25, 2);
                if (song != 0) Bass.ChannelSetAttribute(song, ChannelAttribute.Tempo, (speed - 1) * 100);
            }
        }
    }

    /// <summary>Song offset in ms: positive plays the song later against the hitsounds. Applied from the next play or seek.</summary>
    public double SongOffset
    {
        get => songOffset;
        set
        {
            lock (gate)
            {
                songOffset = Math.Clamp(value, -10000, 10000);
                if (playing) StartSongAt(ClockNow()); // re-place the song against the hitsounds now
            }
        }
    }

    /// <summary>Song volume 0–1; hitsound volumes are unchanged.</summary>
    public float SongVolume
    {
        get => songVolume;
        set
        {
            songVolume = Math.Clamp(value, 0, 1);
            ApplySongVolume();
        }
    }

    /// <summary>Silences the song without changing its volume (also used while lanes are soloed).</summary>
    public bool SongMuted
    {
        get => songMuted;
        set
        {
            if (songMuted == value) return;
            songMuted = value;
            ApplySongVolume();
        }
    }

    private void ApplySongVolume()
    {
        if (song != 0) Bass.ChannelSetAttribute(song, ChannelAttribute.Volume, songMuted ? 0 : songVolume);
    }

    // Must be called holding the gate.
    private double ClockNow() => startPosition + sinceStart.Elapsed.TotalMilliseconds * speed;

    /// <summary>Puts the song where the timeline is: playing from the right place, or waiting for its offset to come round.</summary>
    private void StartSongAt(double timeline)
    {
        if (song == 0) return;
        double songTime = timeline - songOffset;
        songStarted = songTime >= 0;
        if (songStarted)
        {
            Bass.ChannelSetPosition(song, Bass.ChannelSeconds2Bytes(song, songTime / 1000));
            Bass.ChannelPlay(song);
        }
        else
        {
            Bass.ChannelPause(song);
        }
    }

    /// <summary>Loads a map's song and samples, replacing what was loaded. Stops playback.</summary>
    public void Load(DecompiledMap map, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        Pause();
        Unload();

        if (map.Settings.AudioFilename is { Length: > 0 } audio && folder.Find(audio) is { } songFile)
        {
            // A decoding stream wrapped in a tempo stream (BASS_FX), so playback speed changes keep the pitch.
            var bytes = folder.ReadBytes(songFile);
            int decoder = Bass.CreateStream(bytes, 0, bytes.Length, BassFlags.Prescan | BassFlags.Decode);
            song = decoder == 0 ? 0 : ManagedBass.Fx.BassFx.TempoCreate(decoder, BassFlags.FxFreeSource);
            if (song == 0 && decoder != 0) Bass.StreamFree(decoder);
            if (song != 0) Bass.ChannelSetAttribute(song, ChannelAttribute.Tempo, (speed - 1) * 100);
            ApplySongVolume();
        }

        foreach (var lane in map.Lanes)
        {
            byte[]? data = lane.Missing ? null : lane.Source switch
            {
                LaneSource.Skin => skin.GetValueOrDefault(lane.Name),
                _ => folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null,
            };

            int handle = data is { Length: > 0 } ? Bass.SampleLoad(data, 0, data.Length, 16, BassFlags.SampleOverrideLongestPlaying) : 0;
            if (handle != 0) samples[lane] = handle;
            else silent.Add(lane);
        }

        scheduler = new HitScheduler(map);
        double afterLastCue = scheduler.Cues.Count > 0 ? scheduler.Cues[^1].Time + 1000 : 0;
        songLength = song != 0 ? Bass.ChannelBytes2Seconds(song, Bass.ChannelGetLength(song)) * 1000 : 0;
        cueLength = afterLastCue;
        lock (gate) startPosition = 0;
    }

    /// <summary>The playhead in ms.</summary>
    public double Position
    {
        get
        {
            lock (gate)
                return playing ? ClockNow() : startPosition;
        }
    }

    public void Play()
    {
        if (playing || scheduler is null) return;
        lock (gate)
        {
            if (startPosition >= Length) startPosition = 0;
            StartSongAt(startPosition);
            sinceStart.Restart();
            playing = true;
        }

        thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "Hitsound scheduler" };
        thread.Start();
    }

    /// <summary>Stops where the playhead is.</summary>
    public void Pause()
    {
        if (!playing) return;
        double at = Position;
        playing = false;
        if (thread != Thread.CurrentThread) thread?.Join();
        thread = null;
        if (song != 0) Bass.ChannelPause(song);
        lock (gate)
        {
            startPosition = at;
            sinceStart.Reset();
        }
    }

    /// <summary>Stops and returns to <paramref name="time"/> (default: the start).</summary>
    public void Stop(double time = 0)
    {
        Pause();
        Seek(time);
    }

    public void Seek(double time)
    {
        bool wasPlaying = playing;
        Pause();
        lock (gate) startPosition = Math.Clamp(time, 0, Math.Max(Length, 0));
        if (wasPlaying) Play();
    }

    /// <summary>Replaces what plays (after an edit), keeping the song and samples. Safe while playing.</summary>
    public void SetCues(IEnumerable<Cue> cues)
    {
        if (scheduler is null) return; // nothing loaded
        scheduler = new HitScheduler(cues);
    }

    /// <summary>Plays one lane's sample now, e.g. when its name is clicked.</summary>
    public void Preview(Lane lane, float volume = 1) => Trigger(lane, volume);

    private void Run()
    {
        TimeBeginPeriod(1);
        try
        {
            // Just before the start, so a cue exactly at the start position plays too.
            double last = Math.BitDecrement(Position);
            while (playing)
            {
                double now = SyncedPosition();
                if (Loop is { } loop && LoopJump(loop, now) is { } target)
                {
                    Jump(target);
                    last = target - 0.001;
                    continue;
                }

                foreach (var cue in scheduler!.Between(last, now))
                    if (IsAudible(cue.Lane)) Trigger(cue.Lane, cue.Volume);

                last = now;
                if (now >= Length)
                {
                    lock (gate)
                    {
                        playing = false;
                        startPosition = Length;
                        sinceStart.Reset();
                    }

                    if (song != 0) Bass.ChannelPause(song);
                    break;
                }

                Thread.Sleep(1);
            }
        }
        finally
        {
            TimeEndPeriod(1);
        }
    }

    /// <summary>
    /// Where playback jumps to at <paramref name="now"/>, or null to carry on. Start before End repeats the range;
    /// End before Start skips the stretch between them (a reversed cycle, as in dawsu).
    /// </summary>
    public static double? LoopJump((double Start, double End) loop, double now) =>
        loop.End > loop.Start && now >= loop.End ? loop.Start
        : loop.Start > loop.End && now >= loop.End && now < loop.Start ? loop.Start
        : null;

    /// <summary>Re-anchors the clock to the song's reported position when they drift apart by more than 15 ms.</summary>
    private double SyncedPosition()
    {
        lock (gate)
        {
            double clock = ClockNow();
            if (song == 0) return clock;
            if (!songStarted)
            {
                // A positive offset: the song comes in once the timeline reaches it.
                if (clock >= songOffset) StartSongAt(clock);
                return clock;
            }

            // A tempo stream reports its position in the source, i.e. song time.
            double reported = Bass.ChannelBytes2Seconds(song, Bass.ChannelGetPosition(song)) * 1000 + songOffset;
            if (Math.Abs(reported - clock) <= 15 * speed) return clock;

            startPosition = reported;
            sinceStart.Restart();
            return reported;
        }
    }

    private void Jump(double time)
    {
        lock (gate)
        {
            StartSongAt(time);
            startPosition = time;
            sinceStart.Restart();
        }
    }

    private void Trigger(Lane lane, float volume)
    {
        if (!samples.TryGetValue(lane, out int sample)) return;
        int channel = Bass.SampleGetChannel(sample);
        if (channel == 0) return;
        Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume);
        Bass.ChannelPlay(channel);
    }

    private void Unload()
    {
        foreach (var handle in samples.Values) Bass.SampleFree(handle);
        samples.Clear();
        silent.Clear();
        if (song != 0) Bass.StreamFree(song);
        song = 0;
        scheduler = null;
        songLength = cueLength = 0;
    }

    public void Dispose()
    {
        Pause();
        Unload();
        Bass.Free();
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
