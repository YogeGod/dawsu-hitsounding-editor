// Generates the milestone 1 test mapset: one diff per open question about osu!stable hitsounds.
// Run from the repo root: dotnet run tools/make-test-maps.cs
// Output: testmaps/hitsound-rules-test/ and testmaps/hitsound-rules-test.osz (needs ffmpeg on PATH).
// What to listen for is in testmaps/README.md.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;

const string SetName = "hitsound-rules-test";
const int T0 = 1000;   // first beat, ms
const int Beat = 500;  // 120 BPM

var outDir = Path.Combine("testmaps", SetName);
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);

int At(double beat) => T0 + (int)(beat * Beat);

// Sample files. Pitches are far apart so each one is easy to tell by ear.
var samples = new Dictionary<string, byte[]>
{
    // Diff 1: head/tail is the same neutral click for both indices; ticks differ by index.
    ["soft-hitnormal.wav"] = Beep(800, 0.10),
    ["soft-hitnormal2.wav"] = Beep(800, 0.10),
    ["soft-slidertick.wav"] = Beep(2000, 0.08),
    ["soft-slidertick2.wav"] = Beep(400, 0.08),
    ["soft-sliderslide.wav"] = Blank(),
    ["soft-sliderslide2.wav"] = Blank(),
    // Diff 2: index 3 = low, index 4 = high, for both the head/tail and the slide.
    ["soft-hitnormal3.wav"] = Beep(400, 0.10),
    ["soft-hitnormal4.wav"] = Beep(2000, 0.10),
    ["soft-sliderslide3.wav"] = Tone(200, 0.6),
    ["soft-sliderslide4.wav"] = Tone(1200, 0.6),
    ["soft-slidertick3.wav"] = Blank(),
    ["soft-slidertick4.wav"] = Blank(),
    // Diff 3: one pitch per sampleset, index 5.
    ["normal-hitnormal5.wav"] = Beep(300, 0.10),
    ["soft-hitnormal5.wav"] = Beep(800, 0.10),
    ["drum-hitnormal5.wav"] = Beep(2000, 0.10),
};
foreach (var (name, bytes) in samples) File.WriteAllBytes(Path.Combine(outDir, name), bytes);

// Diff 1: does a green line in the middle of a slider change the ticks after it?
// 4-beat sliders, tick rate 1 -> ticks at +1, +2, +3 beats. The green line sits at +1.5 beats.
// Sliders 1-2 change the index (high tick -> low tick); sliders 3-4 change the volume (80 -> 15).
{
    var tp = new List<string> { TimingPoint(T0, Beat, 2, 1, 80, red: true) };
    var ho = new List<string>();
    for (int k = 0; k < 4; k++)
    {
        int t = At(k * 6);
        tp.Add(TimingPoint(t, -100, 2, 1, 80));
        tp.Add(k < 2 ? TimingPoint(At(k * 6 + 1.5), -100, 2, 2, 80) : TimingPoint(At(k * 6 + 1.5), -100, 2, 1, 15));
        ho.Add($"64,192,{t},{(k == 0 ? 6 : 2)},0,L|464:192,1,400,0|0,0:0|0:0,0:0:0:0:");
    }
    WriteDiff("1 Ticks", "Soft", tp, ho);
}

// Diff 2: does stable use a slider's own hitSample index and volume?
// Timing point: index 3, volume 40. Odd sliders are controls (0:0:0:0:), even sliders say index 4, volume 100.
{
    var tp = new List<string> { TimingPoint(T0, Beat, 2, 3, 40, red: true) };
    var ho = new List<string>();
    for (int k = 0; k < 6; k++)
    {
        int t = At(k * 4);
        var hitSample = k % 2 == 0 ? "0:0:0:0:" : "0:0:4:100:";
        ho.Add($"64,192,{t},{(k == 0 ? 6 : 2)},0,L|264:192,1,200,0|0,0:0|0:0,{hitSample}");
    }
    WriteDiff("2 Slider body", "Soft", tp, ho);
}

