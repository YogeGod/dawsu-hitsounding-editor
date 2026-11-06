using System.Collections.Immutable;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// What a slider's body plays, as osu! reads it from the slider's own fields: the slide (and its ticks) in
/// <see cref="Normal"/>, the slide whistle in <see cref="Addition"/> when <see cref="Whistle"/> is on. None means
/// "Auto": the set comes from the timing point (and for additions, from the normal set).
/// </summary>
public sealed record SlideSound(SampleSet Normal, SampleSet Addition, bool Whistle);

public sealed partial class EditSession
{
    private ImmutableDictionary<int, SlideSound>? passiveFor;
    private IReadOnlyList<PassiveSound> passiveNow = [];

    /// <summary>What slider <paramref name="objectIndex"/>'s body plays now: the edit, or what the map says.</summary>
    public SlideSound SlideOf(int objectIndex) => SlideOf(State, objectIndex);

    private SlideSound SlideOf(EditState state, int objectIndex)
    {
        if (state.Slides.TryGetValue(objectIndex, out var edited)) return edited;
        var slider = sourceFile.HitObjects[objectIndex];
        var own = slider.HitSample ?? default;
        return new(own.NormalSet, own.AdditionSet, slider.HitSound.HasFlag(HitSound.Whistle));
    }

    /// <summary>Changes the body sound of several sliders in one undo step; objects that aren't sliders are skipped.</summary>
    public void SetSlides(IEnumerable<int> objectIndexes, Func<SlideSound, SlideSound> change, string label = "Slide sound") => Change(label, s =>
    {
        var slides = s.Slides;
        foreach (int i in objectIndexes.Distinct())
        {
            if (i < 0 || i >= sourceFile.HitObjects.Count || sourceFile.HitObjects[i].Kind != HitObjectKind.Slider) continue;
            var original = SlideOf(s with { Slides = ImmutableDictionary<int, SlideSound>.Empty }, i);
            var next = change(SlideOf(s, i));
            slides = next == original ? slides.Remove(i) : slides.SetItem(i, next);
        }

        return ReferenceEquals(slides, s.Slides) ? s : s with { Slides = slides };
    });

    /// <summary>
    /// Writes the slide edits into a copy of the map: the slider's own sample sets and its whistle flag. Its nodes'
    /// sounds and sets are first written out in full, since nodes without their own entries fall back to those fields.
    /// </summary>
    internal static void ApplySlides(EditState state, IReadOnlyList<HitObject> objects)
    {
        foreach (var (index, slide) in state.Slides)
        {
            if (index >= objects.Count || objects[index] is not { Kind: HitObjectKind.Slider } slider) continue;
            var sounds = slider.NodeSounds;
            var sets = slider.NodeSets;
            for (int node = 0; node < sounds.Length; node++)
            {
                slider.SetNodeSound(node, sounds[node]);
                slider.SetNodeSet(node, sets[node].Normal, sets[node].Addition);
            }

            slider.HitSample = (slider.HitSample ?? default) with { NormalSet = slide.Normal, AdditionSet = slide.Addition };
            slider.HitSound = slide.Whistle ? slider.HitSound | HitSound.Whistle : slider.HitSound & ~HitSound.Whistle;
        }
    }

    /// <summary>The slider body, whistle and tick sounds with the slide edits applied (re-read only for edited sliders).</summary>
    private IReadOnlyList<PassiveSound> PassiveNow()
    {
        if (State.Slides.Count == 0) return baseMap.Passive;
        if (ReferenceEquals(passiveFor, State.Slides)) return passiveNow;
        var copy = OsuFile.Parse(sourceFile.ToBytes());
        ApplySlides(State, copy.HitObjects);
        var edited = State.Slides.Keys.Select(i => sourceFile.HitObjects[i]).ToHashSet();
        var copies = copy.HitObjects.Select((o, i) => (o, i)).ToDictionary(p => p.o, p => sourceFile.HitObjects[p.i]);
        var reread = Decompiler.Decompile(copy, sampleFolder).Passive
            .Where(p => p.Object is { } o && copies.TryGetValue(o, out var source) && edited.Contains(source))
            .Select(p => p with { Object = copies[p.Object!] });
        passiveNow = [.. baseMap.Passive.Where(p => p.Object is null || !edited.Contains(p.Object)).Concat(reread).OrderBy(p => p.Time)];
        passiveFor = State.Slides;
        return passiveNow;
    }
}
