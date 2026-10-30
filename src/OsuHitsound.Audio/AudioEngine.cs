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
    private HitScheduler? scheduler;
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
    public double Length { get; private set; }

    /// <summary>When set, playback jumps back to Start on reaching End.</summary>
    public (double Start, double End)? Loop { get; set; }

    /// <summary>Loads a map's song and samples, replacing what was loaded. Stops playback.</summary>
    public void Load(DecompiledMap map, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        Pause();
        Unload();

        if (map.Settings.AudioFilename is { Length: > 0 } audio && folder.Find(audio) is { } songFile)
        {
            var bytes = folder.ReadBytes(songFile);
            song = Bass.CreateStream(bytes, 0, bytes.Length, BassFlags.Prescan);
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
        Length = song != 0 ? Bass.ChannelBytes2Seconds(song, Bass.ChannelGetLength(song)) * 1000 : afterLastCue;
        lock (gate) startPosition = 0;
    }

    /// <summary>The playhead in ms.</summary>
    public double Position
    {
        get
        {
            lock (gate)
                return playing ? startPosition + sinceStart.Elapsed.TotalMilliseconds : startPosition;
        }
    }

    public void Play()
    {
        if (playing || scheduler is null) return;
        lock (gate)
        {
            if (startPosition >= Length) startPosition = 0;
            if (song != 0)
            {
                Bass.ChannelSetPosition(song, Bass.ChannelSeconds2Bytes(song, startPosition / 1000));
                Bass.ChannelPlay(song);
            }

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
                if (Loop is { } loop && loop.End > loop.Start && now >= loop.End)
                {
                    Jump(loop.Start);
                    last = loop.Start - 0.001;
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

    /// <summary>Re-anchors the clock to the song's reported position when they drift apart by more than 15 ms.</summary>
    private double SyncedPosition()
    {
        lock (gate)
        {
            double clock = startPosition + sinceStart.Elapsed.TotalMilliseconds;
            if (song == 0) return clock;

            double reported = Bass.ChannelBytes2Seconds(song, Bass.ChannelGetPosition(song)) * 1000;
            if (Math.Abs(reported - clock) <= 15) return clock;

            startPosition = reported;
            sinceStart.Restart();
            return reported;
        }
    }

    private void Jump(double time)
    {
        lock (gate)
        {
            if (song != 0) Bass.ChannelSetPosition(song, Bass.ChannelSeconds2Bytes(song, Math.Max(time, 0) / 1000));
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
        Length = 0;
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
