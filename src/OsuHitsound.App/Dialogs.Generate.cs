using Avalonia.Automation;
using Avalonia.Controls;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal static partial class Dialogs
{
    /// <summary>Replans read-only when scope changes; only the currently displayed, valid plan can be accepted.</summary>
    public static Task<(GeneratePlan Plan, MapFolder Folder)?> ConfirmGenerate(Window owner,
        (GeneratePlan Plan, MapFolder Folder) initial, Func<GeneratePlan, string> summary,
        Func<bool, Task<(GeneratePlan Plan, MapFolder Folder)>> replan, ExportMergeSettings? mergeSettings = null)
    {
        Button? generate = null;
        return Show<(GeneratePlan Plan, MapFolder Folder)?>(owner, "Generate hitsound diff", (body, close) =>
        {
            var current = initial;
            bool ready = current.Plan.Conflicts.Count == 0;
            var scope = new CheckBox { Content = "Include slider-body sounds and ticks", IsChecked = false };
            AutomationProperties.SetName(scope, "Include slider-body sounds and ticks");
            var description = MergeText("Normal, whistle, finish and clap hitsounds are included, including slider heads, repeats and tails. Slider-slide, slider-whistle and ticks are off by default; including them can create many extra samples.");
            var details = MergeText(summary(current.Plan));
            body.Children.AddRange([description, scope, details]);
            scope.IsCheckedChanged += async (_, _) =>
            {
                ready = false;
                scope.IsEnabled = false;
                if (generate is not null) generate.IsEnabled = false;
                details.Text = "Updating sample counts…";
                try
                {
                    var prepared = await replan(scope.IsChecked == true);
                    if (TopLevel.GetTopLevel(body) is null) return; // Cancel stays available during planning.
                    current = prepared;
                    details.Text = summary(current.Plan);
                    ready = current.Plan.Conflicts.Count == 0;
                }
                catch (Exception ex)
                {
                    if (TopLevel.GetTopLevel(body) is null) return;
                    details.Text = $"Cannot prepare this export: {ex.Message}\nNothing was written. Change the option or cancel.";
                }
                finally
                {
                    scope.IsEnabled = true;
                    if (generate is not null) generate.IsEnabled = ready;
                }
            };
            if (mergeSettings is not null)
            {
                var status = MergeText($"{mergeSettings.Rules.Count} combination rules.");
                var save = new Button { Content = "Save merge preset…" };
                save.Click += async (_, _) => await SaveMergePreset((Window)TopLevel.GetTopLevel(body)!, mergeSettings, status);
                body.Children.AddRange([save, status]);
            }
            return (() => { if (ready) close(current); }, "Generate");
        }, scroll: true, configureOk: button => { generate = button; button.IsEnabled = initial.Plan.Conflicts.Count == 0; });
    }
}