// Diff 3: what does sampleSet 0 on a timing point mean? [General] SampleSet is Drum in "v2" and Normal in "v3".
// Groups of 4 circles: sampleSet 0, explicit Drum, explicit Normal, explicit Soft, then sampleSet 0 again.
foreach (var (diffName, defaultSet) in new[] { ("3 SampleSet 0 v2", "Drum"), ("3 SampleSet 0 v3 (default Normal)", "Normal") })
{
    var tp = new List<string>
    {
        TimingPoint(T0, Beat, 0, 5, 70, red: true),
        TimingPoint(At(6), -100, 3, 5, 70),
        TimingPoint(At(12), -100, 1, 5, 70),
        TimingPoint(At(18), -100, 2, 5, 70),
        TimingPoint(At(24), -100, 0, 5, 70),
    };
    var ho = new List<string>();
    foreach (int group in new[] { 0, 6, 12, 18, 24 })
        for (int i = 0; i < 4; i++)
            ho.Add($"{64 + i * 96},192,{At(group + i)},{(i == 0 ? 5 : 1)},0,0:0:0:0:");
    WriteDiff(diffName, defaultSet, tp, ho);
}

MakeSilentAudio(Path.Combine(outDir, "audio.mp3"), seconds: 20);

var osz = Path.Combine("testmaps", SetName + ".osz");
File.Delete(osz);
ZipFile.CreateFromDirectory(outDir, osz);
Console.WriteLine($"Wrote {outDir} and {osz}");

void WriteDiff(string version, string defaultSet, List<string> timingPoints, List<string> hitObjects)
{
    var sb = new StringBuilder();
    sb.Append("osu file format v14\r\n\r\n");
    sb.Append("[General]\r\nAudioFilename: audio.mp3\r\nAudioLeadIn: 0\r\nPreviewTime: -1\r\nCountdown: 0\r\n");
    sb.Append($"SampleSet: {defaultSet}\r\nStackLeniency: 0.7\r\nMode: 0\r\nLetterboxInBreaks: 0\r\nWidescreenStoryboard: 0\r\n\r\n");
    sb.Append("[Metadata]\r\nTitle:Hitsound rules test\r\nTitleUnicode:Hitsound rules test\r\nArtist:Test\r\nArtistUnicode:Test\r\n");
    sb.Append($"Creator:hs-editor\r\nVersion:{version}\r\nSource:\r\nTags:\r\nBeatmapID:0\r\nBeatmapSetID:-1\r\n\r\n");
    sb.Append("[Difficulty]\r\nHPDrainRate:0\r\nCircleSize:4\r\nOverallDifficulty:5\r\nApproachRate:7\r\nSliderMultiplier:1\r\nSliderTickRate:1\r\n\r\n");
    sb.Append("[Events]\r\n//Background and Video events\r\n//Break Periods\r\n\r\n");
    sb.Append("[TimingPoints]\r\n");
    foreach (var line in timingPoints) sb.Append(line).Append("\r\n");
    sb.Append("\r\n\r\n[HitObjects]\r\n");
    foreach (var line in hitObjects) sb.Append(line).Append("\r\n");
    File.WriteAllText(Path.Combine(outDir, $"Test - Hitsound rules test (hs-editor) [{version}].osu"), sb.ToString());
}

static string TimingPoint(int time, double beatLength, int sampleSet, int index, int volume, bool red = false) =>
    $"{time},{beatLength},4,{sampleSet},{index},{volume},{(red ? 1 : 0)},0";

// 16-bit mono 44.1 kHz PCM.
static byte[] Wav(short[] pcm)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write("RIFF"u8); w.Write(36 + pcm.Length * 2); w.Write("WAVE"u8);
    w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(44100); w.Write(88200); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8); w.Write(pcm.Length * 2);
    foreach (var s in pcm) w.Write(s);
    w.Flush();
    return ms.ToArray();
}

// Matches the official 44-byte blank.wav: a valid header with no audio data.
static byte[] Blank() => Wav([]);

static byte[] Beep(double freq, double seconds) => Synth(freq, seconds, decay: true);

static byte[] Tone(double freq, double seconds) => Synth(freq, seconds, decay: false);

static byte[] Synth(double freq, double seconds, bool decay)
{
    int n = (int)(44100 * seconds);
    var pcm = new short[n];
    for (int i = 0; i < n; i++)
    {
        double t = i / 44100.0;
        double env = decay ? Math.Exp(-t * 30) : Math.Min(1, Math.Min(i, n - i) / 441.0); // 10 ms fade in/out for loops
        pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * env * 0.5 * short.MaxValue);
    }
    return Wav(pcm);
}

static void MakeSilentAudio(string path, int seconds)
{
    var psi = new ProcessStartInfo("ffmpeg", ["-y", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono", "-t", seconds.ToString(), "-q:a", "9", path]);
    using var p = Process.Start(psi)!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception("ffmpeg failed; is it on PATH?");
}
