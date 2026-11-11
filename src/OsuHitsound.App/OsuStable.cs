using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace OsuHitsound.App;

/// <summary>What osu!stable is doing, and the beatmap it has loaded (its folder in the Songs folder and its .osu name).</summary>
public sealed record OsuStableState(int Status, string? Folder, string? OsuFile)
{
    public const int Editor = 1;
    public bool InEditor => Status == Editor;
    /// <summary>The osu!.exe it runs from, to find its Songs folder when Preferences has none.</summary>
    public string? Executable { get; init; }
}

/// <summary>Reads a 32-bit process's memory, so the reader can be tested against made-up memory.</summary>
internal interface IProcessMemory
{
    bool Read(uint address, Span<byte> into);
    /// <summary>Where each pattern first occurs in the process's code (null for one that doesn't), in one pass.</summary>
    uint?[] Find(params byte[][] patterns);
}

/// <summary>
/// osu!stable's current beatmap and state, read from its memory. dawsu!'s own reader: it uses the well-known layout
/// (the same facts tools like gosumemory and Mapping Tools rely on) and no third-party reader code.
/// <list type="bullet">
/// <item>A code pattern (F8 01 74 04 83 65) marks the "base"; 0xC bytes before it is a pointer to the variable that
/// holds the current beatmap object.</item>
/// <item>In that object the folder name and the .osu file name are .NET strings at +0x78 and +0x90.</item>
/// <item>Another pattern (48 83 F8 04 73 1E) is followed back 4 bytes to a pointer to the status (1 = editor).</item>
/// </list>
/// The layout belongs to osu!stable builds; a build that moves it reads as "can't read", never as a wrong map.
/// </summary>
internal sealed class OsuStableReader(IProcessMemory memory)
{
    internal static readonly byte[] BasePattern = [0xF8, 0x01, 0x74, 0x04, 0x83, 0x65];
    internal static readonly byte[] StatusPattern = [0x48, 0x83, 0xF8, 0x04, 0x73, 0x1E];
    internal const int FolderOffset = 0x78, OsuFileOffset = 0x90;
    private const int MaxStringChars = 1024;

    private uint? baseAddress, statusAddress;

    /// <summary>The state and current beatmap, or null when the layout can't be found or read.</summary>
    public OsuStableState? Read()
    {
        if (baseAddress is null || statusAddress is null)
        {
            var found = memory.Find(BasePattern, StatusPattern);
            (baseAddress, statusAddress) = (found[0], found[1]);
        }
        if (baseAddress is not { } b || statusAddress is not { } s) return null;
        // [[status - 4]] is the status; [[base - 0xC]] the current beatmap object (0 while none is loaded).
        if (Pointer(s - 4) is not { } statusPointer || Int(statusPointer) is not { } status || Pointer(b - 0xC) is not { } holder)
        {
            baseAddress = statusAddress = null; // the code moved (osu! updated while running): find it again next time
            return null;
        }
        var beatmap = Pointer(holder);
        return beatmap is null or 0 ? new(status, null, null)
            : new(status, String(beatmap.Value + FolderOffset), String(beatmap.Value + OsuFileOffset));
    }

    private uint? Pointer(uint address) => Int(address) is { } v ? (uint)v : null;

    private int? Int(uint address)
    {
        Span<byte> bytes = stackalloc byte[4];
        return memory.Read(address, bytes) ? BitConverter.ToInt32(bytes) : null;
    }

    /// <summary>A .NET string (32-bit runtime): [field] → object; +4 is its length, +8 its UTF-16 characters.</summary>
    private string? String(uint field)
    {
        if (Pointer(field) is not { } obj || obj == 0 || Int(obj + 4) is not { } length || length is < 0 or > MaxStringChars) return null;
        var chars = new byte[length * 2];
        return memory.Read(obj + 8, chars) ? Encoding.Unicode.GetString(chars) : null;
    }
}

