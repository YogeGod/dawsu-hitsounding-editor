using System.Collections.Concurrent;
using System.Globalization;

namespace OsuHitsound.Audio;

/// <summary>One sample zone of an SFZ instrument: which keys and velocities it plays, and how.</summary>
public sealed record SfzRegion(string Sample, int LoKey, int HiKey, int KeyCenter, int LoVel, int HiVel)
{
    /// <summary>Loop points in frames (inclusive end), set only for a looping region (loop_continuous or loop_sustain).</summary>
    public long LoopStart { get; init; } = -1;
    public long LoopEnd { get; init; } = -1;
    /// <summary>Seconds to fade out after the key is released (ampeg_release).</summary>
    public double Release { get; init; } = 0.3;
    /// <summary>2-pole low-pass cutoff in Hz, or 0 for none (fil_type=lpf_2p, cutoff).</summary>
    public double Cutoff { get; init; }

    public bool Plays(int key, int velocity) => key >= LoKey && key <= HiKey && velocity >= LoVel && velocity <= HiVel;
    public bool HasLoop => LoopStart >= 0 && LoopEnd > LoopStart;
}

/// <summary>
/// A sampled instrument read from the subset of SFZ the bundled piano uses: &lt;global&gt;, &lt;group&gt; and
/// &lt;region&gt; with key/velocity ranges, pitch_keycenter, loop_mode and loop points, ampeg_release and a 2-pole
/// low-pass. Samples are decoded to stereo 44.1 kHz once, before playing, never on the audio thread.
/// </summary>
public sealed class SfzInstrument
{
    private readonly Dictionary<SfzRegion, float[]> samples;
    // The region for every key and velocity, worked out once: the audio thread looks notes up without allocating.
    private readonly SfzRegion?[] byKeyAndVelocity = new SfzRegion?[128 * 128];

    public IReadOnlyList<SfzRegion> Regions { get; }

    /// <summary>Semitones at full pitch bend (the General MIDI default; SFZ's bend_up/bend_down aren't read).</summary>
    public double PitchBendRange { get; init; } = 2;

    /// <summary>Decodes every sample (in parallel) with <paramref name="loadSample"/>, by the region's sample name.</summary>
    public SfzInstrument(IReadOnlyList<SfzRegion> regions, Func<string, float[]?> loadSample)
    {
        Regions = regions;
        var decoded = new ConcurrentDictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Parallel.ForEach(regions.Select(r => r.Sample).Distinct(StringComparer.OrdinalIgnoreCase), name =>
                decoded[name] = loadSample(name) is { Length: >= 2 } data ? data : throw new InvalidDataException($"The instrument sample {name} can't be read."));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            // Callers handle what one sample load throws (a missing file, unreadable audio), not the parallel wrapper.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
            throw;
        }
        samples = regions.Distinct().ToDictionary(r => r, r => decoded[r.Sample]);
        for (int key = 0; key < 128; key++)
            for (int velocity = 0; velocity < 128; velocity++)
                byKeyAndVelocity[key * 128 + velocity] = Find(key, velocity);
    }

    /// <summary>The bundled piano next to the program (Instruments/UprightPianoKW, CC0 1.0).</summary>
    public static string BundledPianoPath => Path.Combine(AppContext.BaseDirectory, "Instruments", "UprightPianoKW", "UprightPianoKW.sfz");

    /// <summary>Reads an .sfz and decodes its samples (paths relative to the .sfz; they must stay inside its folder).</summary>
    public static SfzInstrument Load(string sfzPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sfzPath))!;
        var regions = Parse(File.ReadAllText(sfzPath));
        return new SfzInstrument(regions, name =>
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return SampleMixer.Decode(File.ReadAllBytes(path));
        });
    }

    public float[] Sample(SfzRegion region) => samples[region];

    /// <summary>The region for a key and velocity: the first that matches, else the nearest key in the velocity's layer.</summary>
    public SfzRegion? RegionFor(int key, int velocity) => byKeyAndVelocity[Math.Clamp(key, 0, 127) * 128 + Math.Clamp(velocity, 0, 127)];

    private SfzRegion? Find(int key, int velocity) =>
        Regions.FirstOrDefault(r => r.Plays(key, velocity))
        ?? Regions.Where(r => velocity >= r.LoVel && velocity <= r.HiVel).MinBy(r => Math.Abs(r.KeyCenter - key));

    public static List<SfzRegion> Parse(string text)
    {
        var global = new Dictionary<string, string>();
        var group = new Dictionary<string, string>();
        Dictionary<string, string>? region = null;
        var regions = new List<SfzRegion>();
        string header = "";

        void Flush()
        {
            if (region is null) return;
            var all = new Dictionary<string, string>(global);
            foreach (var (k, v) in group) all[k] = v;
            foreach (var (k, v) in region) all[k] = v;
            if (ToRegion(all) is { } r) regions.Add(r);
            region = null;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            foreach (var token in Tokens(line))
            {
                if (token.StartsWith('<') && token.EndsWith('>'))
                {
                    Flush();
                    header = token;
                    if (header == "<group>") group = [];
                    if (header == "<region>") region = [];
                    continue;
                }

                int eq = token.IndexOf('=');
                if (eq <= 0) continue;
                var (key, value) = (token[..eq], token[(eq + 1)..]);
                (header switch { "<region>" => region!, "<group>" => group, _ => global })[key] = value;
            }
        }

        Flush();
        return regions;
    }

    // Opcodes are separated by spaces, but a sample path may contain spaces: it runs to the next opcode or the line's end.
    private static IEnumerable<string> Tokens(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            if (token.StartsWith("sample=", StringComparison.Ordinal))
                while (i + 1 < parts.Length && !parts[i + 1].Contains('=') && !parts[i + 1].StartsWith('<')) token += " " + parts[++i];
            yield return token.Trim();
        }
    }

    private static SfzRegion? ToRegion(Dictionary<string, string> o)
    {
        if (!o.TryGetValue("sample", out var sample) || sample.Length == 0) return null;
        int Int(string k, int fallback) => o.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        double Num(string k, double fallback) => o.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : fallback;
        int key = Int("key", -1);
        int lo = Int("lokey", key >= 0 ? key : 0), hi = Int("hikey", key >= 0 ? key : 127);
        bool lowPass = o.GetValueOrDefault("fil_type", "lpf_2p") == "lpf_2p" && o.ContainsKey("cutoff");
        bool loops = o.GetValueOrDefault("loop_mode") is "loop_continuous" or "loop_sustain";
        return new SfzRegion(sample.Replace('\\', '/'), lo, hi, Int("pitch_keycenter", key >= 0 ? key : 60), Int("lovel", 1), Int("hivel", 127))
        {
            LoopStart = loops ? Int("loop_start", -1) : -1,
            LoopEnd = loops ? Int("loop_end", -1) : -1,
            Release = Math.Max(0.005, Num("ampeg_release", 0.3)),
            Cutoff = lowPass ? Math.Clamp(Num("cutoff", 0), 20, 20000) : 0,
        };
    }
}
