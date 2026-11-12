using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using OsuHitsound.Lanes;

namespace OsuHitsound.Audio;

/// <summary>A MIDI event for a VST3 instrument at a timeline time (status byte with channel, two data bytes).</summary>
public readonly record struct TimedMidi(double Time, byte Status, byte Data1, byte Data2);

/// <summary>A Keysound part played by MIDI: a VST3 instrument, or a SoundFont preset.</summary>
public interface IMidiPart : ILivePart
{
    /// <summary>The track's notes and controllers in timeline ms, replacing the previous ones. Any thread.</summary>
    void SetEvents(IEnumerable<TimedMidi> events);

    /// <summary>A live MIDI message (a keyboard): played at the start of the next block. Any thread.</summary>
    void Live(byte status, byte data1, byte data2);
}

/// <summary>
/// A VST3 instrument running in its own dawsu_vst3_host process (native/Vst3IsolatedHost), so a crash takes down only
/// this instrument. Audio goes through shared memory one block at a time: if the host is late the block is silent; if
/// it exits the instrument is <see cref="IsCrashed"/> and silent until it is started again. Commands (editor window,
/// state) go through the host's stdin/stdout, each reply tagged with its request's number.
/// </summary>
public sealed class IsolatedVst3 : IMidiPart, IDisposable
{
    /// <summary>The protocol the host must speak (its READY line says which).</summary>
    public const int Protocol = 2; // 2: --offline and RENDER
    /// <summary>The largest saved state accepted, as for the in-process host.</summary>
    public const int MaxStateBytes = Vst3Instrument.MaxStateBytes;
    /// <summary>How much of a block's own duration the audio thread waits for the host before playing silence.</summary>
    private const double WaitShare = 0.8;

    private readonly Process process;
    private readonly MemoryMappedFile map;
    private readonly MemoryMappedViewAccessor view;
    private readonly EventWaitHandle request, done;
    private readonly SemaphoreSlim inputGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> pending = new();
    private readonly TaskCompletionSource<string> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<(byte Status, byte Data1, byte Data2)> live = new();
    private readonly List<(byte Status, byte Data1, byte Data2)> carried = []; // scheduled events of blocks skipped while late
    private readonly List<(byte Status, byte Data1, byte Data2)> overflow = []; // events past MaxEvents in the block being sent
    private readonly float[] left = new float[ShmLayout.MaxFrames], right = new float[ShmLayout.MaxFrames];
    private readonly HashSet<(int Channel, int Pitch)> sounding = []; // scheduled notes on, for release on stop
    private TimedMidi[] scheduled = [];
    private int nextCommand;
    private uint sequence;
    private bool waiting, begun, disposed;
    private volatile bool exited;

    public string Name { get; private set; } = "";
    /// <summary>A render host for Generate (<see cref="RenderAsync"/>): the plug-in runs in offline mode, with no deadline.</summary>
    public bool Offline { get; }
    public string PluginPath { get; }
    /// <summary>Blocks the host didn't render in time (silence instead).</summary>
    public int LateBlocks { get; private set; }
    public bool IsCrashed => !disposed && exited;
    /// <summary>The host exited unexpectedly. Raised on a thread-pool thread.</summary>
    public event Action? Crashed;

