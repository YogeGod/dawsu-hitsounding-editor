using OsuHitsound.Lanes;

namespace OsuHitsound.App;

internal sealed partial class ArrangeModel
{
    internal static bool CanChooseChannelSample(Lane lane) => lane.Source == LaneSource.BeatmapFile ||
        lane.Source == LaneSource.Skin && lane.Sample?.Sound is "hitnormal" or "hitwhistle" or "hitfinish" or "hitclap";

    private sealed record SampleLaneState(bool Muted, bool Soloed, bool Hidden, string? Name);

    internal bool ReplaceSample(Lane original, Lane replacement)
    {
        if (Session.IsGestureActive || original == replacement || !CanReplaceSample(original) || !CanReplaceSample(replacement) || !CanChooseChannelSample(replacement)) return false;
        SampleLaneState Capture(Lane lane) => new(IsMuted(lane), IsSoloed(lane),
            hidden.Contains(lane) || !laneRows.Any(r => r.Lane == lane), names.GetValueOrDefault(lane));
        var originalBefore = Capture(original);
        var replacementBefore = Capture(replacement);
        var activeBefore = ActiveLane;
        string display = NameOf(original);
        AddLane(replacement);

        void Restore(SampleLaneState a, SampleLaneState b, Lane? active) => Relayout(() =>
        {
            Apply(original, a);
            Apply(replacement, b);
            ActiveLane = active;
        });
        void Apply(Lane lane, SampleLaneState state)
        {
            lock (muted)
            {
                if (state.Muted) muted.Add(lane); else muted.Remove(lane);
                if (state.Soloed) soloed.Add(lane); else soloed.Remove(lane);
            }
            if (state.Hidden) hidden.Add(lane); else hidden.Remove(lane);
            if (state.Name is { } name) names[lane] = name; else names.Remove(lane);
        }

        Session.RetargetLane(original, replacement,
            () => Restore(originalBefore, replacementBefore, activeBefore),
            () => Restore(originalBefore with { Soloed = false, Hidden = true },
                replacementBefore with { Muted = originalBefore.Muted || replacementBefore.Muted,
                    Soloed = originalBefore.Soloed || replacementBefore.Soloed, Hidden = false, Name = display }, replacement));
        return true;
    }

    internal sealed record ViewState(IReadOnlyList<Lane> Muted, IReadOnlyList<Lane> Soloed,
        IReadOnlyList<LaneGroup> Collapsed, IReadOnlyList<string> CollapsedFamilies, bool SongSoloed, string Filter);

    internal ViewState CaptureViewState()
    {
        lock (muted) return new(muted.ToList(), soloed.ToList(), collapsed.ToList(), collapsedFamilies.ToList(), SongSoloed, Filter);
    }

    internal void RestoreViewState(ViewState state) => Relayout(() =>
    {
        lock (muted)
        {
            muted.Clear(); muted.UnionWith(state.Muted);
            soloed.Clear(); soloed.UnionWith(state.Soloed);
        }
        collapsed.Clear(); collapsed.UnionWith(state.Collapsed);
        collapsedFamilies.Clear(); collapsedFamilies.UnionWith(state.CollapsedFamilies);
        SongSoloed = state.SongSoloed;
        Filter = state.Filter;
    });
}
