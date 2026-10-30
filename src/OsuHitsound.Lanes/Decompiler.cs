using OsuHitsound.Format;

namespace OsuHitsound.Lanes;

/// <summary>
/// .osu → lanes: which sound sources play at every hit position, and how loud. Implements the research note's
/// "Decompile" section with osu!stable deciding where stable and lazer differ (docs/osu-hitsound-rules.md,
/// docs/project-context.md):
/// <list type="bullet">
/// <item>Timing points are looked up 5 ms after each node (slider body: start + 6 ms; spinner: its end); among points at
/// the same time the later line wins; before the first point, the first point applies.</item>
/// <item>A timing point's sample set 0 means the map's [General] SampleSet.</item>
/// <item>A custom filename plays only that file: no hitnormal and no additions.</item>
/// <item>Slider nodes and body take index and volume from timing points only.</item>
/// <item>Index 0 is the skin; a missing custom-index file falls back to the skin, never to the un-numbered map file.</item>
/// </list>
/// </summary>
public static class Decompiler
{
    private static readonly (HitSound Flag, string Sound)[] Additions =
        [(HitSound.Finish, "hitfinish"), (HitSound.Whistle, "hitwhistle"), (HitSound.Clap, "hitclap")];

    public static DecompiledMap Decompile(string osuPath)
    {
        var folder = MapFolder.FromDirectory(Path.GetDirectoryName(Path.GetFullPath(osuPath))!);
        return Decompile(OsuFile.Read(osuPath), folder);
    }

    public static DecompiledMap Decompile(OsuFile file, MapFolder folder) => new Run(file, folder).Decompile();

    private sealed class Run(OsuFile file, MapFolder folder)
    {
        private readonly MapSettings settings = MapSettings.Read(file);
        private readonly List<string> warnings = [];
        private readonly Dictionary<Lane, Lane> lanes = [];
        private readonly Dictionary<(SampleSet, string, int), Lane> resolved = [];
        private readonly List<Moment> moments = [];
        private readonly List<PassiveSound> passive = [];
        private TimingPointIndex timing = null!; // built first in Decompile, so its warnings come first

