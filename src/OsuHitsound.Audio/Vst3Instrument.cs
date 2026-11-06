using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace OsuHitsound.Audio;

/// <summary>
/// Experimental Windows x64 VST3 bridge. Lifecycle, state and GUI calls belong to one STA owner.
/// Process is offline/owner-only; EnableRealtime permits IDrumInstrument.Process on one audio
/// consumer while attached to PlaybackMixer. Detach before changing state or disposing.
/// Native plug-in crashes are not isolated.
/// </summary>
public sealed class Vst3Instrument : IDisposable, IDrumInstrument
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct MidiEvent(int Offset, int Status, int Data1, int Data2);

    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly int blockSize;
    private IntPtr handle;
    private bool realtime;
    public const int MaxStateBytes = 64 * 1024 * 1024;

    public Vst3Instrument(string path, double sampleRate = 44100, int blockSize = 512)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The experimental VST3 host requires Windows x64.");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Create the VST3 host on an STA thread.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        if (!path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) || (!Directory.Exists(path) && !File.Exists(path)))
            throw new ArgumentException("Choose an installed .vst3 instrument.", nameof(path));
        if (!double.IsFinite(sampleRate) || sampleRate is < 8000 or > 384000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (blockSize is < 16 or > 8192) throw new ArgumentOutOfRangeException(nameof(blockSize));
        this.blockSize = blockSize;
        var descriptor = ScanInstrument(path);
        var error = new byte[2048];
        handle = Native.Open(path, descriptor, Encoding.UTF8.GetByteCount(descriptor), sampleRate, blockSize, error, error.Length);
        if (handle == IntPtr.Zero) throw new InvalidOperationException($"VST3 load failed: {Encoding.UTF8.GetString(error).TrimEnd('\0')}");
    }

    private static string ScanInstrument(string path)
    {
        var scannerPath = Path.Combine(AppContext.BaseDirectory, "dawsu_vst3_scan.exe");
        if (!File.Exists(scannerPath)) throw new FileNotFoundException("The isolated VST3 scanner is missing.", scannerPath);
        var output = Path.GetTempFileName();
        try
        {
            var start = new ProcessStartInfo(scannerPath) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(output);
            using var scanner = System.Diagnostics.Process.Start(start) ?? throw new IOException("The VST3 scanner could not start.");
            if (!scanner.WaitForExit(30000))
            {
                scanner.Kill(entireProcessTree: true);
                scanner.WaitForExit();
                throw new TimeoutException("The VST3 instrument scan timed out.");
            }
            if (scanner.ExitCode != 0) throw new IOException($"The VST3 instrument scan failed ({scanner.ExitCode}).");
            if (new FileInfo(output).Length is <= 0 or > 65535) throw new InvalidDataException("Invalid VST3 description size.");
            return File.ReadAllText(output, Encoding.UTF8);
        }
        finally { File.Delete(output); }
    }

    public void Process(float[] stereo, int frames, MidiEvent[] events)
    {
        Check();
        ArgumentNullException.ThrowIfNull(stereo);
        ArgumentNullException.ThrowIfNull(events);
        if (frames < 1 || frames > blockSize || stereo.Length < frames * 2)
            throw new ArgumentOutOfRangeException(nameof(frames));
        if (events.Length > 4096 || events.Any(e => e.Offset < 0 || e.Offset >= frames ||
                e.Status is < 0x80 or >= 0xf0 || e.Data1 is < 0 or > 127 || e.Data2 is < 0 or > 127))
            throw new ArgumentException("Invalid block-relative MIDI event.", nameof(events));
        if (Native.Process(handle, stereo, frames, events, events.Length) != 1)
            throw new InvalidOperationException("VST3 processing failed; discard this block.");
    }

    public byte[] SaveState()
    {
        Check();
        var size = Native.State(handle, null, 0, 0);
        if (size is <= 0 or > MaxStateBytes) throw new InvalidOperationException("Invalid VST3 state size.");
        var data = new byte[size];
        var written = Native.State(handle, data, data.Length, 0);
        if (written <= 0 || written > data.Length) throw new InvalidOperationException("VST3 state changed size; retry saving.");
        return written == size ? data : data[..written];
    }

    /// <summary>Owner thread, while detached. Processing can then run on one audio thread.
    /// Detach from the mixer before state operations or disposal.</summary>
    public void EnableRealtime()
    {
        Check();
        if (Native.Realtime(handle) != 1) throw new InvalidOperationException("The instrument could not start live processing.");
        realtime = true;
    }

    bool IDrumInstrument.Process(float[] stereo, int frames, MidiEvent[] events, int count) =>
        realtime && handle != IntPtr.Zero && frames > 0 && frames <= blockSize && stereo.Length >= frames * 2 &&
        count >= 0 && count <= Math.Min(events.Length, 4096) && Native.ProcessRealtime(handle, stereo, frames, events, count) == 1;

    public void RestoreState(byte[] state)
    {
        Check();
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length is <= 0 or > MaxStateBytes) throw new ArgumentOutOfRangeException(nameof(state));
        if (Native.State(handle, state, state.Length, 1) != state.Length)
            throw new InvalidOperationException("VST3 state restore failed.");
    }

    public void ShowEditor()
    {
        Check();
        if (Native.Editor(handle, 1) != 1) throw new InvalidOperationException("This instrument could not open its editor.");
    }

    public void Pump(int milliseconds = 10)
    {
        Check();
        if (milliseconds is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (Native.Pump(handle, milliseconds) != 1) throw new InvalidOperationException("VST3 event dispatch failed.");
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != owner)
            throw new InvalidOperationException("VST3 calls must remain on the creating thread.");
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        Check();
        if (Native.Close(handle) != 1) throw new InvalidOperationException("VST3 shutdown failed.");
        handle = IntPtr.Zero;
    }

    private static class Native
    {
        private const string Library = "dawsu_vst3";
        [DllImport(Library, EntryPoint = "dv_realtime", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int Realtime(IntPtr handle);
        [DllImport(Library, EntryPoint = "dv_process_realtime", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int ProcessRealtime(IntPtr handle, [Out] float[] stereo, int frames, [In] MidiEvent[] midi, int count);
        [DllImport(Library, EntryPoint = "dv_open_v2", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern IntPtr Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string descriptor, int descriptorBytes, double rate, int block,
            [Out] byte[] error, int capacity);
        [DllImport(Library, EntryPoint = "dv_close", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int Close(IntPtr handle);
        [DllImport(Library, EntryPoint = "dv_process", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int Process(IntPtr handle, [Out] float[] stereo, int frames, [In] MidiEvent[] midi, int count);
        [DllImport(Library, EntryPoint = "dv_state", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int State(IntPtr handle, [In, Out] byte[]? state, int length, int restore);
        [DllImport(Library, EntryPoint = "dv_editor", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int Editor(IntPtr handle, int visible);
        [DllImport(Library, EntryPoint = "dv_pump", CallingConvention = CallingConvention.Cdecl)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
        internal static extern int Pump(IntPtr handle, int milliseconds);
    }
}
