// A stand-in for native/Vst3IsolatedHost (dawsu_vst3_host.exe) speaking the same protocol, for tests of the crash,
// timeout and error paths without a real plug-in. The "plug-in path" picks the behaviour:
//   tone        renders 0.25 on both channels while any note is held
//   crash       exits on the first audio block, as a crashing plug-in would
//   slow        takes 300 ms per block
//   fail        refuses to load (ERROR)
//   SHOW/GETSTATE/SETSTATE work for tone: the state is whatever was last set ("AQAAAAAAAAAA" at first).
//   RENDER (with --offline) writes 0.25 from the first note on to its note-off, then a 1000-frame tail at 0.01; a note
//   longer than the limit is cut (truncated). Each note's events must be in its own command, as dawsu! sends them.
using System.IO.MemoryMappedFiles;
using OsuHitsound.Audio;

if (args.Length < 4 || args[0] != "--host") { Console.WriteLine("ERROR\tusage"); return 1; }
string name = args[1], mode = Path.GetFileNameWithoutExtension(args[3]);
if (mode == "fail") { Console.WriteLine("ERROR\tThis instrument refuses to load."); return 2; }

using var map = MemoryMappedFile.OpenExisting($@"Local\{name}");
using var view = map.CreateViewAccessor();
using var req = EventWaitHandle.OpenExisting($@"Local\{name}-req");
using var done = EventWaitHandle.OpenExisting($@"Local\{name}-done");
using var parent = System.Diagnostics.Process.GetProcessById(int.Parse(args[2]));
Console.WriteLine($"READY\tFake {mode}\t{IsolatedVst3.Protocol}");

string state = "AQAAAAAAAAAA";
bool offline = args.Contains("--offline");

string Render(string command)
{
    if (!offline) return "ERROR\tThis host plays live.";
    var f = command.Split('\t');
    long max = long.Parse(f[2]);
    var events = f[5].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split(':').Select(long.Parse).ToArray()).ToList();
    long on = events.Where(e => (e[1] & 0xF0) == 0x90 && e[3] > 0).Select(e => e[0]).DefaultIfEmpty(0).First();
    long off = events.Where(e => (e[1] & 0xF0) == 0x80).Select(e => e[0]).DefaultIfEmpty(on).First();
    long frames = Math.Min(max, off + 1000);
    var samples = new float[frames * 2];
    for (long i = on; i < frames; i++) samples[i * 2] = samples[i * 2 + 1] = i < off ? 0.25f : 0.01f;
    var bytes = new byte[samples.Length * 4];
    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
    File.WriteAllBytes(f[1], bytes);
    return $"RENDERED\t{frames}\t{(frames == max ? 1 : 0)}\t0.25";
}

var commands = new Thread(() =>
{
    while (Console.ReadLine() is { } line)
    {
        var parts = line.Split('\t', 2);
        if (parts.Length < 2) continue;
        var (id, command) = (parts[0], parts[1]);
        if (command == "QUIT") Environment.Exit(0);
        string reply = command switch
        {
            "SHOW" or "HIDE" => "OK",
            "GETSTATE" => "STATE\t" + state,
            _ when command.StartsWith("SETSTATE\t") => "OK",
            _ when command.StartsWith("RENDER\t") => Render(command),
            _ => "ERROR\tUnknown command.",
        };
        if (command.StartsWith("SETSTATE\t")) state = command[9..];
        Console.WriteLine($"{id}\t{reply}");
    }

    Environment.Exit(0);
}) { IsBackground = true };
commands.Start();

var held = new HashSet<int>();
while (!parent.HasExited)
{
    if (!req.WaitOne(500)) continue;
    if (mode == "crash") Environment.Exit(99);
    if (mode == "slow") Thread.Sleep(300);
    int frames = view.ReadInt32(ShmLayout.FramesOffset), count = view.ReadInt32(ShmLayout.EventCountOffset);
    for (int i = 0; i < count; i++)
    {
        int at = ShmLayout.EventsOffset + i * ShmLayout.EventSize;
        byte status = view.ReadByte(at + 4), d1 = view.ReadByte(at + 5), d2 = view.ReadByte(at + 6);
        if ((status & 0xF0) == 0x90 && d2 > 0) held.Add(d1);
        else if ((status & 0xF0) is 0x80 or 0x90) held.Remove(d1);
    }

    var output = Enumerable.Repeat(held.Count > 0 ? 0.25f : 0f, frames).ToArray();
    view.WriteArray(ShmLayout.LeftOffset, output, 0, frames);
    view.WriteArray(ShmLayout.RightOffset, output, 0, frames);
    view.Write(ShmLayout.StatusOffset, 0);
    view.Write(ShmLayout.DoneSeqOffset, view.ReadUInt32(ShmLayout.RequestSeqOffset));
    done.Set();
}

return 0;
