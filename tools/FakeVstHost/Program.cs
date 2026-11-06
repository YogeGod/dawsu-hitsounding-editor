// A stand-in for native/Vst3IsolatedHost (dawsu_vst3_host.exe) speaking the same protocol, for tests of the crash,
// timeout and error paths without a real plug-in. The "plug-in path" picks the behaviour:
//   tone        renders 0.25 on both channels while any note is held
//   crash       exits on the first audio block, as a crashing plug-in would
//   slow        takes 300 ms per block
//   fail        refuses to load (ERROR)
//   SHOW/GETSTATE/SETSTATE work for tone: the state is whatever was last set ("AQAAAAAAAAAA" at first).
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
