using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OsuHitsound.App;

/// <summary>
/// MTIPC, the mapping-tools IPC that clients such as un!stable serve on the named pipe "mtipc" (the protocol Mapping
/// Tools uses): each request is a 4-byte message number, each reply one pipe message. dawsu! uses the handshake (0,
/// answered with 1337), the current beatmap (3) and the editor reload (18).
/// </summary>
internal static class Mtipc
{
    private const string PipeName = "mtipc";
    private const int Hello = 0, ReadBeatmap = 3, ReloadEditor = 18, HelloReply = 1337, TimeoutMs = 500;

    /// <summary>The beatmap the client has open: its folder (relative to the Songs folder) and .osu file name.</summary>
    public sealed record Beatmap(string Folder, string OsuFile, string? ServerExecutable);

    // One connection, kept open and reused, as Mapping Tools does: the server takes a client at a time, and a new
    // connection right after one closes can be refused while it gets ready again.
    private static NamedPipeClientStream? connection;
    private static string? serverExecutable; // the program serving that connection; it can't change while it's open
    private static readonly Lock gate = new();

    /// <summary>Whether anything serves the pipe now (instant: no waiting for a connection that can't come).</summary>
    private static bool Served() => File.Exists(@"\\.\pipe\" + PipeName);

    /// <summary>The open beatmap, or null when no client serves MTIPC (or it has none open).</summary>
    public static Beatmap? TryReadBeatmap()
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (gate)
        {
            if (Send(ReadBeatmap) is not { } reply) return null;
            try
            {
                var (folder, file) = ParseBeatmap(reply);
                return folder.Length == 0 || file.Length == 0 ? null : new(folder, file, serverExecutable);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { return null; }
        }
    }

    /// <summary>Asks the client's editor to reload its map from disk; false when no client answers.</summary>
    public static bool TryReloadEditor()
    {
        if (!OperatingSystem.IsWindows()) return false;
        lock (gate) return Send(ReloadEditor) is not null;
    }

    /// <summary>One request on the kept connection, reconnecting once if it was closed; null when nothing answers.</summary>
    private static byte[]? Send(int message)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (connection is null)
                {
                    if (!Served()) return null; // nothing serves MTIPC
                    connection = Connect();
                    if (connection is null) return null;
                    serverExecutable = ServerExecutable(connection);
                }
                return Request(connection, message);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException)
            {
                connection?.Dispose();
                connection = null;
                if (attempt == 0) Thread.Sleep(150); // let the server listen again
            }
        }
        return null;
    }

    /// <summary>
    /// The reply to "read beatmap": slider multiplier and tick rate (doubles), four floats (AR, CS and two more), the
    /// folder and the .osu file name (length-prefixed UTF-8 strings), then more that dawsu! doesn't need.
    /// </summary>
    internal static (string Folder, string OsuFile) ParseBeatmap(byte[] reply)
    {
        using var reader = new BinaryReader(new MemoryStream(reply));
        reader.ReadDouble(); reader.ReadDouble();
        for (int i = 0; i < 4; i++) reader.ReadSingle();
        string folder = reader.ReadString(), file = reader.ReadString();
        if (folder.Length > 1024 || file.Length > 1024) throw new InvalidDataException("MTIPC sent an implausible beatmap name.");
        return (folder, file);
    }

    private static NamedPipeClientStream? Connect()
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.WriteThrough);
        try
        {
            pipe.Connect(TimeoutMs);
            pipe.ReadMode = PipeTransmissionMode.Message;
            var hello = Request(pipe, Hello);
            if (hello.Length < 4 || BitConverter.ToInt32(hello) != HelloReply) throw new InvalidDataException("MTIPC handshake failed.");
            return pipe;
        }
        catch (TimeoutException) { pipe.Dispose(); return null; } // nothing serves MTIPC
        catch { pipe.Dispose(); throw; }
    }

    private static byte[] Request(NamedPipeClientStream pipe, int message)
    {
        pipe.Write(BitConverter.GetBytes(message));
        pipe.Flush();
        var reply = new MemoryStream(); // not disposed here: a read that timed out may still finish into it
        var buffer = new byte[4096];
        Task.Run(() =>
        {
            do
            {
                int n = pipe.Read(buffer, 0, buffer.Length);
                if (n == 0) throw new EndOfStreamException("MTIPC closed the pipe.");
                reply.Write(buffer, 0, n);
                if (reply.Length > 1 << 20) throw new InvalidDataException("MTIPC sent too much.");
            }
            while (!pipe.IsMessageComplete);
        }).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return reply.ToArray();
    }

    /// <summary>The program serving the pipe (un!stable's osu!.exe), to find its Songs folder.</summary>
    private static string? ServerExecutable(NamedPipeClientStream pipe)
    {
        try
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)) return null;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