/// <summary>Finds a running osu!stable (32-bit; osu!lazer and un!stable have the same process name but are 64-bit) and reads it.</summary>
public static class OsuStable
{
    // Per process: its reader, or null for one that isn't osu!stable (or whose layout wasn't found), so a 64-bit client
    // or an unreadable build isn't opened and scanned again on every call; those are retried after a while.
    private static readonly Dictionary<int, (OsuStableReader? Reader, string? Exe, DateTime RetryAt)> known = [];
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);
    private static int stablePid;
    private static readonly Lock gate = new();

    /// <summary>osu!stable's state, or null when it isn't running or can't be read.</summary>
    public static OsuStableState? TryRead()
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (gate)
        {
            var running = Process.GetProcessesByName("osu!");
            try
            {
                foreach (int gone in known.Keys.Except(running.Select(p => p.Id)).ToList()) known.Remove(gone);
                foreach (var process in running)
                {
                    if (!known.TryGetValue(process.Id, out var entry) || entry.Reader is null && DateTime.UtcNow >= entry.RetryAt)
                        known[process.Id] = entry = Attach(process);
                    if (entry.Reader?.Read() is { } state) { stablePid = process.Id; return state with { Executable = entry.Exe }; }
                    if (entry.Reader is not null) known[process.Id] = (null, entry.Exe, DateTime.UtcNow + RetryAfter); // layout gone: wait before scanning again
                }
            }
            finally { foreach (var p in running) p.Dispose(); }
            stablePid = 0;
            return null;
        }
    }

    private static (OsuStableReader?, string?, DateTime) Attach(Process process)
    {
        var retryAt = DateTime.UtcNow + RetryAfter;
        if (WindowsProcessMemory.Open(process.Id) is not { } memory) return (null, null, retryAt); // 64-bit or no access
        string? exe = null;
        try { exe = process.MainModule?.FileName; } catch (Win32Exception) { } catch (InvalidOperationException) { }
        var reader = new OsuStableReader(memory);
        return reader.Read() is null ? (null, exe, retryAt) : (reader, exe, retryAt);
    }

    /// <summary>The window of the osu!stable last read, for sending it the editor's reload keys.</summary>
    public static IntPtr Window()
    {
        if (stablePid == 0) return IntPtr.Zero;
        try { using var process = Process.GetProcessById(stablePid); return process.MainWindowHandle; }
        catch (ArgumentException) { return IntPtr.Zero; } // it has exited
    }
}

/// <summary>Read-only access to a 32-bit process on Windows (ReadProcessMemory, VirtualQueryEx).</summary>
internal sealed class WindowsProcessMemory : IProcessMemory
{
    private const uint QueryInformation = 0x0400, VmRead = 0x0010;
    private const uint MemCommit = 0x1000, PageGuard = 0x100, PageNoAccess = 0x01;
    private const uint ExecuteFlags = 0x10 | 0x20 | 0x40 | 0x80; // EXECUTE, EXECUTE_READ, EXECUTE_READWRITE, EXECUTE_WRITECOPY
    private const int ChunkBytes = 4 * 1024 * 1024;
    private readonly SafeProcessHandle handle;

    private WindowsProcessMemory(SafeProcessHandle handle) => this.handle = handle;

    /// <summary>A handle to a 32-bit process; null for a 64-bit one or one that can't be opened.</summary>
    public static WindowsProcessMemory? Open(int pid)
    {
        var h = OpenProcess(QueryInformation | VmRead, false, (uint)pid);
        if (h.IsInvalid) { h.Dispose(); return null; }
        if (!IsWow64Process(h, out bool wow64) || !wow64) { h.Dispose(); return null; }
        return new WindowsProcessMemory(h);
    }

    public bool Read(uint address, Span<byte> into)
    {
        if (address == 0) return false;
        var bytes = new byte[into.Length];
        if (!ReadProcessMemory(handle, (IntPtr)address, bytes, (IntPtr)bytes.Length, out var read) || (long)read != bytes.Length) return false;
        bytes.CopyTo(into);
        return true;
    }

    /// <summary>Scans the process's executable memory (where osu!'s compiled code is) for the patterns, in one pass.</summary>
    public uint?[] Find(params byte[][] patterns)
    {
        var found = new uint?[patterns.Length];
        int overlap = patterns.Max(p => p.Length);
        var buffer = new byte[ChunkBytes];
        ulong address = 0x10000;
        while (found.Any(f => f is null) && address < 0x7FFF0000
               && VirtualQueryEx(handle, (IntPtr)address, out var region, (IntPtr)Marshal.SizeOf<MemoryBasicInformation>()) != IntPtr.Zero)
        {
            ulong start = (ulong)region.BaseAddress, size = (ulong)region.RegionSize;
            if (size == 0) break;
            if (region.State == MemCommit && (region.Protect & ExecuteFlags) != 0 && (region.Protect & (PageGuard | PageNoAccess)) == 0)
            {
                for (ulong offset = 0; offset < size && found.Any(f => f is null); offset += (ulong)(buffer.Length - overlap))
                {
                    int length = (int)Math.Min((ulong)buffer.Length, size - offset);
                    bool ok = ReadProcessMemory(handle, (IntPtr)(start + offset), buffer, (IntPtr)length, out var count);
                    int read = (int)(long)count;
                    if (!ok && read <= 0) break;
                    for (int i = 0; i < patterns.Length; i++)
                        if (found[i] is null && buffer.AsSpan(0, read).IndexOf(patterns[i]) is >= 0 and var at)
                            found[i] = (uint)(start + offset + (ulong)at);
                    if (length < buffer.Length) break;
                }
            }
            address = start + size;
        }
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process(SafeProcessHandle process, out bool wow64);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualQueryEx(SafeProcessHandle process, IntPtr address, out MemoryBasicInformation info, IntPtr length);
}
