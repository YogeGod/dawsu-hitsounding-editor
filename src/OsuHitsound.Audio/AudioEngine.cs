using ManagedBass;
using NAudio.Wave;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>
/// Plays a decompiled map: the editor's own mixer (<see cref="PlaybackMixer"/>) puts the song and every hitsound into one
/// stream, each hit at its exact sample, and a native output (WASAPI, ASIO, DirectSound or MME, through NAudio) sends it
/// to the speakers. BASS only decodes the song and the samples. Times are milliseconds on the timeline. Lanes with no
/// playable sound (missing files, skin sounds that couldn't be found) stay silent.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly PlaybackMixer mixer;
    private readonly HashSet<Lane> loaded = [], silent = [];
    private readonly OpenOutput output;
    private readonly bool ownsBass;
    private readonly object loadGate = new();
    private BassSong? song;
    private HitScheduler? scheduler;
    private int loadToken;
    private bool disposed;

    private AudioEngine(OpenOutput output, PlaybackMixer mixer, string? fallback, bool ownsBass = true)
    {
        (this.output, this.mixer, this.ownsBass) = (output, mixer, ownsBass);
        Fallback = fallback;
        mixer.LatencyMs = output.LatencyMs;
    }

    /// <summary>An engine with no device and no BASS device init, for tests of load and publish.</summary>
    internal static AudioEngine CreateForTests() =>
        new(new OpenOutput(new SilentOutput(), static () => 0, "test", static () => { }), new PlaybackMixer(), null, ownsBass: false);

    /// <summary>Output buffer when none is chosen: small keeps the playhead close to what's heard; raise it if it crackles.</summary>
    public const int DefaultBufferMs = 30;

    /// <summary>
    /// Opens an output (<paramref name="device"/>: an id from <see cref="Devices"/>, null or "" for the default). When it
    /// can't start, WASAPI shared on the default device is used instead and <see cref="Fallback"/> says why. Throws only
    /// when no output works at all.
    /// </summary>
    public static AudioEngine Create(OutputKind kind = OutputKind.WasapiShared, string? device = null, int bufferMs = DefaultBufferMs)
    {
        // BASS decodes only: device 0 is its "no sound" device.
        if (!Bass.Init(0) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException($"Couldn't start the audio decoder (BASS error {Bass.LastError}).");

        var mixer = new PlaybackMixer();
        try
        {
            return new AudioEngine(AudioOutputs.Open(kind, device, bufferMs, mixer), mixer, null);
        }
        catch (Exception ex) when (kind != OutputKind.WasapiShared || !string.IsNullOrEmpty(device))
        {
            var reason = $"{AudioOutputs.Name(kind)} didn't start ({ex.Message}), so WASAPI (shared) on the default device is used.";
            try
            {
                return new AudioEngine(AudioOutputs.Open(OutputKind.WasapiShared, null, bufferMs, mixer), mixer, reason);
            }
            catch (Exception second)
            {
                throw new InvalidOperationException($"Couldn't start audio output: {second.Message}", second);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Couldn't start audio output: {ex.Message}", ex);
        }
    }


    /// <summary>The output devices of one kind: (id, name), the system default first.</summary>
    public static IReadOnlyList<(string Id, string Name)> Devices(OutputKind kind) => AudioOutputs.Devices(kind);

    /// <summary>What is playing the sound, e.g. "WASAPI (shared)".</summary>
    public string OutputDescription => output.Description;

    /// <summary>Why the chosen output wasn't used (and WASAPI shared was), or null.</summary>
    public string? Fallback { get; }

    /// <summary>Opens the ASIO driver's own settings window; false when the output isn't ASIO.</summary>
    public bool ShowAsioControlPanel()
    {
        if (output.Player is not AsioOut asio) return false;
        asio.ShowControlPanel();
        return true;
    }

    /// <summary>Mute and solo: return false for a lane that shouldn't sound. Called on the audio thread.</summary>
    public Func<Lane, bool> IsAudible
    {
        get => mixer.IsAudible;
        set => mixer.IsAudible = value;
    }

    /// <summary>Lanes that show notes but have nothing to play.</summary>
    public IReadOnlySet<Lane> SilentLanes => silent;

    public bool HasSong => song is not null;

    /// <summary>The loaded song file, for drawing its waveform; null without a song.</summary>
    public byte[]? SongBytes { get; private set; }

    public bool IsPlaying => mixer.IsPlaying;

    /// <summary>Song length in ms, or a second past the last cue when there's no song.</summary>
    public double Length => mixer.Length;

    /// <summary>Lets playback run at least this long (Keysound notes past the song's end, or a project whose song is missing).</summary>
    public void SetMinimumLength(double ms) => mixer.Length = Math.Max(Math.Max(songLength, cueLength), ms);

    private double songLength, cueLength;

    /// <summary>When set, playback jumps back to Start on reaching End.</summary>
    public (double Start, double End)? Loop
    {
        get => mixer.Loop;
        set => mixer.Loop = value;
    }

    /// <summary>Playback speed, 0.25–2 (1 = normal). Song and hitsounds stay in step; the song keeps its pitch.</summary>
    public double Speed
    {
        get => mixer.Speed;
        set => mixer.Speed = value;
    }

    /// <summary>Song offset in ms: positive plays the song later against the hitsounds.</summary>
    public double SongOffset
    {
        get => mixer.SongOffset;
        set
        {
            mixer.SongOffset = value;
            UpdateLength();
        }
    }

    /// <summary>The lowest global volume: below 20 % the editor gets too quiet to judge hitsounds by ear.</summary>
    public const float MinMasterVolume = 0.2f;

    /// <summary>Global volume (20–100 %): scales the song, the hitsounds and the previews together.</summary>
    public float MasterVolume
    {
        get => mixer.MasterVolume;
        set => mixer.MasterVolume = Math.Clamp(value, MinMasterVolume, 1);
    }

    /// <summary>Song volume 0–1; hitsound volumes are unchanged.</summary>
    public float SongVolume
    {
        get => mixer.SongVolume;
        set => mixer.SongVolume = value;
    }

    /// <summary>Silences the song without changing its volume (also used while lanes are soloed).</summary>
    public bool SongMuted
    {
        get => mixer.SongMuted;
        set => mixer.SongMuted = value;
    }

    /// <summary>Loads a map's song and samples, replacing what was loaded. Stops playback. Decodes on the calling thread.</summary>
    public void Load(DecompiledMap map, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        int token = BeginLoad();
        Publish(token, Prepare(map, folder, skin));
    }

    /// <summary>
    /// Reads and decodes a map's song and samples. Does not touch the mixer, so it can run off the UI thread.
    /// Publish it with <see cref="Apply"/>, or dispose it to drop the song stream.
    /// </summary>
    public static PreparedLoad Prepare(DecompiledMap map, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        byte[]? songBytes = null;
        BassSong? song = null;
        var adopted = false;
        try
        {
            if (map.Settings.AudioFilename is { Length: > 0 } audio && folder.Find(audio) is { } songFile)
            {
                songBytes = folder.ReadBytes(songFile);
                song = BassSong.Open(songBytes);
            }

            var samples = new List<(Lane Lane, float[]? Stereo)>();
            var seen = new HashSet<Lane>();
            foreach (var lane in map.Lanes)
            {
                if (!seen.Add(lane)) continue; // the first sample wins, as LoadLane ignores a lane already loaded
                byte[]? data = lane.Missing ? null : lane.Source switch
                {
                    LaneSource.Skin => skin.GetValueOrDefault(lane.Name),
                    _ => folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null,
                };
                float[]? stereo = data is { Length: > 0 } ? SampleMixer.Decode(data) : null;
                samples.Add((lane, stereo is { Length: > 0 } ? stereo : null));
            }

            var scheduler = new HitScheduler(map);
            double songLength = song?.LengthMs ?? 0;
            double cueLength = scheduler.Cues.Count > 0 ? scheduler.EndTime + 1000 : 0;
            var prepared = new PreparedLoad(song, songBytes, samples, scheduler, songLength, cueLength);
            adopted = true;
            return prepared;
        }
        finally
        {
            if (!adopted) song?.Dispose();
        }
    }

    /// <summary>Stops playback, drops what was loaded, and installs <paramref name="prepared"/>. False when the engine has been disposed.</summary>
    public bool Apply(PreparedLoad prepared) => Publish(BeginLoad(), prepared);

    /// <summary>Stops playback and drops the current song and samples. The returned token is the only one <see cref="Publish"/> will accept until the next call.</summary>
    internal int BeginLoad()
    {
        lock (loadGate)
        {
            if (disposed) return 0;
            Pause();
            Unload();
            return ++loadToken;
        }
    }

    /// <summary>Installs a prepared load. False when a newer load began or the engine was disposed; the prepared song is released either way.</summary>
    internal bool Publish(int token, PreparedLoad prepared)
    {
        lock (loadGate)
        {
            if (disposed || token == 0 || token != loadToken)
            {
                prepared.Dispose();
                return false;
            }

            SongBytes = prepared.SongBytes;
            song = prepared.TakeSong();
            mixer.SetSong(song);
            foreach (var (lane, stereo) in prepared.Samples)
            {
                loaded.Add(lane);
                if (stereo is not null) mixer.SetSound(lane, stereo);
                else silent.Add(lane);
            }

            scheduler = prepared.Scheduler;
            mixer.SetCues(scheduler);
            songLength = prepared.SongLength;
            cueLength = prepared.CueLength;
            UpdateLength();
            mixer.Seek(0);
            return true;
        }
    }

    private void UpdateLength() =>
        mixer.Length = songLength > 0 ? Math.Max(songLength + Math.Max(mixer.SongOffset, 0), cueLength) : cueLength;

    /// <summary>The playhead in ms: what is being heard now.</summary>
    public double Position => mixer.Position;

    public void Play()
    {
        if (scheduler is not null) mixer.Play();
    }

    /// <summary>Stops where the playhead is.</summary>
    public void Pause() => mixer.Pause();

    /// <summary>Stops and returns to <paramref name="time"/> (default: the start).</summary>
    public void Stop(double time = 0)
    {
        Pause();
        Seek(time);
    }

    public void Seek(double time) => mixer.Seek(time);

    /// <summary>Replaces what plays (after an edit), keeping the song and samples. Safe while playing.</summary>
    public void SetCues(IEnumerable<Cue> cues)
    {
        if (scheduler is null) return; // nothing loaded
        scheduler = new HitScheduler(cues);
        mixer.SetCues(scheduler);
        cueLength = scheduler.Cues.Count > 0 ? scheduler.EndTime + 1000 : 0;
        UpdateLength();
    }

    /// <summary>Plays one lane's sample now, e.g. when its name is clicked.</summary>
    public void Preview(Lane lane, float volume = 1) => mixer.Preview(lane, volume);

    /// <summary>Auditions the exact WAV produced by the export renderer, without adding it to the map.</summary>
    public void PreviewMix(byte[] wav)
    {
        var stereo = SampleMixer.Decode(wav) ?? throw new IOException("The rendered preview could not be decoded.");
        mixer.Audition(stereo);
    }

    public void StopAudition() => mixer.Audition(null);

    public void SetDrumInstrument(IDrumInstrument? instrument, IReadOnlyDictionary<Lane, int>? notes = null) =>
        mixer.SetDrumInstrument(instrument, notes);

    public string? DrumInstrumentError => mixer.DrumInstrumentError;

    /// <summary>Keysound mode's live instrument parts, mixed with the song (empty removes them).</summary>
    public void SetLiveParts(IReadOnlyList<ILivePart> parts) => mixer.SetLiveParts(parts);

    /// <summary>Auditions a resolved map lane, loading an unused custom index on demand.</summary>
    public void Preview(Lane lane, MapFolder folder)
    {
        if (lane.Source != LaneSource.Skin && !loaded.Contains(lane))
            LoadLane(lane, folder.Find(lane.Name) is { } file ? folder.ReadBytes(file) : null);
        Preview(lane);
    }

    /// <summary>Loads the sample of a lane (decoded once to the mix format); no-op if it is already loaded.</summary>
    public void LoadLane(Lane lane, byte[]? data)
    {
        if (!loaded.Add(lane)) return;
        if (data is { Length: > 0 } && SampleMixer.Decode(data) is { Length: > 0 } stereo) mixer.SetSound(lane, stereo);
        else silent.Add(lane);
    }

    /// <summary>
    /// Where playback jumps to at <paramref name="now"/>, or null to carry on. Start before End repeats the range;
    /// End before Start skips the stretch between them (a reversed cycle, as in dawsu).
    /// </summary>
    public static double? LoopJump((double Start, double End) loop, double now) =>
        loop.End > loop.Start && now >= loop.End ? loop.Start
        : loop.Start > loop.End && now >= loop.End && now < loop.Start ? loop.Start
        : null;

    private void Unload()
    {
        mixer.SetSong(null);
        mixer.ClearSounds();
        song?.Dispose();
        song = null;
        loaded.Clear();
        silent.Clear();
        SongBytes = null;
        scheduler = null;
        songLength = cueLength = 0;
        UpdateLength();
    }

    public void Dispose()
    {
        lock (loadGate)
        {
            if (disposed) return;
            disposed = true;
            loadToken++; // a prepare still in flight cannot publish
        }

        Pause();
        output.Close();
        Unload();
        if (ownsBass) Bass.Free();
    }

    /// <summary>No device: tests construct an engine without opening WASAPI.</summary>
    private sealed class SilentOutput : IWavePlayer
    {
        public NAudio.Wave.PlaybackState PlaybackState => NAudio.Wave.PlaybackState.Stopped;
        public float Volume { get; set; } = 1;
        public NAudio.Wave.WaveFormat OutputWaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(PlaybackMixer.Rate, 2);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public void Dispose() { }
        public void Init(IWaveProvider waveProvider) { }
        public void Pause() { }
        public void Play() { }
        public void Stop() { }
    }
}

/// <summary>A map's song and samples already decoded to the mix format. Apply it to an engine, or dispose it to drop the song stream.</summary>
public sealed class PreparedLoad : IDisposable
{
    private BassSong? song;

    public PreparedLoad(BassSong? song, byte[]? songBytes, IReadOnlyList<(Lane Lane, float[]? Stereo)> samples, HitScheduler scheduler, double songLength, double cueLength)
    {
        this.song = song;
        SongBytes = songBytes;
        Samples = samples;
        Scheduler = scheduler;
        SongLength = songLength;
        CueLength = cueLength;
    }

    public byte[]? SongBytes { get; }
    public IReadOnlyList<(Lane Lane, float[]? Stereo)> Samples { get; }
    public HitScheduler Scheduler { get; }
    public double SongLength { get; }
    public double CueLength { get; }

    public BassSong? TakeSong()
    {
        var taken = song;
        song = null;
        return taken;
    }

    public void Dispose() => TakeSong()?.Dispose();
}
