using Avalonia.Input;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

/// <summary>The cursor over the grid and bars per tool, with Alt drawing as in V6.39.</summary>
public class ToolCursorTests
{
    [Theory]
    [InlineData("Select", "Arrow")]
    [InlineData("Draw", "Pen")]
    [InlineData("Erase", "Eraser")]
    [InlineData("Range", "Cross")]
    [InlineData("Mute", "Cross")]
    [InlineData("Ramp", "Cross")]
    public void Each_tool_has_its_cursor_over_the_grid(string tool, string expected) =>
        Assert.Equal(Enum.Parse<ToolCursor>(expected), ToolCursors.For(Enum.Parse<Tool>(tool), KeyModifiers.None, overGrid: true));

    [Theory]
    [InlineData("Select")]
    [InlineData("Erase")]
    [InlineData("Range")]
    public void Holding_alt_shows_the_pen_with_any_tool(string tool) =>
        Assert.Equal(ToolCursor.Pen, ToolCursors.For(Enum.Parse<Tool>(tool), KeyModifiers.Alt, overGrid: true));

    [Fact]
    public void Ctrl_alt_and_altgr_are_not_the_draw_modifier() =>
        Assert.Equal(ToolCursor.Arrow, ToolCursors.For(Tool.Select, KeyModifiers.Alt | KeyModifiers.Control, overGrid: true));

    [Fact]
    public void Outside_the_grid_it_is_always_the_arrow() =>
        Assert.Equal(ToolCursor.Arrow, ToolCursors.For(Tool.Draw, KeyModifiers.Alt, overGrid: false));
}
