using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private bool fillingHs;

    private void SetupHsMode()
    {
        HsModeButton.IsCheckedChanged += (_, _) =>
        {
            if (fillingHs || session is null) return;
            session.SetHsMode(HsModeButton.IsChecked == true);
            RefreshHsMode();
            ScheduleProjectSave();
        };
    }

    private void RefreshHsMode()
    {
        fillingHs = true;
        try
        {
            HsModeButton.IsEnabled = session?.HsEligible == true && !WriteBusy;
            HsModeButton.IsChecked = session?.State.HsMode == true;
            ToolTip.SetTip(HsModeButton, session?.HsEligible == true
                ? "Create centred circles for hitsounds. Shared times use one circle; removing its last sound removes an owned circle. Off keeps circles; enabling resumes maintenance. Saved per difficulty."
                : "Available for difficulties that started empty or whose difficulty name contains the word HS or hitsounds.");
        }
        finally { fillingHs = false; }
    }
}
