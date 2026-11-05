using Avalonia;
using Avalonia.Controls;

namespace OsuHitsound.App;

public partial class MainWindow
{
    private double browserWidth = 280;
    private bool compactBrowser, wideBrowserOpen = true;
    private double BrowserWidthToSave => BrowserHost.IsVisible && Root.ColumnDefinitions[4].ActualWidth > 0 ? Root.ColumnDefinitions[4].ActualWidth : browserWidth;

    private void ShowBrowser(bool open)
    {
        if (!open && Root.ColumnDefinitions[4].ActualWidth > 0) browserWidth = Root.ColumnDefinitions[4].ActualWidth;
        // Keep a useful canvas even when both panels are explicitly opened on a small window.
        double width = Bounds.Width <= 0 ? browserWidth : Math.Min(browserWidth, Math.Max(220, Bounds.Width - (Inspector.IsVisible ? InspectorWidthToSave : 0) - 520));
        Root.ColumnDefinitions[4].Width = new GridLength(open ? width : 0);
        Root.ColumnDefinitions[3].Width = new GridLength(open ? 4 : 0);
        BrowserHost.IsVisible = BrowserSplitter.IsVisible = open;
    }

    private void SetupWorkflowPanels()
    {
        BrowserHost.Child = Inspector.Browser;
        browserWidth = double.IsFinite(prefs.BrowserWidth) ? Math.Clamp(prefs.BrowserWidth, 220, 500) : 280;
        wideBrowserOpen = prefs.BrowserOpen;
        BrowserPanelItem.IsChecked = prefs.BrowserOpen;
        BrowserPanelItem.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsCheckedProperty) ShowBrowser(BrowserPanelItem.IsChecked); };
        Inspector.Browser.CloseRequested += () => { BrowserPanelItem.IsChecked = false; ShowBrowser(false); };
        HistoryPanelItem.IsChecked = prefs.HistoryOpen;
        SoundSetsPanelItem.IsChecked = prefs.SoundSetsOpen;
        void Refresh() => Inspector.SetOptionalPanels(HistoryPanelItem.IsChecked, SoundSetsPanelItem.IsChecked);
        HistoryPanelItem.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsCheckedProperty) Refresh(); };
        SoundSetsPanelItem.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsCheckedProperty) Refresh(); };
        RangeToolItem.Click += (_, _) => SetTool(Tool.Range);
        Refresh(); ShowBrowser(prefs.BrowserOpen);
    }
}