    /// <summary>
    /// A plug-in path dawsu! starts by itself: a fully qualified local .vst3. A project names the plug-in it runs, so a
    /// network share or another kind of file is refused.
    /// </summary>
    public static bool IsLoadablePath(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal)
        && path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);

    /// <summary>The host program next to dawsu!.</summary>
    public static string DefaultHostPath => Path.Combine(AppContext.BaseDirectory, "dawsu_vst3_host.exe");

    private IsolatedVst3(string pluginPath, string hostPath, string? classId, bool offline)
    {
        PluginPath = pluginPath;
        string name = $"dawsu-vst-{Environment.ProcessId}-{Guid.NewGuid():N}";
        map = MemoryMappedFile.CreateNew($@"Local\{name}", ShmLayout.Size);
        view = map.CreateViewAccessor(0, ShmLayout.Size);
        view.Write(ShmLayout.MagicOffset, ShmLayout.Magic);
        view.Write(ShmLayout.VersionOffset, (uint)Protocol);
        view.Write(ShmLayout.RateOffset, (double)PlaybackMixer.Rate);
        request = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}-req");
        done = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}-done");

        var start = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in new[] { "--host", name, Environment.ProcessId.ToString(), pluginPath }) start.ArgumentList.Add(arg);
        if (classId is not null) start.ArgumentList.Add(classId);
        if (offline) start.ArgumentList.Add("--offline");
        Offline = offline;
        process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            exited = true;
            ready.TrySetResult("ERROR\tThe instrument's host stopped while loading.");
            foreach (var r in pending.Values) r.TrySetResult("ERROR\tThe instrument's host stopped.");
            if (!disposed) Crashed?.Invoke();
        };
    }

    /// <summary>
    /// Starts a host for the instrument and waits until it is loaded (READY), then restores <paramref name="state"/>.
    /// Throws <see cref="InvalidOperationException"/> with the host's message when it can't load.
    /// </summary>
    public static async Task<IsolatedVst3> StartAsync(string pluginPath, string? classId = null, string? state = null, string? hostPath = null, TimeSpan? timeout = null, bool offline = false)
    {
        hostPath ??= DefaultHostPath;
        if (!IsLoadablePath(pluginPath)) throw new InvalidOperationException("Only a .vst3 on this computer can be loaded.");
        if (!File.Exists(hostPath)) throw new InvalidOperationException("The VST3 host (dawsu_vst3_host.exe) isn't installed next to dawsu!.");
        var vst = new IsolatedVst3(pluginPath, hostPath, classId, offline);
        try
        {
            vst.process.Start();
            _ = Task.Run(vst.ReadReplies);
            string first;
            try { first = await vst.ready.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { throw new InvalidOperationException("The instrument took too long to load."); }
            if (Tagged(first, "ERROR") is { } error) throw new InvalidOperationException(error);
            // READY \t <name> \t <protocol>
            var fields = Tagged(first, "READY")?.Split('\t') ?? throw new InvalidOperationException("The VST3 host didn't start.");
            if (fields.Length < 2 || fields[1] != Protocol.ToString())
                throw new InvalidOperationException("dawsu_vst3_host.exe doesn't match this version of dawsu!; reinstall dawsu!.");
            vst.Name = fields[0];
            if (!string.IsNullOrEmpty(state)) await vst.SetStateAsync(state);
            return vst;
        }
        catch
        {
            vst.Dispose();
            throw;
        }
    }

    // "TAG \t payload" → payload, or null for another tag.
    private static string? Tagged(string line, string tag) =>
        line.Length > tag.Length && line.StartsWith(tag, StringComparison.Ordinal) && line[tag.Length] == '\t' ? line[(tag.Length + 1)..] : null;

    private void ReadReplies()
    {
        try
        {
            while (process.StandardOutput.ReadLine() is { } line)
            {
                int tab = line.IndexOf('\t');
                // The first untagged line is READY or ERROR; later lines are "<id> \t <reply>".
                if (tab > 0 && int.TryParse(line.AsSpan(0, tab), out int id))
                {
                    if (pending.TryRemove(id, out var reply)) reply.TrySetResult(line[(tab + 1)..]);
                }
                else ready.TrySetResult(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task<string> CommandAsync(string command, TimeSpan timeout)
    {
        if (exited || disposed) throw new InvalidOperationException("The instrument isn't running.");
        int id = Interlocked.Increment(ref nextCommand);
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = reply;
        try
        {
            await inputGate.WaitAsync();
            try
            {
                await process.StandardInput.WriteLineAsync($"{id}\t{command}");
                await process.StandardInput.FlushAsync();
            }
            finally { inputGate.Release(); }

            var answer = await reply.Task.WaitAsync(timeout);
            return Tagged(answer, "ERROR") is { } error ? throw new InvalidOperationException(error) : answer;
        }
        catch (TimeoutException) { throw new InvalidOperationException("The instrument didn't answer."); }
        finally { pending.TryRemove(id, out _); } // a late answer to this request is dropped, never given to another
    }

    /// <summary>
    /// Renders one note on an offline host (<see cref="Offline"/>): silences what came before, plays the events (frames
    /// from the start) and keeps rendering until the sound stays below the threshold, or up to the limit.
    /// </summary>
    public async Task<RenderedAudio> RenderAsync(IEnumerable<(long Frame, byte Status, byte Data1, byte Data2)> events, RenderLimits limits, long lastEventFrame, TimeSpan? timeout = null)
    {
        if (!Offline) throw new InvalidOperationException("This instrument plays live; Generate renders on its own host.");
        string file = Path.Combine(Path.GetTempPath(), $"dawsu-render-{Guid.NewGuid():N}.f32");
        string list = string.Join(";", events.Select(e => $"{e.Frame}:{e.Status}:{e.Data1}:{e.Data2}"));
        long max = lastEventFrame + limits.MaxTailFrames;
        try
        {
            string reply = await CommandAsync(FormattableString.Invariant(
                $"RENDER\t{file}\t{max}\t{limits.QuietFrames}\t{limits.Threshold:R}\t{list}"), timeout ?? TimeSpan.FromMinutes(5));
            var fields = Tagged(reply, "RENDERED")?.Split('\t') ?? throw new InvalidOperationException("The instrument didn't render the note.");
            var bytes = await File.ReadAllBytesAsync(file);
            var stereo = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, stereo, 0, stereo.Length * 4);
            return new RenderedAudio(stereo, fields.Length > 1 && fields[1] == "1");
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The plug-in's names for its MIDI notes (VST 3 program pitch names, as DAWs read for drum maps); empty when it has
    /// none, or when the host is older than this command.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string>> NoteNamesAsync()
    {
        string reply;
        try { reply = await CommandAsync("NOTENAMES", TimeSpan.FromSeconds(30)); }
        catch (InvalidOperationException) { return new Dictionary<int, string>(); } // "Unknown command." from an older host
        var names = new Dictionary<int, string>();
        foreach (var field in (Tagged(reply, "NAMES") ?? "").Split('	'))
            if (field.IndexOf('=') is > 0 and var eq && int.TryParse(field.AsSpan(0, eq), System.Globalization.CultureInfo.InvariantCulture, out int pitch)
                && pitch is >= 0 and <= 127 && field.Length - eq - 1 is > 0 and <= 128)
                names[pitch] = field[(eq + 1)..];
        return names;
    }

    /// <summary>Opens the plug-in's own editor window (owned by its host process).</summary>
    public Task ShowEditorAsync() => CommandAsync("SHOW", TimeSpan.FromSeconds(30));
    public Task HideEditorAsync() => CommandAsync("HIDE", TimeSpan.FromSeconds(10));

    /// <summary>The plug-in's saved state (format byte, component and controller state), base64.</summary>
    public async Task<string> GetStateAsync() =>
        Tagged(await CommandAsync("GETSTATE", TimeSpan.FromSeconds(60)), "STATE") ?? throw new InvalidOperationException("The instrument didn't return its state.");

    public Task SetStateAsync(string base64)
    {
        if (base64.Length > (MaxStateBytes + 2) / 3 * 4 + 8) throw new InvalidOperationException("The saved instrument state is larger than 64 MB.");
        return CommandAsync("SETSTATE\t" + base64, TimeSpan.FromSeconds(60));
    }

    // ---- Playback ----

    /// <summary>What playback sends (timeline ms, sorted by time). Any thread.</summary>
    public void SetEvents(IEnumerable<TimedMidi> events) => Volatile.Write(ref scheduled, SortForPlayback(events));

    /// <summary>Events by time, note-offs before note-ons at a shared time (a repeated note retriggers).</summary>
    internal static TimedMidi[] SortForPlayback(IEnumerable<TimedMidi> events)
    {
        var sorted = events.ToArray();
        static int OnLast(TimedMidi e) => (e.Status & 0xF0) == 0x90 && e.Data2 > 0 ? 1 : 0;
        Array.Sort(sorted, (a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : OnLast(a).CompareTo(OnLast(b)));
        return sorted;
    }

    /// <summary>A live MIDI message (a keyboard): sent at the start of the next block. Any thread.</summary>
    public void Live(byte status, byte data1, byte data2) => live.Enqueue((status, data1, data2));

    public void ReleaseAll()
    {
        foreach (var (channel, pitch) in sounding) live.Enqueue(((byte)(0x80 | channel), (byte)pitch, 0));
        sounding.Clear();
    }

    /// <summary>
    /// Sends the block to the host without waiting, so several instruments render at the same time; the following
    /// <see cref="Render"/> of the same block collects the result.
    /// </summary>
    public void Begin(int frames, double start, double msPerFrame, bool playing)
    {
        if (frames > ShmLayout.MaxFrames) return; // Render splits longer blocks itself
        begun = Send(frames, start, msPerFrame, playing);
    }

    public void Render(Span<float> stereo, int frames, double start, double msPerFrame, bool playing)
    {
        if (begun)
        {
            begun = false;
            Collect(stereo, frames);
            return;
        }

        for (int offset = 0; offset < frames; offset += ShmLayout.MaxFrames)
        {
            int n = Math.Min(ShmLayout.MaxFrames, frames - offset);
            if (Send(n, start + offset * msPerFrame, msPerFrame, playing)) Collect(stereo.Slice(offset * 2, n * 2), n);
        }
    }

    // Writes one block's events and signals the host. False when nothing was sent (crashed, or still late).
    private bool Send(int frames, double start, double msPerFrame, bool playing)
    {
        if (disposed || exited) return false;
        if (waiting) // the previous block is still being rendered: don't queue another on top
        {
            if (!done.WaitOne(0))
            {
                // Its scheduled events go with the next block that is sent, so no note-off is lost (no hanging notes).
                if (playing && msPerFrame > 0) Scheduled(start, frames, msPerFrame, (_, s, d1, d2) => carried.Add((s, d1, d2)));
                LateBlocks++;
                return false;
            }

            waiting = false;
        }

        int count = 0;
        void Add(int offset, byte status, byte d1, byte d2)
        {
            // A full block (after a long stall, say) sends the rest with the next one, a block late, instead of dropping
            // them: a dropped note-off would leave the note hanging.
            if (count >= ShmLayout.MaxEvents) { overflow.Add((status, d1, d2)); return; }
            int at = ShmLayout.EventsOffset + count++ * ShmLayout.EventSize;
            view.Write(at, offset);
            view.Write(at + 4, status);
            view.Write(at + 5, d1);
            view.Write(at + 6, d2);
        }

        foreach (var (s, d1, d2) in carried) Add(0, s, d1, d2);
        carried.Clear();
        while (live.TryDequeue(out var e)) Add(0, e.Status, e.Data1, e.Data2);
        if (playing && msPerFrame > 0) Scheduled(start, frames, msPerFrame, Add);
        carried.AddRange(overflow); // first in the next block, in their order
        overflow.Clear();

        view.Write(ShmLayout.FramesOffset, frames);
        view.Write(ShmLayout.EventCountOffset, count);
        view.Write(ShmLayout.RequestSeqOffset, ++sequence);
        request.Set();
        return true;
    }

    // Waits for the sent block (at most most of its duration) and adds it in; a late block is silence.
    private void Collect(Span<float> stereo, int frames)
    {
        int timeout = Math.Max(5, (int)(frames * 1000.0 / PlaybackMixer.Rate * WaitShare));
        if (!done.WaitOne(timeout)) { waiting = true; LateBlocks++; return; }
        if (view.ReadInt32(ShmLayout.StatusOffset) != 0) return;
        view.ReadArray(ShmLayout.LeftOffset, left, 0, frames);
        view.ReadArray(ShmLayout.RightOffset, right, 0, frames);
        for (int i = 0; i < frames; i++)
        {
            stereo[i * 2] += left[i];
            stereo[i * 2 + 1] += right[i];
        }
    }

    // The scheduled events inside a block, with their frame offsets; tracks which notes are sounding.
    private void Scheduled(double start, int frames, double msPerFrame, Action<int, byte, byte, byte> add)
    {
        var list = Volatile.Read(ref scheduled);
        double end = start + frames * msPerFrame;
        for (int i = Sorted.LowerBound(list, start, e => e.Time); i < list.Length && list[i].Time < end; i++)
        {
            var e = list[i];
            add((int)Math.Clamp(Math.Round((e.Time - start) / msPerFrame), 0, frames - 1), e.Status, e.Data1, e.Data2);
            int kind = e.Status & 0xF0, channel = e.Status & 0x0F;
            if (kind == 0x90 && e.Data2 > 0) sounding.Add((channel, e.Data1));
            else if (kind is 0x80 or 0x90) sounding.Remove((channel, e.Data1));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("0\tQUIT");
                process.StandardInput.Flush();
                if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { }

        process.Dispose();
        view.Dispose();
        map.Dispose();
        request.Dispose();
        done.Dispose();
        inputGate.Dispose();
    }

    /// <summary>
    /// The instrument classes in a .vst3, from a short-lived host process (a plug-in that crashes while scanning
    /// can't take dawsu! down): (class id, name, vendor). Empty when it has none or can't be read.
    /// </summary>
    public static async Task<IReadOnlyList<(string Id, string Name, string Vendor)>> ScanAsync(string pluginPath, string? hostPath = null, TimeSpan? timeout = null)
    {
        hostPath ??= DefaultHostPath;
        if (!File.Exists(hostPath)) return [];
        var start = new ProcessStartInfo(hostPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("--scan");
        start.ArgumentList.Add(pluginPath);
        using var scan = Process.Start(start);
        if (scan is null) return [];
        var output = scan.StandardOutput.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        try { await scan.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            try { scan.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return [];
        }

        return [.. (await output).Split('\n').Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(f => f.Length >= 4 && f[0] == "CLASS").Select(f => (f[1], f[2], f[3]))];
    }
}

/// <summary>
/// The shared memory between dawsu! and dawsu_vst3_host (native/Vst3IsolatedHost/host.cpp, whose static_asserts check
/// the same numbers). tools/FakeVstHost links this file.
/// </summary>
public static class ShmLayout
{
    public const uint Magic = 0x31485644; // "DVH1"
    public const int MaxEvents = 1024, MaxFrames = 4096, EventSize = 8;
    public const int MagicOffset = 0, VersionOffset = 4, FramesOffset = 8, EventCountOffset = 12, RequestSeqOffset = 16, DoneSeqOffset = 20,
        StatusOffset = 24, RateOffset = 32, EventsOffset = 40;
    public const int LeftOffset = EventsOffset + MaxEvents * EventSize, RightOffset = LeftOffset + MaxFrames * 4, Size = RightOffset + MaxFrames * 4;
}
