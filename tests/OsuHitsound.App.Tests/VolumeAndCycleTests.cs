using OsuHitsound.Audio;
using OsuHitsound.Lanes;

namespace OsuHitsound.App.Tests;

public class VolumeAndCycleTests
{
    private static MomentKey K(int i) => new(i, 0);

    [Theory]
    [InlineData(RampShape.Linear, 0.5, 60)]
    [InlineData(RampShape.EaseIn, 0.5, 40)]  // 20 + 80·0.25
    [InlineData(RampShape.EaseOut, 0.5, 80)] // 20 + 80·0.75
    [InlineData(RampShape.SCurve, 0.5, 60)]
    [InlineData(RampShape.SCurve, 0.25, 33)] // 20 + 80·0.15625
    public void Ramp_shapes(RampShape shape, double t, int expected) =>
        Assert.Equal(expected, VolumeRamp.At(shape, t, 20, 100));

    [Fact]
    public void A_ramp_sets_moments_in_its_range_and_works_either_way_round()
    {
        var moments = new[] { (0.0, K(0)), (100.0, K(1)), (200.0, K(2)), (300.0, K(3)) };
        var forwards = VolumeRamp.Ramp(moments, 100, 10, 300, 90, RampShape.Linear);
        var backwards = VolumeRamp.Ramp(moments, 300, 90, 100, 10, RampShape.Linear);

        Assert.Equal(new Dictionary<MomentKey, int> { [K(1)] = 10, [K(2)] = 50, [K(3)] = 90 }, forwards);
        Assert.Equal(forwards, backwards);
    }

    [Fact]
    public void Relative_edits_keep_volumes_within_1_to_100()
    {
        var volumes = new Dictionary<MomentKey, int> { [K(0)] = 95, [K(1)] = 5 };
        Assert.Equal(new Dictionary<MomentKey, int> { [K(0)] = 100, [K(1)] = 15 }, VolumeRamp.Shift(volumes, [K(0), K(1)], 10));
        Assert.Equal(new Dictionary<MomentKey, int> { [K(0)] = 85, [K(1)] = 1 }, VolumeRamp.Shift(volumes, [K(0), K(1)], -10));
    }

    [Theory]
    [InlineData(1000, 2000, 1500, null)]   // inside a cycle: carry on
    [InlineData(1000, 2000, 2000, 1000.0)] // reached R: back to L
    [InlineData(2000, 1000, 500, null)]    // reversed: before the skipped stretch
    [InlineData(2000, 1000, 1000, 2000.0)] // reversed: entering the stretch jumps over it
    [InlineData(2000, 1000, 2500, null)]   // reversed: after it
    public void Cycles_repeat_and_reversed_cycles_skip(double start, double end, double now, double? expected) =>
        Assert.Equal(expected, AudioEngine.LoopJump((start, end), now));
}
