using System.Text;
using OsuHitsound.Format;
using OsuHitsound.Lanes;

namespace OsuHitsound.Lanes.Tests;

public class PlayfieldTests
{
    // 120 BPM, slider multiplier 1: a slider travels 100 units per 500 ms beat.
    private static Playfield Build(string objects, string difficulty = "CircleSize:4\nApproachRate:9\n", string version = "14", string general = "") =>
        Playfield.Build(OsuFile.Parse(Encoding.UTF8.GetBytes(
            $"osu file format v{version}\n[General]\n{general}[Difficulty]\n{difficulty}SliderMultiplier:1\nSliderTickRate:1\n" +
            $"[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n{objects}")));

    [Fact]
    public void Circle_size_and_approach_rate_give_the_radius_and_the_time_objects_appear()
    {
        var field = Build("100,100,1000,5,0,0:0:0:0:\n");

        Assert.Equal(54.4 - 4.48 * 4, field.Radius, 6);
        Assert.Equal(600, field.Preempt, 6); // AR 9
        Assert.Equal(new PlayfieldPoint(100, 100), field.Objects[0].Position);
        Assert.Single(field.Between(400, 400 + field.Preempt));   // appearing at 400
        Assert.Empty(field.Between(300, 300 + field.Preempt));    // not yet
        Assert.Empty(field.Between(1001, 2000));                  // gone
    }

    [Fact]
    public void A_map_without_an_approach_rate_uses_its_overall_difficulty()
    {
        var field = Build("100,100,1000,5,0,0:0:0:0:\n", "CircleSize:4\nOverallDifficulty:5\n");

        Assert.Equal(1200, field.Preempt, 6);
    }

    [Fact]
    public void A_linear_slider_runs_its_length_and_the_ball_goes_back_and_forth()
    {
        // 100 units long with one repeat: 500 ms per span, ending at 2000.
        var slider = Build("100,100,1000,6,0,L|300:100,2,100\n").Objects[0];

        Assert.Equal(2000, slider.End, 6);
        Assert.Equal(new PlayfieldPoint(200, 100), slider.Path[^1]); // cut at 100 units, not at the control point
        Assert.Equal(150, slider.BallAt(1250).X, 6);
        Assert.Equal(200, slider.BallAt(1500).X, 6);
        Assert.Equal(150, slider.BallAt(1750).X, 6);
        Assert.Equal(new PlayfieldPoint(100, 100), slider.EndPosition); // two spans end at the head
    }

    [Fact]
    public void A_slider_longer_than_its_control_points_is_extended_in_a_straight_line()
    {
        var slider = Build("100,100,1000,6,0,L|150:100,1,100\n").Objects[0];

        Assert.Equal(200, slider.Path[^1].X, 6);
    }

    [Fact]
    public void A_perfect_curve_is_an_arc_through_its_three_points()
    {
        // A half circle of radius 50 around (150,100): its length is 50π.
        var slider = Build("100,100,1000,6,0,P|150:50|200:100,1,157.0796326795\n").Objects[0];

        Assert.All(slider.Path, p => Assert.Equal(50, p.DistanceTo(new PlayfieldPoint(150, 100)), 2));
        var middle = slider.PointAt(0.5);
        Assert.Equal(150, middle.X, 1);
        Assert.Equal(50, middle.Y, 1);
        Assert.Equal(200, slider.Path[^1].X, 1);
    }

    [Fact]
    public void A_bezier_point_written_twice_makes_a_corner()
    {
        // Two straight pieces joined at (200,100): a single curve would cut the corner.
        var slider = Build("100,100,1000,6,0,B|200:100|200:100|200:200,1,200\n").Objects[0];

        Assert.Contains(new PlayfieldPoint(200, 100), slider.Path);
        Assert.Equal(new PlayfieldPoint(200, 200), slider.Path[^1]);
    }

    [Fact]
    public void Circles_on_one_spot_stack_up_and_left_with_the_last_one_in_place()
    {
        var field = Build("100,100,1000,5,0,0:0:0:0:\n100,100,1100,1,0,0:0:0:0:\n100,100,1200,1,0,0:0:0:0:\n300,300,1300,1,0,0:0:0:0:\n");

        Assert.Equal([2, 1, 0, 0], field.Objects.Select(o => o.StackHeight));
        Assert.Equal(2 * field.Radius / 10, field.Objects[0].StackOffset, 6);
    }

    [Fact]
    public void Circles_too_far_apart_in_time_do_not_stack()
    {
        // AR 9 and the default stack leniency 0.7: notes more than 420 ms apart stay where they are.
        var field = Build("100,100,1000,5,0,0:0:0:0:\n100,100,1500,1,0,0:0:0:0:\n");

        Assert.All(field.Objects, o => Assert.Equal(0, o.StackHeight));
    }

    [Fact]
    public void A_spinner_sits_in_the_middle_until_its_end_and_old_files_play_24_ms_later()
    {
        var field = Build("256,192,1000,12,0,3000,0:0:0:0:\n", version: "4");

        var spinner = field.Objects[0];
        Assert.Equal(HitObjectKind.Spinner, spinner.Kind);
        Assert.Equal((1024, 3024), (spinner.Start, spinner.End));
        Assert.Equal(new PlayfieldPoint(256, 192), spinner.Position);
    }

    [Fact]
    public void An_unreadable_object_is_left_out()
    {
        var field = Build("100,100,1000,5,0,0:0:0:0:\nnot,an,object\n200,100,2000,6,0\n");

        Assert.Single(field.Objects);
    }
}
