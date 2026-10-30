using OsuHitsound.Lanes;

namespace OsuHitsound.App;

/// <summary>
/// The selection and the editing commands that act on it (keyboard and menus): select, delete, nudge, move between
/// lanes, copy, cut, paste and duplicate, with snapping. Kept apart from the drawing so it can be tested on its own.
/// </summary>
internal sealed class HitEditor
{
    private ArrangeModel? model;
    private List<(Lane Lane, double Offset)> clipboard = [];

    /// <summary>The selected hits. Mouse gestures change it directly while dragging and call <see cref="Notify"/> after.</summary>
    public HashSet<EditHit> Selected { get; } = [];

    /// <summary>Raised when the selection changes through a command.</summary>
    public event Action? Changed;

    /// <summary>The open map; changing it empties the selection (the clipboard is kept, to paste into another map).</summary>
    public ArrangeModel? Model
    {
        get => model;
        set
        {
            model = value;
            Selected.Clear();
        }
    }

    /// <summary>Snap divisor (1/n of a beat) and whether snapping is on (J).</summary>
    public int Divisor { get; set; } = 4;

    public bool SnapOn { get; set; } = true;

    private EditSession? Session => model?.Session;

    public double SnapTime(double time) => SnapOn && model is { } m ? m.Snap(time, Divisor) : time;

    /// <summary>The moments the selected hits play on.</summary>
    public IReadOnlySet<MomentKey> SelectedMoments => Selected.Where(h => h.Anchor is not null).Select(h => h.Anchor!.Value).ToHashSet();

    public void Notify() => Changed?.Invoke();

    public void Select(IEnumerable<EditHit> hits)
    {
        var list = hits.ToList(); // the argument may be built from the selection itself
        Selected.Clear();
        Selected.UnionWith(list);
        Notify();
    }

    public void SelectAll()
    {
        if (Session is { } session) Select(session.State.Hits);
    }

    public void Clear() => Select([]);

    public void SelectWhere(Func<EditHit, bool> predicate)
    {
        if (Session is { } session) Select(session.State.Hits.Where(predicate));
    }

    /// <summary>Removes one hit and drops it from the selection (right-click, Draw strokes that start on a hit, Erase).</summary>
    public void Erase(EditHit hit)
    {
        Session?.Remove([hit]);
        Selected.Remove(hit);
    }

    public void DeleteSelection()
    {
        if (Session is not { } session || Selected.Count == 0) return;
        session.Remove(Selected.ToList());
        Clear();
    }

    /// <summary>Moves the selected hits and keeps them selected.</summary>
    public void Move(double offset, Func<EditHit, Lane> laneOf)
    {
        if (Session is not { } session || Selected.Count == 0) return;
        Select(session.Move(Selected.ToList(), offset, laneOf));
    }

    /// <summary>By one snap step, keeping offsets (fine: 1 ms; toGrid: to the previous or next grid line).</summary>
    public void Nudge(int direction, bool fine, bool toGrid)
    {
        if (model is null || Selected.Count == 0) return;
        var anchor = Selected.MinBy(h => h.Time)!;
        double offset;
        if (fine) offset = direction;
        else
        {
            double step = StepAt(anchor.Time);
            offset = toGrid ? model.Snap(anchor.Time + direction * step * 0.51, Divisor) - anchor.Time : direction * step;
            if (toGrid && Math.Abs(offset) < 1e-6) offset = direction * step;
        }

        Move(offset, h => h.Lane);
    }

    /// <summary>Moves the selection to the adjacent visible lane.</summary>
    public void ShiftLanes(int direction)
    {
        if (model is { } m) Move(0, h => ShiftLane(m, h.Lane, direction));
    }

    public void Copy()
    {
        if (Selected.Count == 0) return;
        double start = Selected.Min(h => h.Time);
        clipboard = [.. Selected.Select(h => (h.Lane, h.Time - start))];
    }

    public void Cut()
    {
        Copy();
        DeleteSelection();
    }

    /// <summary>Pastes at <paramref name="time"/> (snapped), selecting the pasted hits.</summary>
    public void Paste(double time)
    {
        if (Session is not { } session || clipboard.Count == 0) return;
        double at = SnapTime(time);
        var hits = clipboard.Select(c => session.HitAt(c.Lane, at + c.Offset)).Distinct().ToList();
        session.Add(hits);
        Select(hits);
    }

    /// <summary>Copies the selection right after itself (its length rounded up to a snap step). The clipboard is kept.</summary>
    public void Duplicate()
    {
        if (Session is not { } session || Selected.Count == 0) return;
        double start = Selected.Min(h => h.Time), end = Selected.Max(h => h.Time);
        double step = StepAt(start);
        double shift = Math.Max(step, Math.Ceiling((end - start + step / 2) / step) * step);
        var hits = Selected.Select(h => session.HitAt(h.Lane, h.Time + shift)).Distinct().ToList();
        session.Add(hits);
        Select(hits);
    }

    /// <summary>Drops selected hits that no longer exist (after undo or redo).</summary>
    public void Prune()
    {
        if (Session is not { } session) return;
        var present = session.State.Hits.ToHashSet();
        if (Selected.RemoveWhere(h => !present.Contains(h)) > 0) Notify();
    }

    private double StepAt(double time) => (model?.RedLineAt(time)?.BeatLength ?? 500) / Divisor;

    /// <summary>The lane <paramref name="shift"/> visible lane rows away (group rows skipped), clamped to the list.</summary>
    public static Lane ShiftLane(ArrangeModel model, Lane lane, int shift)
    {
        if (shift == 0) return lane;
        var lanes = model.Rows.OfType<LaneRow>().Select(r => r.Lane).ToList();
        int at = lanes.IndexOf(lane);
        return at < 0 ? lane : lanes[Math.Clamp(at + shift, 0, lanes.Count - 1)];
    }
}
