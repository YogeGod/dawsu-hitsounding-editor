// Scores the signal-processing kick/snare detector against drum-only GM MIDI annotations.
// Usage: dotnet run --project tools/DrumAutoFill/DspEval -- [--scale 1.25] song.wav annotation.mid [song2.wav annotation2.mid ...]
// Matching follows the earlier prototype: GM kick 35/36, snare 38/40, chronological one-to-one within 50 ms.
using OsuHitsound.Audio;

double scale = 1.25;
var rest = new List<string>(args);
int s = rest.IndexOf("--scale");
if (s >= 0) { scale = double.Parse(rest[s + 1], System.Globalization.CultureInfo.InvariantCulture); rest.RemoveRange(s, 2); }
// --osu map.osu --start-ms N --grid 4[,3]: keep only detections within 20 ms (or a third of a step) of the map's
// beat grid, like the editor does. --start-ms is the excerpt's position in the song.
string? osu = null; double startMs = 0; int[] grid = [4];
int o = rest.IndexOf("--osu"); if (o >= 0) { osu = rest[o + 1]; rest.RemoveRange(o, 2); }
o = rest.IndexOf("--start-ms"); if (o >= 0) { startMs = double.Parse(rest[o + 1], System.Globalization.CultureInfo.InvariantCulture); rest.RemoveRange(o, 2); }
o = rest.IndexOf("--grid"); if (o >= 0) { grid = rest[o + 1].Split(",").Select(int.Parse).ToArray(); rest.RemoveRange(o, 2); }
var reds = osu is null ? [] : File.ReadLines(osu).SkipWhile(l => l.Trim() != "[TimingPoints]").Skip(1).TakeWhile(l => !l.StartsWith("[")).Select(l => l.Split(","))
    .Where(x => x.Length >= 7 && x[6].Trim() == "1").Select(x => (Time: double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), Beat: double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture))).OrderBy(r => r.Time).ToList();
bool OnGrid(double excerptMs)
{
    if (reds.Count == 0) return true;
    double t = excerptMs + startMs;
    var red = reds.LastOrDefault(r => r.Time <= t, reds[0]);
    return grid.Any(d => { double step = red.Beat / d, snapped = red.Time + Math.Round((t - red.Time) / step) * step; return Math.Abs(snapped - t) <= Math.Min(20, step / 3); });
}
if (rest.Count == 0 || rest.Count % 2 != 0) { Console.Error.WriteLine("Pass WAV/MIDI pairs."); return 2; }
for (int i = 0; i < rest.Count; i += 2)
{
    var (mono, rate) = ReadWav(rest[i]);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var events = DrumOnsets.Detect(mono, rate, scale).Where(e => OnGrid(e.TimeMs)).ToList();
    watch.Stop();
    var reference = ReadMidi(rest[i + 1]);
    if (Environment.GetEnvironmentVariable("DSP_DIAG") == "1") Console.WriteLine("  annotated: " + string.Join(" ", reference.GroupBy(e => e.Drum).Select(g => $"{g.Key}:{g.Count()}")));
    Console.WriteLine($"{Path.GetFileName(rest[i])} ({mono.Length / (double)rate:F2} s, {watch.ElapsedMilliseconds} ms, scale {scale})");
    foreach (string drum in new[] { "kick", "snare" })
    {
        var p = events.Where(e => e.Drum == drum).Select(e => e.TimeMs).ToList();
        var r = reference.Where(e => e.Drum == drum).Select(e => e.Ms).ToList();
        var (hits, deltas) = Match(p, r);
        double prec = p.Count == 0 ? 0 : hits / (double)p.Count, rec = r.Count == 0 ? 0 : hits / (double)r.Count;
        double f = prec + rec == 0 ? 0 : 2 * prec * rec / (prec + rec);
        if (Environment.GetEnvironmentVariable("DSP_DIAG") == "1")
        {
            var usedP = p.Where(x => !r.Any(y => Math.Abs(x - y) <= 50)).ToList();
            var lands = usedP.Select(x => reference.Where(e => Math.Abs(e.Ms - x) <= 50).Select(e => e.Drum).DefaultIfEmpty("nothing").First())
                .GroupBy(x => x).Select(g => $"{g.Key}:{g.Count()}");
            Console.WriteLine($"  false {drum} land on: " + string.Join(" ", lands));
            Console.WriteLine("    first false at s: " + string.Join(" ", usedP.Take(12).Select(x => (x / 1000).ToString("F2"))));
        }
        Console.WriteLine($"  {drum,-5} correct/proposed/reference {hits}/{p.Count}/{r.Count}  P {prec:F2} R {rec:F2} F {f:F2}  mean offset {(deltas.Count > 0 ? deltas.Average() : 0):+0.0;-0.0} ms");
    }
}
return 0;

