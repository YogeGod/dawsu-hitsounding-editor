using System.Collections.Concurrent;
using MeltySynth;
using OsuHitsound.Lanes;
using OsuHitsound.Lanes.Keysound;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

namespace OsuHitsound.Audio;

/// <summary>One preset of a SoundFont: its bank (128 = a percussion kit), program and name.</summary>
public readonly record struct SoundFontPreset(int Bank, int Program, string Name)
{
    public string Label => $"{Bank:000}:{Program:000} {Name}";
    public bool IsDrumKit => Bank == 128;
}

/// <summary>
/// SoundFont (.sf2) files, played with MeltySynth. A file is read once and shared (they can be hundreds of MB) until
/// it changes on disk; each instrument on it gets its own synthesizer.
/// </summary>
public static class SoundFonts
{
    // Weakly held: a file stays read while a track or kit plays it (their synthesizers keep it), and can go after that.
    private static readonly Dictionary<string, (DateTime Written, long Length, WeakReference<SoundFont> Font)> loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A path dawsu! may open as a SoundFont: a fully qualified local path to a .sf2. A project from elsewhere can't
    /// make it reach a network share (\\server\…), which Windows would answer with the user's login hash.
    /// </summary>
    public static bool IsLoadablePath(string? path) =>
        path is not null && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !path.StartsWith("//", StringComparison.Ordinal) && path.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a .sf2 (or gives the copy already read). Throws IOException for a missing, unreadable or non-local file.</summary>
    public static SoundFont Load(string path)
    {
        if (!IsLoadablePath(path)) throw new IOException($"{Path.GetFileName(path)}: only a .sf2 file on this computer can be used.");
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists) throw new FileNotFoundException($"{file.Name} isn't there any more.", file.FullName);
        lock (loaded)
        {
            if (loaded.TryGetValue(file.FullName, out var hit) && hit.Written == file.LastWriteTimeUtc && hit.Length == file.Length
                && hit.Font.TryGetTarget(out var cached)) return cached;
            SoundFont font;
            try { font = new SoundFont(file.FullName); }
            // A damaged or foreign file can fail anywhere in the parser: report it as unreadable, whatever it threw.
            catch (Exception ex) when (ex is not (IOException or OutOfMemoryException))
            {
                throw new IOException($"{file.Name} isn't a SoundFont dawsu! can read ({ex.Message}).", ex);
            }

            loaded[file.FullName] = (file.LastWriteTimeUtc, file.Length, new WeakReference<SoundFont>(font));
            return font;
        }
    }

    /// <summary>The presets of a SoundFont, by bank then program.</summary>
    public static IReadOnlyList<SoundFontPreset> Presets(SoundFont font) =>
        [.. font.Presets.Select(p => new SoundFontPreset(p.BankNumber, p.PatchNumber, p.Name.Trim())).OrderBy(p => p.Bank).ThenBy(p => p.Program)];
}

/// <summary>
/// A synthesizer playing one preset. Every incoming MIDI message is played on the preset's channel (channel 10 for a
/// percussion kit, else channel 1), and program or bank changes are ignored, so the chosen preset stays. Not thread
/// safe: one thread renders it (the audio thread, or an offline render).
/// </summary>
public sealed class SoundFontSynth
{
    private const int Chunk = 1024;
    private readonly Synthesizer synth;
    private readonly int channel, bank, program;
    private readonly float[] left = new float[Chunk], right = new float[Chunk];

    /// <summary>The synthesizer for a SoundFont instrument (a Keysound track's, or a drum kit's), reading its file.</summary>
    public static SoundFontSynth Create(KeyInstrument instrument) =>
        new(SoundFonts.Load(instrument.Path ?? ""), instrument.Bank, instrument.Program, instrument.Effects);

    public SoundFontSynth(SoundFont font, int bank, int program, bool effects)
    {
        synth = new Synthesizer(font, new SynthesizerSettings(PlaybackMixer.Rate) { EnableReverbAndChorus = effects });
        (this.bank, this.program) = (bank, program);
        channel = bank == 128 ? synth.PercussionChannel : 0;
        SelectPreset();
    }

    private void SelectPreset()
    {
        // On the percussion channel MeltySynth counts banks from 128, so its own bank 0 is the kit bank.
        synth.ProcessMidiMessage(channel, 0xB0, 0, bank == 128 ? 0 : bank);
        synth.ProcessMidiMessage(channel, 0xC0, program, 0);
    }

    /// <summary>Plays one MIDI message on the preset's channel (note on/off, pedal, pitch bend, all-notes-off…).</summary>
    public void Send(int status, int data1, int data2)
    {
        int kind = status & 0xF0;
        if (kind is < 0x80 or 0xC0 or 0xF0) return; // keep the chosen preset; no system messages
        if (kind == 0xB0 && data1 is 0 or 32) return; // bank select
        synth.ProcessMidiMessage(channel, kind, data1, data2);
        if (kind == 0xB0 && data1 == 121) SelectPreset(); // "reset all controllers" in MeltySynth also resets the program
    }

    /// <summary>Adds <paramref name="frames"/> frames of interleaved stereo into <paramref name="stereo"/>.</summary>
    public void RenderAdd(Span<float> stereo, int frames)
    {
        for (int done = 0; done < frames;)
        {
            int n = Math.Min(Chunk, frames - done);
            synth.Render(left.AsSpan(0, n), right.AsSpan(0, n));
            for (int i = 0; i < n; i++)
            {
                stereo[(done + i) * 2] += left[i];
                stereo[(done + i) * 2 + 1] += right[i];
            }

            done += n;
        }
    }

