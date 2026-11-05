using OsuHitsound.Lanes;
using MidiEvent = OsuHitsound.Audio.Vst3Instrument.MidiEvent;

namespace OsuHitsound.Audio;

/// <summary>One audio consumer. Lifecycle and editor operations belong to the instrument's UI owner.</summary>
public interface IDrumInstrument
{
    bool Process(float[] stereo, int frames, MidiEvent[] events, int count);
}

/// <summary>Live MIDI scheduling, called under the mixer's gate; never dispatches GUI events.</summary>
internal sealed class DrumInstrumentPlayback(IDrumInstrument instrument, IReadOnlyDictionary<Lane, int> mapping)
{
    internal const int BlockSize = 512;
    private readonly MidiEvent[] midi = new MidiEvent[4096];
    private readonly float[] audio = new float[BlockSize * 2];
    private readonly long[] releases = new long[128];
    private readonly List<(int Note, float Volume)> previews = [];
    private long frame;
    private bool reset = true, failed, overflow;
    private int count;
    public string? Error { get; private set; }
    public bool Routes(Lane lane) => mapping.ContainsKey(lane);
    public void Reset() { reset = true; previews.Clear(); }
    public void Preview(Lane lane, float volume)
    {
        if (mapping.TryGetValue(lane, out int note) && previews.Count < 256) previews.Add((note, volume));
    }
    public void Begin()
    {
        count = 0;
        if (overflow) { reset = true; overflow = false; }
        if (!reset) return;
        // All sound off also cuts release tails on seek/pause/change.
        Add(new(0, 0xb9, 120, 0));
        Add(new(0, 0xb9, 123, 0));
        Array.Clear(releases);
        reset = false;
    }
    public void Trigger(Lane lane, float volume, int offset)
    {
        if (mapping.TryGetValue(lane, out int note)) Note(note, volume, offset);
    }
    private void Note(int note, float volume, int offset)
    {
        if (volume <= 0 || count > midi.Length - 2) return;
        offset = Math.Clamp(offset, 0, BlockSize - 1);
        if (releases[note] > 0) Add(new(offset, 0x89, note, 0));
        Add(new(offset, 0x99, note, Math.Clamp((int)Math.Round(volume * 127), 1, 127)));
        releases[note] = frame + offset + PlaybackMixer.Rate / 10;
    }
    private void Add(MidiEvent item)
    {
        if (count < midi.Length) midi[count++] = item;
        else overflow = true;
    }
    public void Render(Span<float> destination)
    {
        int frames = destination.Length / 2;
        foreach (var preview in previews) Note(preview.Note, preview.Volume, 0);
        previews.Clear();
        for (int note = 0; note < releases.Length; note++)
            if (releases[note] > 0 && releases[note] < frame + frames)
            {
                Add(new((int)Math.Max(0, releases[note] - frame), 0x89, note, 0));
                releases[note] = 0;
            }
        // JUCE orders events by sample offset; insertion order preserves off/on at retriggers.
        try
        {
            if (!failed && instrument.Process(audio, frames, midi, count))
                for (int i = 0; i < destination.Length; i++)
                {
                    if (!float.IsFinite(audio[i])) throw new InvalidOperationException("The instrument returned invalid audio.");
                    destination[i] += audio[i];
                }
            else if (!failed) throw new InvalidOperationException("Live VST processing failed. Unload and reload the instrument.");
        }
        catch (Exception ex)
        {
            failed = true;
            Error = ex.Message;
        }
        frame += frames;
    }
}
