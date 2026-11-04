using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed class SoundSetPanel : StackPanel
{
    private readonly ComboBox sets = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = Brushes.Gainsboro };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Add", "Replace (confirm)" }, SelectedIndex = 0, Foreground = Brushes.Gainsboro };
    private readonly ComboBox members = new() { Foreground = Brushes.Gainsboro, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBox gain = new() { Text = "100", Width = 55, Foreground = Brushes.Gainsboro }, memberVolume = new() { Text = "100", Width = 55, Foreground = Brushes.Gainsboro };
    private List<Guid> choices = [];
    private List<Guid> memberIds = [];
    private bool filling;
    public Guid? SelectedSet => sets.SelectedIndex is > 0 and var i && i <= choices.Count ? choices[i - 1] : null;
    public Guid? Placement { get; private set; }
    public Guid? Member => members.SelectedIndex is >= 0 and var i && i < memberIds.Count ? memberIds[i] : null;
    public bool Replace => mode.SelectedIndex == 1;
    public int Gain => int.TryParse(gain.Text, out int value) ? Math.Clamp(value, 0, 100) : 100;
    public int MemberVolume => int.TryParse(memberVolume.Text, out int value) ? Math.Clamp(value, 0, 100) : 100;
    public event Action<string>? Command;
    public event Action? ChoiceChanged;
    public SoundSetPanel()
    {
        Spacing = 6;
        sets.ItemsSource = new[] { "Single sample" }; sets.SelectedIndex = 0;
        sets.SelectionChanged += (_, _) => { if (!filling) ChoiceChanged?.Invoke(); };
        members.SelectionChanged += (_, _) => { if (!filling) ChoiceChanged?.Invoke(); };
        Button Action(string label, string command)
        { var button = new Button { Content = label }; button.Click += (_, _) => Command?.Invoke(command); return button; }
        WrapPanel Row(params Control[] controls) { var row = new WrapPanel { Orientation = Orientation.Horizontal }; row.Children.AddRange(controls); return row; }
        Children.AddRange([sets, mode, Row(Action("Save selection…", "capture"), Action("New set…", "new")),
            Row(Action("Edit set…", "edit"), Action("Apply", "apply"), Action("Preview", "preview")),
            Row(Action("Save to library", "librarySave"), Action("Import…", "import"), Action("Update…", "update")),
            info, Row(new TextBlock { Text = "Set volume %", VerticalAlignment = VerticalAlignment.Center }, gain, Action("Set", "gain")),
            members, Row(new TextBlock { Text = "Sound %", VerticalAlignment = VerticalAlignment.Center }, memberVolume, Action("Set sound", "member")),
            Row(Action("Select whole set", "select"), Action("Reset to set", "reset")), Action("Make independent", "detach"), Action("Review output…", "review")]);
    }
    public void Choose(Guid id) { sets.SelectedIndex = choices.IndexOf(id) + 1; }
    public void Refresh(EditSession? session, IReadOnlyCollection<EditHit> selected)
    {
        filling = true;
        try
        {
            var chosen = SelectedSet;
            var definitions = session?.State.SoundSets.ToList() ?? [];
            var ids = definitions.Select(s => s.Id).ToList();
            if (!ids.SequenceEqual(choices) || !sets.Items.Cast<string>().Skip(1).SequenceEqual(definitions.Select(s => s.Name)))
            { choices = ids; sets.ItemsSource = new[] { "Single sample" }.Concat(definitions.Select(s => s.Name)).ToList(); sets.SelectedIndex = chosen is { } id && choices.Contains(id) ? choices.IndexOf(id) + 1 : 0; }
            var placement = session?.State.SoundPlacements.FirstOrDefault(p => selected.Any(h => h.SetPlacement == p.Id));
            Placement = placement?.Id;
            var set = placement is null ? null : definitions.FirstOrDefault(s => s.Id == placement.SetId);
            var previous = Member;
            memberIds = set?.Members.Select(m => m.Id).ToList() ?? [];
            members.ItemsSource = set?.Members.Select(m => m.Lane.Name + (placement!.Overrides.GetValueOrDefault(m.Id)?.Shared == true ? " (shared)" : placement.Overrides.GetValueOrDefault(m.Id)?.Removed == true ? " (removed here)" : "")).ToList() ?? [];
            members.SelectedIndex = previous is { } member && memberIds.Contains(member) ? memberIds.IndexOf(member) : memberIds.Count > 0 ? 0 : -1;
            if (!gain.IsFocused) gain.Text = (placement?.Gain ?? 100).ToString();
            if (!memberVolume.IsFocused && Member is { } mid) memberVolume.Text = (placement!.Overrides.GetValueOrDefault(mid)?.Volume ?? set!.Members.Single(m => m.Id == mid).Volume).ToString();
            info.Text = set is null ? "Choose a set, then Draw. Apply uses selected times or the playhead." : $"{set.Name}: {placement!.Overrides.Values.Count(o => o.Shared)} shared sound(s). Shared sounds follow the first set's volume and edits. Other changes affect this placement only.";
        }
        finally { filling = false; }
    }
}
