using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OsuHitsound.App;

/// <summary>
/// Type-to-jump for a list, as in file dialogs: a letter selects the next entry that starts with it (the same letter
/// again moves on to the next such entry), and letters typed in quick succession match more of the name ("odin i").
/// </summary>
internal sealed class TypeAhead
{
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1);
    private readonly ListBox list;
    private string typed = "";
    private DateTime last;

    private TypeAhead(ListBox list) => this.list = list;

    /// <summary>
    /// Typing in the list jumps in it; so does typing anywhere else in its dialog (its buttons, say) except a text box.
    /// Not in the main window, where letters are editor shortcuts.
    /// </summary>
    /// <param name="dialogWide">False for a list that shares its window with other controls taking letters (the
    /// Preferences key list beside the command tree): then only typing in the list itself jumps.</param>
    public static void Attach(ListBox list, bool dialogWide = true)
    {
        var typeAhead = new TypeAhead(list);
        void OnText(object? sender, TextInputEventArgs e)
        {
            if (e.Source is TextBox) return;
            if (typeAhead.Type(e.Text)) e.Handled = true;
        }
        // One handler, so a letter is counted once: on the dialog (which also gets what's typed in the list), or on the
        // list alone in the main window.
        InputElement? target = null;
        list.AttachedToVisualTree += (_, _) =>
        {
            target = dialogWide && TopLevel.GetTopLevel(list) is { } top and not MainWindow ? top : list;
            target.AddHandler(InputElement.TextInputEvent, OnText, RoutingStrategies.Bubble);
        };
        list.DetachedFromVisualTree += (_, _) => { target?.RemoveHandler(InputElement.TextInputEvent, OnText); target = null; };
    }

    /// <summary>Selects the entry for what has been typed so far; false when nothing matches (the selection stays).</summary>
    internal bool Type(string? text)
    {
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0])) return false;
        var now = DateTime.UtcNow;
        typed = now - last > Pause ? text : typed + text;
        last = now;
        var items = list.ItemsSource?.Cast<object>().Select(i => i?.ToString() ?? "").ToList() ?? [];
        if (items.Count == 0) return false;
        // One letter, repeated or not: the next entry after the selected one. A longer prefix: from the selected one on.
        bool repeated = typed.Length > 1 && typed.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(typed[0]));
        string prefix = repeated ? typed[..1] : typed;
        int start = list.SelectedIndex + (prefix.Length == 1 ? 1 : 0);
        for (int n = 0; n < items.Count; n++)
        {
            int i = ((start + n) % items.Count + items.Count) % items.Count;
            if (!items[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            list.SelectedIndex = i;
            list.ScrollIntoView(i);
            return true;
        }
        return false;
    }
}
