using Avalonia.Input;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

/// <summary>The mouse wheel in both editors: DAW-like, or as osu!'s editor (checked against it).</summary>
public class WheelModeTests
{
    private const bool Daw = false, Osu = true;
    private const KeyModifiers None = KeyModifiers.None, Ctrl = KeyModifiers.Control, Shift = KeyModifiers.Shift, Alt = KeyModifiers.Alt;

    [Fact]
    public void Daw_like_wheel_scrolls_and_zooms()
    {
        Assert.Equal(WheelAction.ScrollRows, WheelMode.For(Daw, None, 0, -1, overHeaders: false).Action);
        Assert.Equal(WheelAction.Pan, WheelMode.For(Daw, Shift, 0, -1, false).Action);
        Assert.Equal(WheelAction.ZoomTime, WheelMode.For(Daw, Ctrl, 0, -1, false).Action);
        Assert.Equal(WheelAction.Divisor, WheelMode.For(Daw, Alt, 0, -1, false).Action);
        Assert.Equal(WheelAction.ZoomRows, WheelMode.For(Daw, Ctrl | Shift, 0, -1, false).Action);
    }

    [Fact]
    public void Osu_wheel_moves_the_playhead_by_grid_lines()
    {
        Assert.Equal((WheelAction.Step, 1), WheelMode.For(Osu, None, 0, -1, false));   // down: later
        Assert.Equal((WheelAction.Step, -1), WheelMode.For(Osu, None, 0, 1, false));   // up: earlier
        Assert.Equal((WheelAction.Step, 4), WheelMode.For(Osu, Shift, 0, -1, false));
        Assert.Equal(WheelAction.Divisor, WheelMode.For(Osu, Ctrl, 0, -1, false).Action);
        Assert.Equal(WheelAction.ZoomTime, WheelMode.For(Osu, Alt, 0, -1, false).Action);
        Assert.Equal(WheelAction.ScrollRows, WheelMode.For(Osu, None, 0, -1, overHeaders: true).Action); // over the lane list or keys
    }

    [Fact]
    public void Grid_steps_repeat_one_line_at_a_time()
    {
        Assert.Equal(1500, WheelMode.StepGrid(1000, 4, (t, d) => t + d * 125));
        Assert.Equal(875, WheelMode.StepGrid(1000, -1, (t, d) => t + d * 125));
    }
}
