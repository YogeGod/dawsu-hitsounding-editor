using Avalonia.Input;

namespace OsuHitsound.App;

/// <summary>What the mouse wheel does over the editing area, for both editors.</summary>
internal enum WheelAction { None, ScrollRows, Pan, ZoomTime, ZoomRows, Divisor, Step }

/// <summary>
/// The mouse wheel's meaning. DAW-like: the wheel scrolls rows, Shift pans, Ctrl zooms in time, Ctrl+Shift zooms rows
/// and Alt changes the snap. osu! editor (checked against osu!'s editor): the wheel moves the playhead one snap step
/// (down = later), Shift+wheel four steps, Ctrl+wheel changes the snap, Alt+wheel zooms in time; Ctrl+Shift still
/// zooms rows, and over the lane list or the keys the wheel still scrolls them.
/// </summary>
internal static class WheelMode
{
    /// <summary>On with the osu! editor key preset (the window sets it with the keys).</summary>
    public static bool Osu { get; set; }

    /// <summary>The action for a wheel turn, and for <see cref="WheelAction.Step"/> how many snap steps (signed, later = +).</summary>
    public static (WheelAction Action, int Steps) For(bool osu, KeyModifiers mods, double deltaX, double deltaY, bool overHeaders)
    {
        bool ctrl = mods.HasFlag(KeyModifiers.Control), shift = mods.HasFlag(KeyModifiers.Shift), alt = mods.HasFlag(KeyModifiers.Alt);
        if (ctrl && shift) return (WheelAction.ZoomRows, 0);
        if (deltaX != 0 && deltaY == 0) return (WheelAction.Pan, 0); // a trackpad's sideways swipe
        if (!osu)
            return ctrl ? (WheelAction.ZoomTime, 0) : alt ? (WheelAction.Divisor, 0) : shift ? (WheelAction.Pan, 0) : (WheelAction.ScrollRows, 0);
        if (ctrl) return (WheelAction.Divisor, 0);
        if (alt) return (WheelAction.ZoomTime, 0);
        if (overHeaders && !shift) return (WheelAction.ScrollRows, 0);
        return (WheelAction.Step, (deltaY < 0 ? 1 : deltaY > 0 ? -1 : 0) * (shift ? 4 : 1));
    }

    /// <summary><paramref name="steps"/> grid lines from <paramref name="time"/> (signed), one <paramref name="gridStep"/> at a time.</summary>
    public static double StepGrid(double time, int steps, Func<double, int, double> gridStep)
    {
        for (int i = 0; i < Math.Abs(steps); i++) time = gridStep(time, Math.Sign(steps));
        return time;
    }
}
