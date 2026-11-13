using Avalonia.Media;
using OsuHitsound.App;

namespace OsuHitsound.App.Tests;

public class LegacySkinTests
{
    private static string SkinFolder(string ini)
    {
        string folder = Path.Combine(Path.GetTempPath(), "dawsu-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "skin.ini"), ini);
        return folder;
    }

    [Fact]
    public void Skin_ini_settings_are_read_with_comments_and_spaces()
    {
        string folder = SkinFolder(
            "[General]\nVersion: 2.5\nAllowSliderBallTint: 1\nHitCircleOverlayAboveNumber: 0\n" +
            "[Colours]\nCombo2: 1, 2, 3\nCombo1: 10,20,30 // first\nSliderBorder: 0, 82, 75 //green\nSliderTrackOverride: 17,28,26\n" +
            "[Fonts]\nHitCirclePrefix: Fonts/hit/default\nHitCircleOverlap: 15\n");
        try
        {
            var skin = LegacySkin.FromFolder(folder, null);

            Assert.Equal(2.5m, skin.Version);
            Assert.True(skin.AllowSliderBallTint);
            Assert.False(skin.OverlayAboveNumber);
            Assert.Equal([Color.FromRgb(10, 20, 30), Color.FromRgb(1, 2, 3)], skin.ComboColours);
            Assert.Equal(Color.FromRgb(0, 82, 75), skin.SliderBorder);
            Assert.Equal(Color.FromRgb(17, 28, 26), skin.SliderTrackOverride);
            Assert.Equal("Fonts/hit/default", skin.HitCirclePrefix);
            Assert.Equal(15, skin.HitCircleOverlap);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_skin_ini_without_a_version_is_an_old_skin_and_osu_defaults_fill_the_rest()
    {
        string folder = SkinFolder("[General]\nName: plain\n");
        try
        {
            var skin = LegacySkin.FromFolder(folder, null);

            Assert.Equal(1.0m, skin.Version);
            Assert.False(skin.AllowSliderBallTint);
            Assert.True(skin.OverlayAboveNumber);
            Assert.Empty(skin.ComboColours);
            Assert.Null(skin.SliderBorder);
            Assert.Equal("default", skin.HitCirclePrefix);
            Assert.Equal(-2, skin.HitCircleOverlap);
            Assert.Null(skin.Get("hitcircle")); // no image, no fallback
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Skin_choices_list_lazers_skins_first_then_the_skin_folders_by_name()
    {
        string skins = Path.Combine(Path.GetTempPath(), "dawsu-skins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(skins, "zeta"));
        Directory.CreateDirectory(Path.Combine(skins, "Alpha"));
        try
        {
            var choices = SkinChoice.All(skins, lazerInstalled: true);

            Assert.Equal(["argon", "classic", "folder:Alpha", "folder:zeta"], choices.Select(c => c.Id));
            Assert.Equal(["argon"], SkinChoice.All(null, lazerInstalled: false).Select(c => c.Id));
        }
        finally { Directory.Delete(skins, true); }
    }

    [Fact]
    public void The_skins_folder_is_the_one_beside_the_songs_folder()
    {
        string osu = Path.Combine(Path.GetTempPath(), "dawsu-osu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(osu, "Songs"));
        Directory.CreateDirectory(Path.Combine(osu, "Skins"));
        try
        {
            Assert.Equal(Path.Combine(osu, "Skins"), SkinChoice.FindSkinsFolder(Path.Combine(osu, "Songs")));
        }
        finally { Directory.Delete(osu, true); }
    }
}