using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>The inspector cards: Selection, Tracks, Instrument, MIDI input and Bookmarks, folded, moved and shown or hidden as in Hitsound mode.</summary>
public partial class KeysoundEditor
{
    private const double InspectorWidth = 250;

    private void SetupInspector()
    {
        // The card bodies are laid out in XAML (their controls keep their names); the cards take them over.
        foreach (var (id, title, body) in new (string, string, Control)[]
                 {
                     (KeysoundInspector.SelectionCard, "Selection", SelectionBody),
                     (KeysoundInspector.TracksCard, "Tracks", TracksBody),
                     (KeysoundInspector.InstrumentCard, "Instrument", InstrumentBody),
                     (KeysoundInspector.MidiCard, "MIDI input", MidiBody),
                     (KeysoundInspector.BookmarksCard, "Bookmarks", BookmarksBody),
                 })
        {
            CardBodies.Children.Remove(body);
            Side.AddCard(id, title, body);
        }

        InspectorToggle.IsCheckedChanged += (_, _) => ShowInspector(InspectorToggle.IsChecked == true);
        Side.CloseRequested += () => InspectorToggle.IsChecked = false;
    }

    private double inspectorWidth = InspectorWidth;

    // Open or closed, as the Hitsound inspector: the splitter resizes it, and closing keeps its width for next time.
    private void ShowInspector(bool open)
    {
        if (!open && Layout.ColumnDefinitions[0].ActualWidth > 0) inspectorWidth = Layout.ColumnDefinitions[0].ActualWidth;
        Side.IsVisible = InspectorSplitter.IsVisible = open;
        Layout.ColumnDefinitions[0].Width = new GridLength(open ? Math.Clamp(inspectorWidth, 160, 600) : 0);
        Layout.ColumnDefinitions[1].Width = new GridLength(open ? 4 : 0);
    }

    /// <summary>The cards' order, the folded ones and whether the inspector shows, for the preferences.</summary>
    public (IReadOnlyList<string> Order, IReadOnlyList<string> Folded, bool Open) InspectorLayout =>
        (Side.Order, Side.Folded, narrowLayout ? wideInspectorOpen : InspectorToggle.IsChecked == true);

    public void SetInspectorLayout(IEnumerable<string> order, IEnumerable<string> folded, bool open)
    {
        // The Selection card replaced the selection bar: it starts at the top unless a saved layout already placed it, as in
        // the Hitsound inspector.
        var cards = order.ToList();
        if (cards.Count > 0 && !cards.Contains(KeysoundInspector.SelectionCard)) cards.Insert(0, KeysoundInspector.SelectionCard);
        Side.SetLayout(cards, folded);
        wideInspectorOpen = open;
        if (!narrowLayout) InspectorToggle.IsChecked = open;
        ShowInspector(InspectorToggle.IsChecked == true);
    }
}
