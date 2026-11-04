using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed partial class ArrangeView
{
    private ArrangeAutomationPeer? automation;

    protected override AutomationPeer OnCreateAutomationPeer() => automation ??= new ArrangeAutomationPeer(this);

    private void RefreshAutomation() => automation?.Refresh();

    private void OnAccessibleModelChanged()
    {
        InvalidateVisual();
        RefreshAutomation();
    }

    internal void FocusLane(Lane lane)
    {
        if (Model is not { } model) return;
        // Issue navigation must also work through a filter or a collapsed group.
        if (!model.Rows.OfType<LaneRow>().Any(r => r.Lane == lane)) model.SetFilter("");
        var row = model.LaneRows.FirstOrDefault(r => r.Lane == lane);
        if (row is null) return;
        if (model.IsCollapsed(row.Group)) model.ToggleCollapsed(row.Group);
        model.SetActive(lane);
        int index = model.Rows.ToList().FindIndex(r => r is LaneRow l && l.Lane == lane);
        if (index >= 0)
        {
            double top = RowTop(index), visible = Math.Max(RowHeight, Bounds.Height - HeaderHeight);
            if (top < ScrollY) ScrollY = top;
            else if (top + RowHeight > ScrollY + visible) ScrollY = top + RowHeight - visible;
        }
        AutomationProperties.SetName(this, $"Hitsound grid. Active lane: {model.NameOf(lane)}");
        Focus();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Model is { } model)
        {
            var rows = model.Rows.OfType<LaneRow>().ToList();
            var active = model.ActiveLane ?? rows.FirstOrDefault()?.Lane;
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && active is not null)
            {
                switch (e.Key)
                {
                    case Key.Up or Key.Down:
                        int at = rows.FindIndex(r => r.Lane == active);
                        if (rows.Count > 0) FocusLane(rows[Math.Clamp(at + (e.Key == Key.Up ? -1 : 1), 0, rows.Count - 1)].Lane);
                        break;
                    case Key.P: PreviewRequested?.Invoke(active); break;
                    case Key.M: model.ToggleMute(active); break;
                    case Key.S: model.ToggleSolo(active); break;
                    default: base.OnKeyDown(e); return;
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift && active is not null)
            {
                if (model.LaneRows.FirstOrDefault(r => r.Lane == active) is { } row) LaneMenuRequested?.Invoke(row);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    private sealed class ArrangeAutomationPeer(ArrangeView view) : ControlAutomationPeer(view)
    {
        public void Refresh() => InvalidateChildren();
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
        protected override string GetNameCore() => view.Model?.ActiveLane is { } lane
            ? $"Hitsound grid. Active lane: {view.Model.NameOf(lane)}" : "Hitsound grid";
        protected override string GetHelpTextCore() => "Ctrl+Alt+Up or Down chooses a lane. Ctrl+Alt+P previews it; Ctrl+Alt+M mutes it; Ctrl+Alt+S solos it. Shift+F10 opens lane actions. F6 returns here from other controls.";
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => view.Model is { } model
            ? model.Rows.Select((row, index) => (AutomationPeer)new RowPeer(view, this, row, index)).ToArray() : [];
    }

    private sealed class RowPeer(ArrangeView view, AutomationPeer parent, Row row, int index) : ControlAutomationPeer(view), IInvokeProvider
    {
        protected override AutomationPeer GetParentCore() => parent;
        protected override AutomationControlType GetAutomationControlTypeCore() => row is GroupRow ? AutomationControlType.Button : AutomationControlType.ListItem;
        protected override string GetNameCore() => row is LaneRow lane
            ? $"{view.Model!.NameOf(lane.Lane)}, {view.Model.Session.State.Hits.Count(h => h.Lane == lane.Lane)} hits"
            : $"{ArrangeModel.GroupName(row.Group)}, {(view.Model!.IsCollapsed(row.Group) ? "collapsed" : "expanded")}";
        protected override Rect GetBoundingRectangleCore()
        {
            var bounds = parent.GetBoundingRectangle();
            return new Rect(bounds.X, bounds.Y + view.HeaderHeight + view.RowTop(index) - view.ScrollY, Math.Min(GridLeft, bounds.Width), view.HeightOf(row));
        }
        protected override bool IsOffscreenCore() => view.HeaderHeight + view.RowTop(index) - view.ScrollY is var y && (y < view.HeaderHeight || y >= view.Bounds.Height);
        protected override bool HasKeyboardFocusCore() => view.IsFocused && row is LaneRow l && view.Model!.ActiveLane == l.Lane;
        protected override void SetFocusCore() { if (row is LaneRow l) view.FocusLane(l.Lane); else view.Focus(); }
        public void Invoke()
        {
            if (row is LaneRow l) view.FocusLane(l.Lane);
            else view.Model!.ToggleCollapsed(row.Group);
        }
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
        {
            if (row is not LaneRow l || view.Model is not { } model) return [];
            var lane = l.Lane;
            var children = new List<AutomationPeer>
            {
                new ActionPeer(view, this, "Preview " + model.NameOf(lane), () => view.PreviewRequested?.Invoke(lane)),
                new TogglePeer(view, this, "Mute " + model.NameOf(lane), () => model.ToggleMute(lane), () => model.IsMuted(lane)),
                new TogglePeer(view, this, "Solo " + model.NameOf(lane), () => model.ToggleSolo(lane), () => model.IsSoloed(lane)),
                new ActionPeer(view, this, "Select hits in " + model.NameOf(lane), () => { view.FocusLane(lane); view.Edits.Select(model.Session.State.Hits.Where(h => h.Lane == lane)); }),
                new ActionPeer(view, this, "More actions for " + model.NameOf(lane), () => view.LaneMenuRequested?.Invoke(l)),
            };
            foreach (var hit in model.Session.State.Hits.Where(h => h.Lane == lane && h.Time >= view.Axis.Left && h.Time <= view.Axis.Left + view.Axis.VisibleDuration))
                children.Add(new ActionPeer(view, this, $"Hit at {hit.Time:0.###} milliseconds, volume {model.Session.VolumeOf(hit)} percent",
                    () => { view.FocusLane(lane); view.Edits.Select([hit]); }));
            return children;
        }
    }

    private class ActionPeer(ArrangeView view, AutomationPeer parent, string name, Action action) : ControlAutomationPeer(view), IInvokeProvider
    {
        protected override AutomationPeer GetParentCore() => parent;
        protected override string GetNameCore() => name;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override Rect GetBoundingRectangleCore() => parent.GetBoundingRectangle();
        protected override bool IsOffscreenCore() => parent.IsOffscreen();
        protected override void SetFocusCore() => parent.SetFocus();
        protected override bool HasKeyboardFocusCore() => false;
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];
        public void Invoke() { EnsureEnabled(); action(); }
    }

    private sealed class TogglePeer(ArrangeView view, AutomationPeer parent, string name, Action toggle, Func<bool> isOn)
        : ActionPeer(view, parent, name, toggle), IToggleProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
        public ToggleState ToggleState => isOn() ? ToggleState.On : ToggleState.Off;
        public void Toggle() => Invoke();
    }
}