static (int, List<double>) Match(List<double> predicted, List<double> reference)
{
    var used = new bool[predicted.Count];
    var deltas = new List<double>();
    foreach (double r in reference)
    {
        int best = -1;
        for (int j = 0; j < predicted.Count; j++)
            if (!used[j] && Math.Abs(predicted[j] - r) <= 50 && (best < 0 || Math.Abs(predicted[j] - r) < Math.Abs(predicted[best] - r))) best = j;
        if (best >= 0) { used[best] = true; deltas.Add(predicted[best] - r); }
    }
    return (deltas.Count, deltas);
}

static (float[], int) ReadWav(string path)
{
    using var r = new BinaryReader(File.OpenRead(path));
    if (new string(r.ReadChars(4)) != "RIFF") throw new IOException("Not a WAV file");
    r.ReadInt32(); r.ReadChars(4);
    int channels = 0, rate = 0, bits = 0;
    while (r.BaseStream.Position < r.BaseStream.Length)
    {
        string id = new(r.ReadChars(4)); int size = r.ReadInt32();
        if (id == "fmt ") { r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32(); r.ReadBytes(6); bits = r.ReadInt16(); r.ReadBytes(size - 16); }
        else if (id == "data")
        {
            if (bits != 16) throw new IOException("Only 16-bit PCM is supported");
            int frames = size / (2 * channels);
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < channels; c++) sum += r.ReadInt16() / 32768.0;
                mono[f] = (float)(sum / channels);
            }
            return (mono, rate);
        }
        else r.ReadBytes(size + (size & 1));
    }
    throw new IOException("No data chunk");
}

static List<(string Drum, double Ms)> ReadMidi(string path)
{
    var b = File.ReadAllBytes(path);
    int pos = 0;
    uint U32() { uint v = (uint)(b[pos] << 24 | b[pos + 1] << 16 | b[pos + 2] << 8 | b[pos + 3]); pos += 4; return v; }
    int U16() { int v = b[pos] << 8 | b[pos + 1]; pos += 2; return v; }
    int Var() { int v = 0; byte c; do { c = b[pos++]; v = v << 7 | c & 0x7f; } while ((c & 0x80) != 0); return v; }
    pos = 8; U16(); int tracks = U16(), division = U16();
    var tempos = new List<(long Tick, double Us)>();
    var notes = new List<(long Tick, int Pitch)>();
    pos = 14;
    for (int t = 0; t < tracks; t++)
    {
        pos += 4; int end = (int)U32() + pos; long tick = 0; int status = 0;
        while (pos < end)
        {
            tick += Var();
            int x = b[pos];
            if (x >= 0x80) { status = x; pos++; }
            if (status == 0xFF) { int type = b[pos++]; int len = Var(); if (type == 0x51) tempos.Add((tick, b[pos] << 16 | b[pos + 1] << 8 | b[pos + 2])); pos += len; }
            else if (status is 0xF0 or 0xF7) pos += Var();
            else
            {
                int kind = status & 0xF0, d1 = b[pos++], d2 = kind is 0xC0 or 0xD0 ? 0 : b[pos++];
                if (kind == 0x90 && d2 > 0) notes.Add((tick, d1));
            }
        }
        pos = end;
    }
    tempos.Sort();
    if (tempos.Count == 0 || tempos[0].Tick > 0) tempos.Insert(0, (0, 500000));
    double Ms(long tick)
    {
        double ms = 0; long last = 0; double us = tempos[0].Us;
        foreach (var (tt, tu) in tempos) { if (tt > tick) break; ms += (tt - last) * us / division / 1000; last = tt; us = tu; }
        return ms + (tick - last) * us / division / 1000;
    }
    return notes.Select(n => (n.Pitch switch { 35 or 36 => "kick", 38 or 40 => "snare", _ => "gm" + n.Pitch }, Ms(n.Tick))).ToList();
}
