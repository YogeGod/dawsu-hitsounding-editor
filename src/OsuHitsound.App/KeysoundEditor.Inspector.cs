using Avalonia.Controls;

namespace OsuHitsound.App;

/// <summary>The inspector cards: Tracks, Instrument and MIDI input, folded, moved and shown or hidden as in Hitsound mode.</summary>
public partial class KeysoundEditor
{
    private const double InspectorWidth = 250;

    private void SetupInspector()
    {
        // The card bodies are laid out in XAML (their controls keep their names); the cards take them over.
        foreach (var (id, title, body) in new (string, string, Control)[]
                 {
                     (KeysoundInspector.TracksCard, "Tracks", TracksBody),
                     (KeysoundInspector.InstrumentCard, "Instrument", InstrumentBody),
                     (KeysoundInspector.MidiCard, "MIDI input", MidiBody),
                 })
        {
            CardBodies.Children.Remove(body);
            Side.AddCard(id, title, body);
        }

        InspectorToggle.IsCheckedChanged += (_, _) => ShowInspector(InspectorToggle.IsChecked == true);
        Side.CloseRequested += () => InspectorToggle.IsChecked = false;
    }

    private void ShowInspector(bool open)
    {
        Side.IsVisible = open;
        Layout.ColumnDefinitions[0].Width = new GridLength(open ? InspectorWidth : 0);
        Layout.ColumnDefinitions[1].Width = new GridLength(open ? 4 : 0);
    }

    /// <summary>The cards' order, the folded ones and whether the inspector shows, for the preferences.</summary>
    public (IReadOnlyList<string> Order, IReadOnlyList<string> Folded, bool Open) InspectorLayout =>
        (Side.Order, Side.Folded, InspectorToggle.IsChecked == true);

    public void SetInspectorLayout(IEnumerable<string> order, IEnumerable<string> folded, bool open)
    {
        Side.SetLayout(order, folded);
        InspectorToggle.IsChecked = open;
        ShowInspector(open);
    }
}
