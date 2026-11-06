namespace OsuHitsound.Lanes;

public sealed partial class EditSession
{
    /// <summary>Retains exact timing/anchors; velocity is baked into the WAV, so its layer plays at 100%.</summary>
    public IReadOnlyList<EditHit> ApplyDrumRender(IReadOnlyDictionary<EditHit, Lane> assignments, Action? undoLayout = null, Action? redoLayout = null, IReadOnlySet<Lane>? alreadyRendered = null)
    {
        if (IsGestureActive) throw new InvalidOperationException("Finish the current edit before rendering drums.");
        if (assignments.Keys.Any(h => !State.Hits.Contains(h))) throw new InvalidOperationException("The hits changed while drums were rendered.");
        IReadOnlyList<EditHit> placed = [];
        int Gain(EditHit hit) => alreadyRendered?.Contains(hit.Lane) == true ? VolumeOf(hit) : 100;
        var changed = assignments.Where(p => p.Key.Lane != p.Value || VolumeOf(p.Key) != Gain(p.Key)).ToDictionary();
        if (changed.Count == 0) return assignments.Keys.ToArray();
        Change("Render VST drums", s => PlaceLayers(s, changed.Select(p =>
            (p.Key with { Lane = p.Value, SetPlacement = Guid.Empty, SetMember = Guid.Empty }, (int?)Gain(p.Key))), changed.Keys.ToHashSet(), out placed), undoLayout, redoLayout);
        return placed;
    }
}