    /// <summary>A block with MIDI messages at frame offsets (sorted): the audio is rendered up to each one, then it plays.</summary>
    public void RenderAdd(Span<float> stereo, int frames, ReadOnlySpan<(int Offset, byte Status, byte Data1, byte Data2)> events)
    {
        int at = 0;
        foreach (var e in events)
        {
            int offset = Math.Clamp(e.Offset, 0, frames);
            if (offset > at) RenderAdd(stereo.Slice(at * 2), offset - at);
            at = Math.Max(at, offset);
            Send(e.Status, e.Data1, e.Data2);
        }

        if (frames > at) RenderAdd(stereo.Slice(at * 2), frames - at);
    }

    /// <summary>Every note off at once, and its tail cut (between offline renders).</summary>
    public void Silence() => synth.NoteOffAll(immediate: true);
}

/// <summary>
/// A Keysound track played by a SoundFont preset in the mixer, fed like a VST3 track: its notes and controllers as
/// timed MIDI, plus live messages from a MIDI keyboard or musical typing.
/// </summary>
public sealed class SoundFontPart(SoundFontSynth synth) : IMidiPart
{
    private TimedMidi[] scheduled = [];
    private readonly ConcurrentQueue<(byte Status, byte Data1, byte Data2)> live = new();
    private readonly HashSet<(int Channel, int Pitch)> sounding = [];
    private readonly List<(int Offset, byte Status, byte Data1, byte Data2)> block = [];

    public void SetEvents(IEnumerable<TimedMidi> events) => Volatile.Write(ref scheduled, IsolatedVst3.SortForPlayback(events));

    public void Live(byte status, byte data1, byte data2) => live.Enqueue((status, data1, data2));

    public void ReleaseAll()
    {
        foreach (var (channel, pitch) in sounding) live.Enqueue(((byte)(0x80 | channel), (byte)pitch, 0));
        sounding.Clear();
    }

    public void Render(Span<float> stereo, int frames, double start, double msPerFrame, bool playing)
    {
        block.Clear();
        while (live.TryDequeue(out var e)) block.Add((0, e.Status, e.Data1, e.Data2));
        if (playing && msPerFrame > 0)
        {
            var list = Volatile.Read(ref scheduled);
            double end = start + frames * msPerFrame;
            for (int i = Sorted.LowerBound(list, start, e => e.Time); i < list.Length && list[i].Time < end; i++)
            {
                var e = list[i];
                block.Add(((int)Math.Clamp(Math.Round((e.Time - start) / msPerFrame), 0, frames - 1), e.Status, e.Data1, e.Data2));
                int kind = e.Status & 0xF0, channel = e.Status & 0x0F;
                if (kind == 0x90 && e.Data2 > 0) sounding.Add((channel, e.Data1));
                else if (kind is 0x80 or 0x90) sounding.Remove((channel, e.Data1));
            }
        }

        synth.RenderAdd(stereo, frames, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(block));
    }
}

/// <summary>Generate's renderer for a SoundFont track: a fresh synthesizer per note, so no tail carries over.</summary>
public sealed class SoundFontNoteRenderer(SoundFont font, int bank, int program, bool effects) : IKeyNoteRenderer
{
    public SoundFontNoteRenderer(KeyInstrument instrument)
        : this(SoundFonts.Load(instrument.Path ?? ""), instrument.Bank, instrument.Program, instrument.Effects) { }

    public Task<RenderedAudio> RenderAsync(KeyRenderJob job, RenderLimits limits, CancellationToken cancel = default) => Task.Run(() =>
    {
        // A fresh synthesizer per note: equal notes render equal audio, and no tail carries over.
        var synth = new SoundFontSynth(font, bank, program, effects);
        var (events, last) = VstNoteRenderer.Events(job);
        var due = new List<(int Offset, byte Status, byte Data1, byte Data2)>();
        int next = 0;
        return KeyTailRender.Run(last, limits, cancel, (block, n, frame) =>
        {
            due.Clear();
            for (; next < events.Count && events[next].Frame < frame + n; next++)
                due.Add(((int)(events[next].Frame - frame), events[next].Status, events[next].Data1, events[next].Data2));
            synth.RenderAdd(block, n, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(due));
        }, pending: () => next < events.Count);
    }, cancel);

    public void Dispose() { }
}

/// <summary>A SoundFont preset as the Hitsound editor's drum kit: plays mapped lanes live and renders drum WAVs.</summary>
public sealed class SoundFontDrumKit(SoundFontSynth synth) : IDrumInstrument
{
    private readonly List<(int Offset, byte Status, byte Data1, byte Data2)> block = [];

    public bool Process(float[] stereo, int frames, MidiEvent[] events, int count)
    {
        Array.Clear(stereo, 0, frames * 2);
        block.Clear();
        // By offset, keeping the given order at equal offsets: a retrigger's note-off must stay before its note-on.
        for (int i = 0; i < count; i++)
        {
            var e = (events[i].Offset, (byte)events[i].Status, (byte)events[i].Data1, (byte)events[i].Data2);
            int at = block.Count;
            while (at > 0 && block[at - 1].Offset > e.Offset) at--;
            block.Insert(at, e);
        }
        synth.RenderAdd(stereo, frames, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(block));
        return true;
    }

    /// <summary>For <see cref="DrumVstRenderer"/>'s offline renders.</summary>
    public void Process(float[] stereo, int frames, MidiEvent[] events) => Process(stereo, frames, events, events.Length);
}
