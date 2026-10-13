using System.Runtime.InteropServices;
using System.Text;

namespace Laura.Platform.Windows.System;

/// <summary>Provides structured native mouse, keyboard and window actions without generated shell scripts.</summary>
public static class WindowsComputerController
{
    /// <summary>Returns visible top-level windows for choosing a real target.</summary>
    public static IReadOnlyList<string> ListWindows()
    {
        var titles = new List<string>();
        EnumWindows((window, _) => { string title = Title(window); if (IsWindowVisible(window) && title.Length > 0) { titles.Add(title); } return true; }, 0);
        return titles;
    }
    /// <summary>Focuses a matching window and refuses ambiguous window names.</summary>
    public static bool Focus(string title)
    {
        var matches = new List<nint>();
        EnumWindows((window, _) => { if (IsWindowVisible(window) && Title(window).Contains(title, StringComparison.OrdinalIgnoreCase)) { matches.Add(window); } return true; }, 0);
        if (matches.Count != 1) { return false; }
        ShowWindow(matches[0], 9); return SetForegroundWindow(matches[0]);
    }
    /// <summary>Pins a real window and process before approval; Laura's own security UI cannot be automated by a model.</summary>
    public static WindowsInputTarget ResolveInputTarget(string title)
    {
        var matches = new List<nint>();
        if (title.Length == 0) { matches.Add(GetForegroundWindow()); }
        else { EnumWindows((window, _) => { if (IsWindowVisible(window) && Title(window).Contains(title, StringComparison.OrdinalIgnoreCase)) { matches.Add(window); } return true; }, 0); }
        if (matches.Count != 1 || !IsWindow(matches[0])) { throw new InvalidOperationException("Choose one actual window."); }
        _ = GetWindowThreadProcessId(matches[0], out uint process);
        if (process == Environment.ProcessId) { throw new InvalidOperationException("Model input cannot change Laura's own authorization UI."); }
        return new(matches[0].ToInt64(), process, Title(matches[0]));
    }
    /// <summary>Rechecks identity after approval and verifies foreground focus immediately before input.</summary>
    public static bool FocusVerified(WindowsInputTarget target)
    {
        nint handle = new(target.Handle);
        _ = GetWindowThreadProcessId(handle, out uint process);
        if (!IsWindow(handle) || process != target.ProcessId || process == Environment.ProcessId || Title(handle) != target.Title) { return false; }
        ShowWindow(handle, 9); return SetForegroundWindow(handle) && GetForegroundWindow() == handle;
    }
    /// <summary>Refuses mouse input redirected onto another top-level window.</summary>
    public static void ValidatePoint(WindowsInputTarget target, int x, int y)
    {
        if (GetAncestor(WindowFromPoint(new NativePoint { X = x, Y = y }), 2).ToInt64() != target.Handle)
        { throw new InvalidOperationException("Pointer coordinates no longer belong to the approved window."); }
    }
    /// <summary>Types Unicode literally into the active application, without interpreting command syntax.</summary>
    public static void Type(string text)
    {
        if (text.Length > 12000) { throw new ArgumentException("Text exceeds the typing limit.", nameof(text)); }
        foreach (char character in text) { Send(Key(0, character, 4), Key(0, character, 6)); }
    }
    /// <summary>Presses and releases only known virtual-key chords.</summary>
    public static void Hotkey(IReadOnlyList<string> names)
    {
        if (names.Count is 0 or > 5) { throw new ArgumentException("Invalid key chord.", nameof(names)); }
        ushort[] keys = names.Select(Code).ToArray();
        Send([.. keys.Select(key => Key(key, '\0', 0)), .. keys.Reverse().Select(key => Key(key, '\0', 2))]);
    }
    /// <summary>Clicks one bounded primary-display location.</summary>
    public static void Click(int x, int y, bool right = false, bool twice = false)
    {
        if (x < 0 || y < 0 || x >= GetSystemMetrics(0) || y >= GetSystemMetrics(1)) { throw new ArgumentOutOfRangeException(nameof(x)); }
        if (!SetCursorPos(x, y)) { throw new InvalidOperationException("The pointer could not be positioned."); }
        for (int i = 0; i < (twice ? 2 : 1); i++) { Send(Mouse(right ? 8u : 2u), Mouse(right ? 16u : 4u)); }
    }
    /// <summary>Scrolls a bounded number of mouse-wheel steps.</summary>
    public static void Scroll(int steps) => Send(Mouse(0x800, unchecked((uint)(Math.Clamp(steps, -20, 20) * 120))));
    /// <summary>Moves the pointer only to a valid primary-display pixel.</summary>
    public static void Move(int x, int y)
    { if (x < 0 || y < 0 || x >= GetSystemMetrics(0) || y >= GetSystemMetrics(1) || !SetCursorPos(x, y)) { throw new ArgumentOutOfRangeException(nameof(x)); } }
    /// <summary>Drags between explicit pixels and always releases the mouse button on cancellation.</summary>
    public static async Task DragAsync(int x, int y, int destinationX, int destinationY, CancellationToken token)
    {
        Move(destinationX, destinationY); Move(x, y); Send(Mouse(2));
        try { for (int step = 1; step <= 15; step++) { token.ThrowIfCancellationRequested(); Move(x + (destinationX - x) * step / 15, y + (destinationY - y) * step / 15); await Task.Delay(12, token).ConfigureAwait(false); } }
        finally { Send(Mouse(4)); }
    }
    /// <summary>Applies an existing local wallpaper image through the native Windows settings API.</summary>
    public static bool SetWallpaper(string path) => File.Exists(path) && SystemParametersInfo(0x14, 0, path, 3);
    /// <summary>Builds a mouse input with the native union layout.</summary>
    private static Input Mouse(uint flags, uint data = 0) => new() { Type = 0, Payload = new() { Mouse = new() { Flags = flags, Data = data } } };
    /// <summary>Builds a keyboard input with optional literal Unicode scan data.</summary>
    private static Input Key(ushort key, char scan, uint flags) => new() { Type = 1, Payload = new() { Keyboard = new() { Key = key, Scan = scan, Flags = flags } } };
    /// <summary>Checks native delivery rather than reporting an unobserved success.</summary>
    private static void Send(params Input[] inputs)
    { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) { throw new InvalidOperationException("Windows rejected input, possibly because the target is elevated."); } }
    /// <summary>Maps a finite, language-independent keyboard vocabulary.</summary>
    private static ushort Code(string name)
    {
        string key = name.Trim().ToUpperInvariant();
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) { return key[0]; }
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 12) { return (ushort)(0x6F + function); }
        return key switch { "CTRL" or "CONTROL" => 0x11, "ALT" => 0x12, "SHIFT" => 0x10, "WIN" or "WINDOWS" => 0x5B,
            "ENTER" or "RETURN" => 0x0D, "ESC" or "ESCAPE" => 0x1B, "TAB" => 9, "SPACE" => 0x20, "BACKSPACE" => 8,
            "DELETE" => 0x2E, "HOME" => 0x24, "END" => 0x23, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "VOLUMEUP" => 0xAF, "VOLUMEDOWN" => 0xAE, "VOLUMEMUTE" => 0xAD, "PLAYPAUSE" => 0xB3,
            "PLUS" => 0xBB, "MINUS" => 0xBD,
            _ => throw new ArgumentException("Unsupported keyboard key.", nameof(name)) };
    }
    /// <summary>Reads a window title for targeting without inspecting another app's private data.</summary>
    private static string Title(nint window) { char[] title = new char[512]; int length = GetWindowText(window, title, title.Length); return length > 0 ? new string(title, 0, length) : ""; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputPayload Payload; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Explicit)] private struct InputPayload { [FieldOffset(0)] internal MouseInput Mouse; [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal nuint Extra; }
    private delegate bool EnumProcedure(nint window, nint data);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flag);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProcedure procedure, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, [Out] char[] text, int length);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
/// <summary>A window identity captured before a user approval. Changing titles/processes invalidates pending input.</summary>
public sealed record WindowsInputTarget(long Handle, uint ProcessId, string Title);
