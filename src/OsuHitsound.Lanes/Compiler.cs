using System.Globalization;
using System.Text.RegularExpressions;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>A hit position: an object (by its place in [HitObjects]) and, for sliders, the node (0 = head).</summary>
public readonly record struct MomentKey(int ObjectIndex, int Node);

/// <summary>What should play at a moment: the lanes, and the one volume they share.</summary>
public sealed record WantedSound(IReadOnlyList<Lane> Lanes, int Volume);

/// <summary>A moment that can't be saved as wanted. It keeps what it played before.</summary>
public sealed record Conflict(double Time, MomentKey Moment, string Reason);

/// <summary>
/// The outcome of <see cref="Compiler.Apply"/>: conflicts, notes about side effects (slider bodies and ticks that
/// follow a new green line), and how many timing points were added or changed.
/// </summary>
public sealed record CompileResult(IReadOnlyList<Conflict> Conflicts, IReadOnlyList<string> Notes, int TimingPointsAdded, int TimingPointsChanged);

/// <summary>
/// Lanes → .osu, Apply mode (docs/osu-hitsound-rules.md, "Compile"): objects stay where they are and only hitsound
/// fields change. For each moment, in time order, the cheapest encoding wins:
/// <list type="number">
/// <item>sound flags and sample sets on the object (or slider node), keeping what already decodes right;</item>
/// <item>circles and spinners: their own index and volume;</item>
/// <item>slider nodes: a green line at the node, copying the slider velocity and effects in force; the next moment
/// that needs the old settings gets its own line back.</item>
/// </list>
/// Moments that can't be encoded become conflicts and keep their old sounds. The filename field is never written.
/// The result is checked by decompiling again: any moment that doesn't come back as wanted is a conflict too.
/// </summary>
public static partial class Compiler
{
    private static readonly (HitSound Flag, string Sound)[] Additions =
        [(HitSound.Whistle, "hitwhistle"), (HitSound.Finish, "hitfinish"), (HitSound.Clap, "hitclap")];

    /// <summary>The key of a moment of <paramref name="file"/> (a linear search: for one-off lookups, not loops).</summary>
    public static MomentKey KeyOf(OsuFile file, Moment moment) => new(IndexOf(file, moment.Object), moment.Node);

    /// <summary>The moment's sounds as they are, as a wish (lanes and volume).</summary>
    public static WantedSound AsWanted(Moment moment) => new([.. moment.Hits.Select(h => h.Lane)], moment.Volume);

    /// <summary>
    /// Changes <paramref name="file"/> in place so each moment in <paramref name="wanted"/> plays those lanes; moments
    /// not listed keep what they play now. Pass a copy (OsuFile.Parse(file.ToBytes())) to keep the original.
    /// </summary>
    public static CompileResult Apply(OsuFile file, MapFolder folder, IReadOnlyDictionary<MomentKey, WantedSound> wanted) =>
        new Run(file, folder, wanted).Apply();

    private static int IndexOf(OsuFile file, HitObject obj)
    {
        for (int i = 0; i < file.HitObjects.Count; i++)
            if (ReferenceEquals(file.HitObjects[i], obj)) return i;
        throw new ArgumentException("The moment's object isn't in this file.", nameof(obj));
    }

    /// <summary>A lane that is a set-named hitsound: e.g. drum-hitclap2.ogg → (Drum, "hitclap", 2); skin soft-hitclap → index 0.</summary>
    private readonly record struct Parsed(Lane Lane, SampleSet Set, string Sound, int Index)
    {
        public bool IsSkin => Index == 0;
    }

