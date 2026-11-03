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
    private BassSong? song;
    private HitScheduler? scheduler;

    private AudioEngine(OpenOutput output, PlaybackMixer mixer, string? fallback)
    {
        (this.output, this.mixer) = (output, mixer);
        Fallback = fallback;
        mixer.LatencyMs = output.LatencyMs;
    }

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

    /// <summary>Loads a map's song and samples, replacing what was loaded. Stops playback.</summary>
    public void Load(DecompiledMap map, MapFolder folder, IReadOnlyDictionary<string, byte[]> skin)
    {
        Pause();
        Unload();

        if (map.Settings.AudioFilename is { Length: > 0 } audio && folder.Find(audio) is { } songFile)
        {
            var bytes = folder.ReadBytes(songFile);
            SongBytes = bytes;
            song = BassSong.Open(bytes);
            mixer.SetSong(song);
        }

        foreach (var lane in map.Lanes)
        {
            byte[]? data = lane.Missing ? null : lane.Source switch
            {
                LaneSource.Skin => skin.GetValueOrDefault(lane.Name),
                _ => folder.Find(lane.Name) is { } f ? folder.ReadBytes(f) : null,
            };
            LoadLane(lane, data);
        }

        scheduler = new HitScheduler(map);
        mixer.SetCues(scheduler);
        songLength = song?.LengthMs ?? 0;
        cueLength = scheduler.Cues.Count > 0 ? scheduler.EndTime + 1000 : 0;
        UpdateLength();
        mixer.Seek(0);
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
        Pause();
        output.Close();
        Unload();
        Bass.Free();
    }
}
