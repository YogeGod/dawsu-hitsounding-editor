using System.Globalization;
using System.IO.Compression;
using System.Text;
using OsuHitsound.Format;

namespace OsuHitsound.Lanes.Keysound;

/// <summary>One file of the generated mapset: a copy of one rendered sound, a mix of several, or silence (none).</summary>
public sealed record KeyOutputFile(string Name, IReadOnlyList<string> Sounds);

/// <summary>
/// The generated [Keysounds] difficulty: the compiled .osu, the sample files it needs, and anything the compiler couldn't
/// encode (Generate stops on conflicts rather than publishing a difficulty that sounds wrong).
/// </summary>
public sealed record KeyGeneratePlan(OsuFile Diff, string FileName, IReadOnlyList<KeyOutputFile> Files, IReadOnlyList<Conflict> Conflicts, IReadOnlyList<string> Notes);

/// <summary>A track Generate includes: its synced notes, and those it will skip (never synced, or a target that changed).</summary>
public sealed record KeyGenerateTrack(KeyTrack Track, IReadOnlyList<KeyNote> Synced, int Unsynced, int Stale);

/// <summary>
/// Generate (docs/ideas/keysounding.md): each synced note's render becomes a sample on its gameplay position in a copy
/// of the reference difficulty, whose own hitsounds are replaced (positions without keysounds are silent). Up to four
/// notes on one position play natively (hitnormal, whistle, finish and clap of one index); more are mixed into one file.
/// The output is a standalone mapset: its own song, visuals and storyboard (without storyboard sounds).
/// </summary>
public static class KeyGenerate
{
    public const string Version = "Keysounds";
    private static readonly string[] Slots = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];
    private static readonly string[] SliderBody = ["sliderslide", "slidertick", "sliderwhistle"];

    /// <summary>The tracks included in Generate with their synced notes, in time order.</summary>
    public static List<KeyGenerateTrack> Tracks(KeysoundDocument doc)
    {
        var result = new List<KeyGenerateTrack>();
        foreach (var t in doc.Session.State.Tracks.Where(t => t.IncludeInGenerate))
        {
            var synced = new List<KeyNote>();
            int unsynced = 0, stale = 0;
            foreach (var n in KeyEditing.InTimeOrder(t.Notes))
                switch (KeySync.Status(n, doc.TargetIndex))
                {
                    case KeySyncStatus.Synced: synced.Add(n); break;
                    case KeySyncStatus.Unsynced: unsynced++; break;
                    default: stale++; break;
                }
            result.Add(new KeyGenerateTrack(t, synced, unsynced, stale));
        }

        return result;
    }

    /// <summary>
    /// The reference difficulty renamed [Keysounds] as a new local map (BeatmapID 0, BeatmapSetID -1, so importing it
    /// never updates the source set), without storyboard Sample lines.
    /// </summary>
    public static string DiffText(OsuFile reference)
    {
        var retimed = ShadowingGreenLines(reference);
        var sb = new StringBuilder();
        // A map without these fields still gets them, so the copy is always named [Keysounds] and never matches the source.
        string[] own = ["Version:", "BeatmapID:", "BeatmapSetID:"];
        var missing = own.Where(f => !reference.Lines.Any(l => l.Section == "Metadata" && l.Text.Trim().StartsWith(f, StringComparison.Ordinal))).ToList();
        string Added(string field) => field switch { "Version:" => "Version:" + Version, "BeatmapID:" => "BeatmapID:0", _ => "BeatmapSetID:-1" };
        bool metadata = reference.Lines.Any(l => l.Text.Trim() == "[Metadata]");
        foreach (var line in reference.Lines)
        {
            var text = line.Text.Trim();
            if (text == "[Metadata]")
            {
                sb.Append(line.Text).Append("\r\n");
                foreach (var field in missing) sb.Append(Added(field)).Append("\r\n");
                continue;
            }
            if (line.Section == "Metadata" && text.StartsWith("Version:", StringComparison.Ordinal)) sb.Append("Version:").Append(Version);
            else if (line.Section == "Metadata" && text.StartsWith("BeatmapID:", StringComparison.Ordinal)) sb.Append("BeatmapID:0");
            else if (line.Section == "Metadata" && text.StartsWith("BeatmapSetID:", StringComparison.Ordinal)) sb.Append("BeatmapSetID:-1");
            else if (line.Section == "Events" && IsSampleLine(text)) continue;
            else if (retimed.TryGetValue(line, out var time))
                sb.Append(time.ToString(CultureInfo.InvariantCulture)).Append(line.Text[line.Text.IndexOf(',')..]);
            else sb.Append(line.Text);
            sb.Append("\r\n");
        }

        if (!metadata) sb.Append("\r\n[Metadata]\r\n").Append(string.Join("\r\n", missing.Select(Added))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Green lines just after a slider node (within osu!'s 5 ms lookup) are in force at the node, so a sound written
    /// there would be overridden: they move onto the node (whole ms at or before it). Slider velocity and kiai use the
    /// same lookup, so the map plays the same; the copy's hitsounds are all Generate's own anyway.
    /// </summary>
    private static Dictionary<OsuLine, long> ShadowingGreenLines(OsuFile reference)
    {
        var nodes = Decompiler.Decompile(reference, new MapFolder([])).Moments
            .Where(m => m.Position is HitPosition.SliderHead or HitPosition.SliderRepeat or HitPosition.SliderTail)
            .Select(m => m.Time).Order().ToArray();
        var points = new List<(double Time, bool Red, double BeatLength, int Effects, OsuLine Line)>();
        foreach (var p in reference.TimingPoints)
            try { points.Add((p.Time, p.Uninherited, p.BeatLength, p.Effects, p.Line)); }
            catch (FormatException) { }
        points.Sort((a, b) => a.Time.CompareTo(b.Time)); // stable: file order at equal times

        var moved = new Dictionary<OsuLine, long>();
        foreach (var point in points.Where(p => !p.Red))
        {
            int i = Array.BinarySearch(nodes, point.Time);
            if (i >= 0) continue; // already on a node
            i = ~i - 1; // the last node before the line
            if (i < 0 || point.Time - nodes[i] > HitsoundRules.ControlPointLeniency) continue;
            long to = (long)Math.Floor(nodes[i]);
            if (to >= point.Time) continue;
            // osu! reads slider velocity and kiai exactly at the time (only sounds get the lookup), so move only a line
            // that changes neither from what is in force where it lands.
            var before = points.Where(p => p.Time <= to && p.Line != point.Line).ToList();
            var red = before.LastOrDefault(p => p.Red);
            var green = before.LastOrDefault(p => !p.Red && (red.Line is null || p.Time >= red.Time));
            double velocity = green.Line is not null ? green.BeatLength : -100;
            int effects = before.Count > 0 ? before[^1].Effects : 0;
            if (point.BeatLength == velocity && (point.Effects & 1) == (effects & 1)) moved[point.Line] = to;
        }

        return moved;
    }

    private static bool IsSampleLine(string text) =>
        text.StartsWith("Sample,", StringComparison.Ordinal) || text.StartsWith("5,", StringComparison.Ordinal);

    /// <summary>A storyboard (.osb) without its Sample lines: only the keysounds play over the song.</summary>
    public static string WithoutSamples(string osb) =>
        string.Join("\r\n", osb.Replace("\r\n", "\n").Split('\n').Where(l => !IsSampleLine(l.Trim())));

    /// <summary>
    /// The difficulty and its files for the sounds at each position ((object index, node) → sound ids, which may repeat).
    /// Equal sound sets share an index; positions with no sound get a silent hitnormal, and every index on a slider
    /// silences that slider's body sounds.
    /// </summary>
    public static KeyGeneratePlan Plan(OsuFile reference, string referencePath, IReadOnlyDictionary<(int Object, int Node), IReadOnlyList<string>> soundsAt)
    {
        var diff = OsuFile.Parse(Encoding.UTF8.GetBytes(DiffText(reference)));
        var moments = Decompiler.Decompile(diff, new MapFolder([])).Moments;
        var files = new Dictionary<string, KeyOutputFile>(StringComparer.OrdinalIgnoreCase);
        var indexOf = new Dictionary<string, (int Index, List<Lane> Lanes)>();
        var sliderIndexes = new SortedSet<int>();
        var wanted = new Dictionary<MomentKey, WantedSound>();

        foreach (var m in moments)
        {
            var key = Compiler.KeyOf(diff, m);
            var sounds = soundsAt.GetValueOrDefault((key.ObjectIndex, key.Node)) ?? [];
            var ordered = sounds.Order(StringComparer.Ordinal).ToList();
            bool native = ordered.Count <= Slots.Length && ordered.Distinct().Count() == ordered.Count;
            string signature = ordered.Count == 0 ? "silent" : (native ? "native:" : "mix:") + string.Join("+", ordered);
            if (!indexOf.TryGetValue(signature, out var entry))
            {
                int index = indexOf.Count + 2; // index 1 is the unnumbered "normal-hitnormal.wav", reachable only by filename
                var lanes = new List<Lane>();
                void Add(string slot, IReadOnlyList<string> content)
                {
                    string name = $"normal-{slot}{index.ToString(CultureInfo.InvariantCulture)}.wav";
                    files[name] = new KeyOutputFile(name, content);
                    lanes.Add(new Lane(LaneSource.BeatmapFile, name));
                }

                if (ordered.Count == 0) Add("hitnormal", []);
                else if (native) for (int i = 0; i < ordered.Count; i++) Add(Slots[i], [ordered[i]]);
                else Add("hitnormal", ordered);
                indexOf[signature] = entry = (index, lanes);
            }

            if (m.Position is HitPosition.SliderHead or HitPosition.SliderRepeat or HitPosition.SliderTail) sliderIndexes.Add(entry.Index);
            wanted[key] = new WantedSound(entry.Lanes, 100);
        }

        foreach (int index in sliderIndexes)
            foreach (var set in new[] { "normal", "soft", "drum" })
                foreach (var body in SliderBody)
                {
                    string name = $"{set}-{body}{index.ToString(CultureInfo.InvariantCulture)}.wav";
                    files.TryAdd(name, new KeyOutputFile(name, []));
                }

        var result = Compiler.Apply(diff, new MapFolder(files.Keys), wanted);
        return new KeyGeneratePlan(diff, HitsoundDiff.FileNameFor(reference, referencePath, Version), [.. files.Values], result.Conflicts, result.Notes);
    }

    /// <summary>
    /// Writes the generated mapset into <c>output</c> in the project folder, all or nothing: everything goes into a new
    /// folder first, which replaces the previous output only once complete. Copies the reference's song, visuals and
    /// storyboard (without Sample lines), never its .osu files. <paramref name="bytes"/> gives each sample file's content.
    /// Returns the output folder.
    /// </summary>
    public static string Write(KeysoundDocument doc, KeyGeneratePlan plan, Func<KeyOutputFile, byte[]> bytes)
    {
        if (plan.Conflicts.Count > 0) throw new IOException($"Nothing was written: {plan.Conflicts[0].Reason}");
        string output = Path.Combine(doc.Folder, "output");
        string staging = Path.Combine(doc.Folder, $"output.new-{Guid.NewGuid():N}");
        string reference = Path.Combine(doc.Folder, KeysoundStore.ReferenceFolder);
        Directory.CreateDirectory(staging);
        try
        {
            // Real files inside the project only: a link or junction in a project from elsewhere could otherwise pull files
            // from anywhere on this computer into the .osz.
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(reference, "*", options))
            {
                string relative = Path.GetRelativePath(reference, file);
                if (relative.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (relative.EndsWith(".osb", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(target, WithoutSamples(File.ReadAllText(file)));
                else File.Copy(file, target);
            }

            foreach (var file in plan.Files)
            {
                if (!MapFolder.IsInside(file.Name) || Path.GetFileName(file.Name) != file.Name) throw new IOException("A sample name is outside the mapset.");
                File.WriteAllBytes(Path.Combine(staging, file.Name), bytes(file));
            }

            File.WriteAllBytes(Path.Combine(staging, plan.FileName), plan.Diff.ToBytes());

            string? old = null;
            if (Directory.Exists(output))
            {
                old = Path.Combine(doc.Folder, $"output.old-{Guid.NewGuid():N}");
                Directory.Move(output, old);
            }

            try { Directory.Move(staging, output); }
            catch
            {
                if (old is not null) Directory.Move(old, output); // put the previous output back
                throw;
            }

            if (old is not null)
                try { Directory.Delete(old, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // a leftover folder, not a failure
            return output;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Zips the generated mapset as a .osz for osu! to import (replacing an existing file at that path).</summary>
    public static void ExportOsz(string output, string oszPath)
    {
        string temp = oszPath + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        ZipFile.CreateFromDirectory(output, temp, CompressionLevel.Optimal, includeBaseDirectory: false);
        File.Move(temp, oszPath, overwrite: true);
    }
}