    private static readonly string[] HitSounds = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];

    private static Parsed? Parse(Lane lane)
    {
        if (lane.Missing || lane.Source == LaneSource.StoryboardFile) return null;
        var name = lane.Source == LaneSource.Skin ? lane.Name : Path.GetFileNameWithoutExtension(lane.Name);
        if (lane.Source == LaneSource.BeatmapFile && (lane.Name.Contains('/') || !HitsoundRules.SampleExtensions.Contains(Path.GetExtension(lane.Name).ToLowerInvariant())))
            return null;
        // Index 1 has no number; "soft-hitclap1" or "soft-hitclap0" are only reachable through the filename field.
        if (HitsoundRules.ParseSampleName(name) is not (var set, var sound, var index) || !HitSounds.Contains(sound)) return null;
        if (lane.Source == LaneSource.Skin) return index == 1 && string.Equals(name, HitsoundRules.SampleName(set, sound), StringComparison.OrdinalIgnoreCase) ? new Parsed(lane, set, sound, 0) : null;
        return new Parsed(lane, set, sound, index);
    }

    private sealed class Run(OsuFile file, MapFolder folder, IReadOnlyDictionary<MomentKey, WantedSound> wanted)
    {
        private readonly MapSettings settings = MapSettings.Read(file);
        private readonly List<Conflict> conflicts = [];
        private readonly HashSet<MomentKey> conflicted = [];
        private readonly Dictionary<HitObject, int> objectIndex = file.HitObjects.Select((o, i) => (o, i)).ToDictionary(x => x.o, x => x.i); // HitObject compares by reference
        private TimingPointIndex timing = new(file.TimingPoints);
        private bool timingChanged;
        private int added, changed;

        private MomentKey Key(Moment m) => new(objectIndex[m.Object], m.Node);

        public CompileResult Apply()
        {
            var original = Decompiler.Decompile(file, folder);
            var wish = original.Moments.ToDictionary(Key, m => wanted.GetValueOrDefault(Key(m)) ?? AsWanted(m));

            double previousLookup = double.NegativeInfinity;
            foreach (var before in original.Moments)
            {
                // Only timing points change as moments are encoded, so a moment is reread only after a green line.
                var key = Key(before);
                var moment = timingChanged ? Decompiler.MomentNow(file, folder, settings, timing, before) : before;
                double time = moment.Time - settings.TimeOffset;
                try
                {
                    if (!Same(moment, wish[key])) Encode(key, moment, time, wish[key], previousLookup);
                }
                catch (ConflictException ex)
                {
                    conflicts.Add(new Conflict(moment.Time, key, ex.Message));
                    conflicted.Add(key);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException)
                {
                    // A line that can't be read or safely rewritten (e.g. not UTF-8): its first write throws before changing it.
                    conflicts.Add(new Conflict(moment.Time, key, $"This object's line can't be edited: {ex.Message}"));
                    conflicted.Add(key);
                }

                previousLookup = Math.Max(previousLookup, time + HitsoundRules.ControlPointLeniency);
            }

            // Check by decompiling the result: every moment must come back as wanted.
            var result = Decompiler.Decompile(file, folder);
            foreach (var m in result.Moments)
            {
                var key = Key(m);
                if (conflicted.Contains(key) || Same(m, wish[key])) continue;
                conflicts.Add(new Conflict(m.Time, key, $"Came out as {Describe(m)} instead of what was wanted; please report this."));
            }

            var notes = new List<string>();
            int bodyChanges = PassiveChanges(original, result);
            if (bodyChanges > 0)
                notes.Add($"{bodyChanges} slider body or tick sound{(bodyChanges == 1 ? "" : "s")} changed with the new green lines (osu! reads them from the same timing point).");

            return new CompileResult([.. conflicts.OrderBy(c => c.Time)], notes, added, changed);
        }


        private void Encode(MomentKey key, Moment moment, double time, WantedSound want, double previousLookup)
        {
            if (want.Volume is < 0 or > 100) throw new ConflictException($"Volume {want.Volume} is outside osu!'s 0-100.");
            var parsed = want.Lanes.Distinct().Select(l => (Lane: l, Parsed: Parse(l))).ToList();
            if (parsed.FirstOrDefault(p => p.Parsed is null) is { Lane: { } own })
                throw new ConflictException($"\"{own.Name}\" isn't a set-named hitsound, so osu! could only play it through the filename field, which this editor never writes.");

            var sounds = parsed.Select(p => p.Parsed!.Value).ToList();
            var normals = sounds.Where(s => s.Sound == "hitnormal").ToList();
            var additions = sounds.Where(s => s.Sound != "hitnormal").ToList();
            if (normals.Count == 0) throw new ConflictException("No hitnormal: osu! always plays one.");
            if (normals.Count > 1) throw new ConflictException("Two hitnormals at once: osu! plays only one.");
            if (additions.GroupBy(a => a.Sound).Any(g => g.Count() > 1)) throw new ConflictException("The same addition twice: osu! plays each addition once.");
            if (additions.Select(a => a.Set).Distinct().Count() > 1) throw new ConflictException("Additions from two sample sets at once: osu! plays all additions from one set.");
            var indices = sounds.Where(s => !s.IsSkin).Select(s => s.Index).Distinct().ToList();
            if (indices.Count > 1) throw new ConflictException($"Needs index {string.Join(" and ", indices)} at once: all sounds of a moment share one index.");

            var normalSet = normals[0].Set;
            SampleSet? additionSet = additions.Count > 0 ? additions[0].Set : null;
            var flags = (moment.Object.Kind == HitObjectKind.Slider ? moment.Object.NodeSounds[moment.Node] : moment.Object.HitSound) & HitSound.Normal;
            foreach (var (flag, sound) in Additions)
                if (additions.Any(a => a.Sound == sound)) flags |= flag;

            bool Works(int index) => sounds.All(s => Resolve(s.Set, s.Sound, index) == s.Lane);
            int? fixedIndex = indices.Count == 1 ? indices[0] : null;
            if (fixedIndex is { } fi && !Works(fi))
                throw new ConflictException($"Index {fi} can't play these together: another file with that index would play instead of a skin sound.");

            var obj = moment.Object;
            var point = Decompiler.PointState(timing.SamplePointAt(time), settings);
            if (obj.Kind is HitObjectKind.Circle or HitObjectKind.Spinner)
            {
                var hs = obj.HitSample ?? default;
                if (hs.Filename is not null)
                    throw new ConflictException("This object plays a custom file (filename field), which this editor never writes.");

                int effective = hs.Index > 0 ? hs.Index : point.Index;
                int indexField = Works(effective) ? hs.Index
                    : Works(point.Index) ? 0
                    : FirstWorkingIndex(Works, fixedIndex, allowZero: false) ?? throw new ConflictException("No index plays these sounds together.");

                int effectiveVolume = hs.Volume > 0 ? Math.Min(hs.Volume, 100) : point.Volume;
                int volumeField = effectiveVolume == want.Volume ? hs.Volume
                    : point.Volume == want.Volume ? 0
                    : want.Volume >= 1 ? want.Volume
                    : throw new ConflictException("Volume 0 on an object means \"use the timing point's\", which is different here.");

                var (normalField, additionField) = Banks(hs.NormalSet, hs.AdditionSet, normalSet, additionSet, point.Set);
                obj.HitSound = flags;
                obj.HitSample = hs with { NormalSet = normalField, AdditionSet = additionField, Index = indexField, Volume = volumeField };
                return;
            }

            // Slider nodes: index and volume come from the timing point only.
            if (!Works(point.Index) || point.Volume != want.Volume)
            {
                int index = Works(point.Index) ? point.Index
                    : FirstWorkingIndex(Works, fixedIndex, allowZero: true) ?? throw new ConflictException("No index plays these sounds together.");
                if (want.Volume < 1) throw new ConflictException("Volume 0 can't be set on a timing point.");
                SetGreenLine(time, previousLookup, index, want.Volume);
                point = Decompiler.PointState(timing.SamplePointAt(time), settings);
            }

            var (n, a) = obj.NodeSets[moment.Node];
            var (newNormal, newAddition) = Banks(n, a, normalSet, additionSet, point.Set);
            if ((newNormal, newAddition) != (n, a)) obj.SetNodeSet(moment.Node, newNormal, newAddition);
            obj.SetNodeSound(moment.Node, flags);
        }

        /// <summary>
        /// Banks to write: keep the current fields when they already decode to the wanted sets; otherwise write the set,
        /// or None (inherit) when it would be inherited anyway.
        /// </summary>
        private static (SampleSet Normal, SampleSet Addition) Banks(SampleSet normalField, SampleSet additionField, SampleSet normal, SampleSet? addition, SampleSet pointSet)
        {
            var newNormal = Choose(normalField, normal, pointSet);
            if (addition is not { } add) return (newNormal, additionField);
            return (newNormal, Choose(additionField, add, Decompiler.DecodeSet(newNormal, pointSet)));
        }

        private static SampleSet Choose(SampleSet field, SampleSet wanted, SampleSet inherited) =>
            Decompiler.DecodeSet(field, inherited) == wanted ? field : wanted == inherited ? SampleSet.None : wanted;

        private static int? FirstWorkingIndex(Func<int, bool> works, int? fixedIndex, bool allowZero)
        {
            if (fixedIndex is { } f) return works(f) ? f : null;
            if (allowZero && works(0)) return 0;
            // All skin sounds: any index whose files are all missing plays the skin.
            for (int i = 1; i <= 200; i++)
                if (works(i)) return i;
            return null;
        }

        private Lane Resolve(SampleSet set, string sound, int index) => Decompiler.ResolveLane(folder, set, sound, index);

        /// <summary>
        /// Sets index and volume for sounds from <paramref name="time"/> on, with a green line no earlier moment reads:
        /// at the node time, or just after the previous moment's lookup when that is within 5 ms.
        /// </summary>
        private void SetGreenLine(double time, double previousLookup, int index, int volume)
        {
            // Whole milliseconds, as osu! writes timing points.
            double at = Math.Ceiling(Math.Max(time, Math.Floor(previousLookup) + 1));
            if (at > time + HitsoundRules.ControlPointLeniency)
                throw new ConflictException("Another sound within 5 ms reads the same timing point, so a green line here would change it too.");

            var inForce = timing.PointAt(at) ?? throw new ConflictException("No timing point before this slider to copy from.");
            if (inForce.Time == at)
            {
                // A point is already at this time and is the last one there: change it rather than adding another.
                inForce.SampleIndex = index;
                inForce.Volume = volume;
                changed++;
            }
            else
            {
                file.InsertTimingPoint(TimingPoint.FormatLine(at,
                    beatLength: inForce.Uninherited ? "-100" : inForce.BeatLengthText,
                    meter: timing.RedLineAt(at)?.Meter ?? 4,
                    // A line with no set field plays the map default; written as 0 it would play Soft.
                    sampleSet: inForce.SampleSet ?? settings.DefaultSampleSet, index, volume, uninherited: false,
                    effects: inForce.Effects & ~8)); // "omit first bar line" belongs to red lines only
                added++;
            }

            timingChanged = true;
            timing = new TimingPointIndex(file.TimingPoints);
        }

        private static bool Same(Moment moment, WantedSound want) =>
            moment.Volume == want.Volume && moment.Hits.Select(h => h.Lane).ToHashSet().SetEquals(want.Lanes);

        private static string Describe(Moment m) => $"{string.Join(" + ", m.Hits.Select(h => h.Lane.Name))} at volume {m.Volume}";

        private static int PassiveChanges(DecompiledMap before, DecompiledMap after)
        {
            static IEnumerable<(PassiveKind, double, Lane, int)> SliderSounds(DecompiledMap map) => map.Passive
                .Where(p => p.Kind is not PassiveKind.StoryboardSample)
                .Select(p => (p.Kind, p.Time, p.Lane, p.Volume));

            var now = SliderSounds(after).ToHashSet();
            return SliderSounds(before).Count(x => !now.Contains(x));
        }
    }

    private sealed class ConflictException(string message) : Exception(message);
}
