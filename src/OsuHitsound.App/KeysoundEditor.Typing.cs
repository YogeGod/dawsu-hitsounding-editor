using Avalonia.Input;
using OsuHitsound.Audio;

namespace OsuHitsound.App;

/// <summary>
/// Musical typing: the computer keyboard as a MIDI keyboard for the selected track. The home row plays white keys from
/// C (A S D F G H J K L ; '), the row above the black keys (W E · T Y U · O P); Z and X shift the octave, C and V the
/// velocity. Notes go through the same path as a MIDI keyboard, so they play the track's instrument and record.
/// While it's on, every other key is ignored except Space (play / stop) and Esc (close), so editor shortcuts can't
/// fire by accident. Keys are read by their position, so other keyboard layouts get the same shape.
/// </summary>
public partial class KeysoundEditor
{
    private static readonly Dictionary<PhysicalKey, int> TypingKeys = new()
    {
        [PhysicalKey.A] = 0, [PhysicalKey.W] = 1, [PhysicalKey.S] = 2, [PhysicalKey.E] = 3, [PhysicalKey.D] = 4,
        [PhysicalKey.F] = 5, [PhysicalKey.T] = 6, [PhysicalKey.G] = 7, [PhysicalKey.Y] = 8, [PhysicalKey.H] = 9,
        [PhysicalKey.U] = 10, [PhysicalKey.J] = 11, [PhysicalKey.K] = 12, [PhysicalKey.O] = 13, [PhysicalKey.L] = 14,
        [PhysicalKey.P] = 15, [PhysicalKey.Semicolon] = 16, [PhysicalKey.Quote] = 17,
    };

    private const int TypingVelocityStep = 10;
    private int typingBase = 60; // middle C under A
    private readonly Dictionary<PhysicalKey, int> typingHeld = []; // key → the pitch it started, for its note-off

    /// <summary>Whether musical typing is on: then the keyboard plays notes instead of running shortcuts.</summary>
    public bool TypingOn => TypingToggle.IsChecked == true;

    private void SetupTyping()
    {
        TypingToggle.IsCheckedChanged += (_, _) => ShowTyping();
        TypingCloseButton.Click += (_, _) => TypingToggle.IsChecked = false;
        TypingHelp.Text = "A S D F G H J K L ; '  white keys    W E  T Y U  O P  black keys    Z / X  octave down / up    "
            + "C / V  velocity down / up    Space  play / stop    Esc  close. Other shortcuts are off while this is open.";
        ShowTyping();
    }

    /// <summary>Turns musical typing on or off (Alt+K, or the toolbar's keyboard button).</summary>
    public void ToggleTyping() => TypingToggle.IsChecked = !TypingOn;

    private void ShowTyping()
    {
        if (!TypingOn) ReleaseTypedNotes();
        TypingPanel.IsVisible = TypingOn;
        // The key names as the piano roll shows them.
        TypingTitle.Text = $"Musical typing · {NoteEditor.PitchName(typingBase)}–{NoteEditor.PitchName(typingBase + 17)} · velocity {TypingVelocity}";
    }

    private int TypingVelocity => (int)(InsertVelocityBox.Value ?? 100);

    /// <summary>A key press while musical typing is on. Returns true when it was used (every key but Space and Esc is).</summary>
    public bool TypingKeyDown(KeyEventArgs e)
    {
        if (!TypingOn) return false;
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false; // closing the window still works
        if (e.Key == Key.Escape) { TypingToggle.IsChecked = false; return true; }
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None) { RunShortcut("play"); return true; }
        if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Alt) { ToggleTyping(); return true; }
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return true; // no Ctrl/Alt shortcuts either
        switch (e.PhysicalKey)
        {
            case PhysicalKey.Z: ShiftTypingOctave(-12); return true;
            case PhysicalKey.X: ShiftTypingOctave(12); return true;
            case PhysicalKey.C: InsertVelocityBox.Value = Math.Max(1, TypingVelocity - TypingVelocityStep); ShowTyping(); return true;
            case PhysicalKey.V: InsertVelocityBox.Value = Math.Min(127, TypingVelocity + TypingVelocityStep); ShowTyping(); return true;
        }

        if (TypingKeys.TryGetValue(e.PhysicalKey, out int offset) && !typingHeld.ContainsKey(e.PhysicalKey))
        {
            int pitch = Math.Clamp(typingBase + offset, 0, 127);
            typingHeld[e.PhysicalKey] = pitch; // a held key repeats its press: only the first one plays
            OnKeyboard(new KeyboardEvent(KeyboardEventKind.NoteOn, pitch, TypingVelocity));
        }

        return true;
    }

    /// <summary>A key release while musical typing is on: ends the note that key started.</summary>
    public bool TypingKeyUp(KeyEventArgs e)
    {
        if (!TypingOn) return false;
        if (typingHeld.Remove(e.PhysicalKey, out int pitch)) OnKeyboard(new KeyboardEvent(KeyboardEventKind.NoteOff, pitch, 0));
        return true;
    }

    private void ShiftTypingOctave(int semitones)
    {
        ReleaseTypedNotes();
        typingBase = Math.Clamp(typingBase + semitones, 12, 108);
        ShowTyping();
    }

    /// <summary>
    /// Ends every note still held: when typing stops, the octave changes, or the window loses focus (whose key releases
    /// then go to another app, so they'd otherwise hang).
    /// </summary>
    public void ReleaseTypedNotes()
    {
        foreach (int pitch in typingHeld.Values) OnKeyboard(new KeyboardEvent(KeyboardEventKind.NoteOff, pitch, 0));
        typingHeld.Clear();
    }
}
