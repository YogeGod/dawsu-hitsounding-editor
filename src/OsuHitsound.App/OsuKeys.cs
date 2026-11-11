using System.Runtime.InteropServices;

namespace OsuHitsound.App;

/// <summary>Keyboard input for osu!stable's window. osu! reads scan codes, so each key carries one as well as its key code.</summary>
internal static class OsuKeys
{
    private const ushort Control = 0x11, L = 0x4C, Enter = 0x0D;
    private const uint KeyUp = 0x0002;

    /// <summary>
    /// osu!stable's editor reload: brings osu! forward, then Ctrl+L (pressed repeatedly, as osu! may drop the first) and
    /// Enter for its confirmation. False when osu!'s window can't be brought forward.
    /// </summary>
    public static async Task<bool> SendEditorReload(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero) return false;
        if (GetForegroundWindow() != window)
        {
            if (IsIconic(window)) ShowWindow(window, 9); // restore a minimised osu!
            if (!SetForegroundWindow(window)) return false;
            await Task.Delay(300);
        }
        // Off the UI thread: the events go one at a time with short pauses, which would otherwise hold up dawsu!.
        await Task.Run(async () =>
        {
            Send(Control, false);
            for (int i = 0; i < 10; i++) { Send(L, false); Send(L, true); }
            Send(Control, true);
            await Task.Delay(100);
            Send(Enter, false); Send(Enter, true);
        });
        return true;
    }

    private static readonly int InputSize = Marshal.SizeOf<Input>();

    private static void Send(ushort key, bool up)
    {
        var input = new Input
        {
            Type = 1, // keyboard
            Keyboard = new KeyboardInput { VirtualKey = key, ScanCode = (ushort)MapVirtualKey(key, 0), Flags = up ? KeyUp : 0 },
        };
        SendInput(1, [input], InputSize);
        Thread.Sleep(1); // osu! sees the sequence reliably only one event at a time
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    // INPUT is a type tag and a union; the keyboard member is the one used, padded to the union's largest member (mouse).
    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
        [FieldOffset(8)] private MouseInputPadding padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPadding
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
