using System.Text;
using OsuHitsound.App;
using OsuHitsound.Format;

namespace OsuHitsound.App.Tests;

public class ComboTests
{
    [Fact]
    public void Combos_count_as_in_osu_with_new_combos_colour_skips_and_spinners()
    {
        // Circle (new combo), circle, circle (new combo, skip 1 colour: type 5 + 16), spinner (new combo), circle after the spinner.
        var file = OsuFile.Parse(Encoding.UTF8.GetBytes(
            "osu file format v14\n[TimingPoints]\n0,500,4,2,0,100,1,0\n[HitObjects]\n" +
            "256,192,1000,5,0,0:0:0:0:\n256,192,1500,1,0,0:0:0:0:\n256,192,2000,21,0,0:0:0:0:\n" +
            "256,192,2500,12,0,3000,0:0:0:0:\n256,192,3500,1,0,0:0:0:0:\n"));

        var combos = ArrangeModel.ComboOf(file);

        Assert.Equal([(1, 0), (2, 0), (1, 2), (0, 3), (1, 4)], combos);
    }
}