        public DecompiledMap Decompile()
        {
            if (settings.Mode != 0)
                warnings.Add($"This map is for mode {settings.Mode}, not osu!standard; only osu!standard hitsounds are supported.");

            timing = new TimingPointIndex(file.TimingPoints, warnings);

            foreach (var obj in file.HitObjects)
            {
                try
                {
                    AddObject(obj);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
                {
                    warnings.Add($"Skipped an unreadable hit object ({ex.Message}): {obj.Line.Text}");
                }
            }

            AddStoryboard(file);
            foreach (var osb in folder.FilesWithExtension(".osb"))
            {
                try
                {
                    AddStoryboard(OsuFile.Parse(folder.ReadBytes(osb)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Couldn't read {osb}: {ex.Message}");
                }
            }

            return new DecompiledMap(
                settings,
                [.. lanes.Values.OrderBy(l => l.Group).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)],
                [.. moments.OrderBy(m => m.Time)],
                [.. passive.OrderBy(p => p.Time)],
                warnings);
        }

        private void AddObject(HitObject obj)
        {
            double start = obj.Time;
            switch (obj.Kind)
            {
                case HitObjectKind.Circle:
                    AddMoment(obj, 0, HitPosition.Circle, start, obj.HitSound, obj.HitSample ?? default, objectLevel: true);
                    break;

                case HitObjectKind.Spinner:
                    AddMoment(obj, 0, HitPosition.SpinnerEnd, obj.SpinnerEndTime, obj.HitSound, obj.HitSample ?? default, objectLevel: true);
                    break;

                case HitObjectKind.Slider:
                    AddSlider(obj, start);
                    break;

                default:
                    warnings.Add($"Skipped an osu!mania hold note: {obj.Line.Text}");
                    break;
            }
        }

        private void AddSlider(HitObject slider, double start)
        {
            var sounds = slider.NodeSounds;
            var sets = slider.NodeSets;
            int spans = sounds.Length - 1;
            var timingResult = SliderTiming.Compute(slider, start, spans, settings, timing, warnings);
            var sample = slider.HitSample ?? default;

            for (int node = 0; node <= spans; node++)
            {
                var position = node == 0 ? HitPosition.SliderHead : node == spans ? HitPosition.SliderTail : HitPosition.SliderRepeat;
                AddMoment(slider, node, position, start + node * timingResult.SpanDuration, sounds[node],
                    new HitSample(sets[node].Normal, sets[node].Addition, 0, 0, null), objectLevel: false);
            }

            // The body (slide, whistle, ticks) is evaluated once, at start + 6 ms, with the slider's own banks.
            double end = start + spans * timingResult.SpanDuration;
            var body = Settle(start + 1, sample, objectLevel: false);
            AddPassive(PassiveKind.SliderSlide, start, end, Resolve(body.Normal, "sliderslide", body.Index), body.Volume, slider);
            if (slider.HitSound.HasFlag(HitSound.Whistle))
                AddPassive(PassiveKind.SliderWhistle, start, end, Resolve(body.Addition, "sliderwhistle", body.Index), body.Volume, slider);

            var tick = Resolve(body.Normal, "slidertick", body.Index);
            foreach (double t in timingResult.TickTimes)
                AddPassive(PassiveKind.SliderTick, t, t, tick, body.Volume, slider);
        }

        private void AddMoment(HitObject obj, int node, HitPosition position, double time, HitSound sound, HitSample sample, bool objectLevel)
        {
            var s = Settle(time, sample, objectLevel);
            var hits = new List<Hit>(4);

            if (objectLevel && sample.Filename is { } filename)
            {
                // Stable plays only the named file: no hitnormal, no additions.
                hits.Add(new Hit(FileLane(filename, LaneSource.BeatmapFile)));
            }
            else
            {
                hits.Add(new Hit(Resolve(s.Normal, "hitnormal", s.Index), Layered: sound != HitSound.None && !sound.HasFlag(HitSound.Normal)));
                foreach (var (flag, name) in Additions)
                {
                    if (sound.HasFlag(flag))
                        hits.Add(new Hit(Resolve(s.Addition, name, s.Index)));
                }
            }

            moments.Add(new Moment(time + settings.TimeOffset, obj, node, position, s.Volume, hits, s.Point));
        }

        private readonly record struct Settled(SampleSet Normal, SampleSet Addition, int Index, int Volume, TimingPoint? Point);

        /// <summary>
        /// Banks, index and volume after inheritance from the timing point for a sound at <paramref name="time"/>. Only
        /// circles and spinners (<paramref name="objectLevel"/>) may set their own index and volume.
        /// </summary>
        private Settled Settle(double time, HitSample sample, bool objectLevel)
        {
            var point = timing.SamplePointAt(time);
            var pointSet = Known(point?.SampleSet ?? SampleSet.None) is var set && set != SampleSet.None ? set : settings.DefaultSampleSet;
            int pointIndex = Math.Max(point?.SampleIndex ?? 0, 0);
            int pointVolume = Math.Clamp(point?.Volume ?? 100, 0, 100);

            var normal = Known(sample.NormalSet) is var n && n != SampleSet.None ? n : pointSet;
            var addition = Known(sample.AdditionSet) is var a && a != SampleSet.None ? a : normal;
            int index = objectLevel && sample.Index > 0 ? sample.Index : pointIndex;
            int volume = objectLevel && sample.Volume > 0 ? Math.Min(sample.Volume, 100) : pointVolume;
            return new Settled(normal, addition, index, volume, point);
        }

        /// <summary>Unknown set numbers act as Normal (lazer; unverified for stable).</summary>
        private static SampleSet Known(SampleSet set) => set is >= SampleSet.None and <= SampleSet.Drum ? set : SampleSet.Normal;

        /// <summary>Index ≥ 1 plays the map's file when it exists (wav, mp3, ogg); otherwise, and for index 0, the skin's.</summary>
        private Lane Resolve(SampleSet set, string sound, int index)
        {
            if (resolved.TryGetValue((set, sound, index), out var lane)) return lane;
            lane = HitsoundRules.BeatmapSampleName(set, sound, index) is { } name && folder.FindSample(name) is { } file
                ? new Lane(LaneSource.BeatmapFile, file)
                : new Lane(LaneSource.Skin, HitsoundRules.SampleName(set, sound));
            return resolved[(set, sound, index)] = Intern(lane);
        }

        private Lane FileLane(string path, LaneSource source)
        {
            if (!MapFolder.IsInside(path))
            {
                warnings.Add($"\"{path}\" points outside the mapset folder, so it is never read.");
                return Intern(new Lane(source, path, Missing: true));
            }

            return Intern(folder.Find(path) is { } actual ? new Lane(source, actual) : new Lane(source, path, Missing: true));
        }

        private void AddStoryboard(OsuFile storyboard)
        {
            foreach (var (time, path, volume) in Storyboard.Samples(storyboard, warnings))
                AddPassive(PassiveKind.StoryboardSample, time, time, FileLane(path, LaneSource.StoryboardFile), volume);
        }

        private void AddPassive(PassiveKind kind, double time, double end, Lane lane, int volume, HitObject? obj = null) =>
            passive.Add(new PassiveSound(kind, time + settings.TimeOffset, end + settings.TimeOffset, lane, volume, obj));

        private Lane Intern(Lane lane) => lanes.TryGetValue(lane, out var existing) ? existing : lanes[lane] = lane;
    }
}
