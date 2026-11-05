using NAudio;
using NAudio.Midi;
using OsuHitsound.Lanes.Keysound;

namespace OsuHitsound.Audio;

/// <summary>
/// A message from a MIDI keyboard, decoded: a note (pitch, velocity) or a controller change (sustain and modulation
/// 0–127, pitch bend −8192–8191).
/// </summary>
public readonly record struct KeyboardEvent(KeyboardEventKind Kind, int Pitch, int Value, ControllerKind Controller = default);

public enum KeyboardEventKind { NoteOn, NoteOff, Controller }

/// <summary>
/// The MIDI keyboards connected to the PC (Windows' MIDI inputs, through NAudio's WinMM MidiIn), on every channel.
/// <see cref="Received"/> runs on the driver's thread; listeners must be quick and thread-safe.
/// </summary>
public sealed class MidiKeyboard : IDisposable
{
    private readonly List<MidiIn> inputs = [];

    public event Action<KeyboardEvent>? Received;

    /// <summary>The names of the MIDI inputs Windows has now.</summary>
    public static IReadOnlyList<string> DeviceNames()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try { return [.. Enumerable.Range(0, MidiIn.NumberOfDevices).Select(i => MidiIn.DeviceInfo(i).ProductName)]; }
        catch (MmException) { return []; }
    }

    /// <summary>The inputs that opened (a device in use by another program may refuse).</summary>
    public IReadOnlyList<string> Open { get; private set; } = [];

    /// <summary>Opens one input by name, or every input when <paramref name="device"/> is null. Returns the ones that opened.</summary>
    public IReadOnlyList<string> Start(string? device = null)
    {
        Stop();
        if (!OperatingSystem.IsWindows()) return Open = [];
        var opened = new List<string>();
        var names = DeviceNames();
        for (int i = 0; i < names.Count; i++)
        {
            if (device is not null && names[i] != device) continue;
            try
            {
                var input = new MidiIn(i);
                input.MessageReceived += (_, e) => { if (Decode(e.RawMessage) is { } ev) Received?.Invoke(ev); };
                input.Start();
                inputs.Add(input);
                opened.Add(names[i]);
            }
            catch (MmException) { } // busy or gone: the others still work
        }

        return Open = opened;
    }

    public void Stop()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var input in inputs)
        {
            try { input.Stop(); } catch (MmException) { }
            input.Dispose();
        }

        inputs.Clear();
        Open = [];
    }

    public void Dispose() => Stop();

    /// <summary>
    /// A short MIDI message (status in the low byte, then two data bytes) as a keyboard event, any channel; null for
    /// what Keysound doesn't use. Note-on with velocity 0 is a note-off.
    /// </summary>
    public static KeyboardEvent? Decode(int raw)
    {
        int status = raw & 0xF0, a = (raw >> 8) & 0x7F, b = (raw >> 16) & 0x7F;
        return status switch
        {
            0x90 when b > 0 => new KeyboardEvent(KeyboardEventKind.NoteOn, a, b),
            0x80 or 0x90 => new KeyboardEvent(KeyboardEventKind.NoteOff, a, 0),
            0xB0 when a == 64 => new KeyboardEvent(KeyboardEventKind.Controller, 0, b, ControllerKind.Sustain),
            0xB0 when a == 1 => new KeyboardEvent(KeyboardEventKind.Controller, 0, b, ControllerKind.Modulation),
            0xE0 => new KeyboardEvent(KeyboardEventKind.Controller, 0, (b << 7 | a) - 8192, ControllerKind.PitchBend),
            _ => null,
        };
    }
}
