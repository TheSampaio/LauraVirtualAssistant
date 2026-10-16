using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Laura.Platform.Windows.System;

/// <summary>Verifies normal close against owned hidden Win32 fixture windows without touching user applications.</summary>
internal static class ApplicationCloseSmoke
{
    private static readonly WindowProcedure Procedure = HandleMessage;
    private static bool _holdClose;

    /// <summary>Creates an invisible synthetic window; refusing WM_CLOSE simulates an application awaiting user work.</summary>
    internal static int RunFixture(bool holdClose)
    {
        _holdClose = holdClose; string title = "Laura close fixture " + Environment.ProcessId;
        var windowClass = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = GetModuleHandle(null), Name = title };
        if (RegisterClass(ref windowClass) == 0) { throw new InvalidOperationException("Fixture window class registration failed."); }
        nint window = CreateWindow(0, title, title, 0, 0, 0, 160, 100, 0, 0, windowClass.Instance, 0);
        if (window == 0) { throw new InvalidOperationException("Fixture window creation failed."); }
        Console.WriteLine(JsonSerializer.Serialize(new WindowsInputTarget(window.ToInt64(), (uint)Environment.ProcessId, title))); Console.Out.Flush();
        while (GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        return 0;
    }

    /// <summary>Checks real close, identity mismatch rejection and an unresolved close without force termination by the adapter.</summary>
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (bool hold in new[] { false, true })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(typeof(ApplicationCloseSmoke).Assembly.Location); start.ArgumentList.Add("--window-fixture"); start.ArgumentList.Add(hold ? "hold" : "normal");
            using var process = new Process { StartInfo = start }; process.Start();
            try
            {
                string line = await process.StandardOutput.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                    ?? throw new InvalidOperationException("Fixture did not publish its native identity.");
                WindowsInputTarget target = JsonSerializer.Deserialize<WindowsInputTarget>(line)!;
                bool rejected = false;
                try { await WindowsComputerController.CloseVerifiedAsync(target with { Title = "Changed identity" }, cancellationToken); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) { throw new InvalidOperationException("An altered application identity was allowed to close."); }
                bool closed = await WindowsComputerController.CloseVerifiedAsync(target, cancellationToken);
                if (closed == hold || hold && process.HasExited) { throw new InvalidOperationException("Normal close or unresolved-document behavior was misreported."); }
                if (!hold) { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
            }
        }
        Console.WriteLine("Owned hidden native windows: normal close, changed identity rejection and unresolved close without adapter force-kill verified.");
    }

    /// <summary>Processes only the synthetic fixture's close/destroy events.</summary>
    private static nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == 0x0010) { if (!_holdClose) { DestroyWindow(window); } return 0; }
        if (message == 0x0002) { PostQuitMessage(0); return 0; }
        return DefWindowProc(window, message, wParam, lParam);
    }
    /// <summary>Defines the native synthetic-window callback signature.</summary>
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style; internal nint Procedure; internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background; internal string? Menu; internal string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    { internal nint Window; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal int X, Y; internal uint Private; }
    /// <summary>Returns the fixture executable module identity.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
    /// <summary>Registers the owned fixture class.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW")] private static extern ushort RegisterClass(ref WindowClass windowClass);
    /// <summary>Creates the invisible fixture window.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindow(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameters);
    /// <summary>Handles messages unrelated to the fixture close scenario.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    /// <summary>Destroys an owned synthetic window after normal close.</summary>
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    /// <summary>Terminates the synthetic message loop.</summary>
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    /// <summary>Reads the next fixture message.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, nint window, uint first, uint last);
    /// <summary>Translates native fixture messages.</summary>
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    /// <summary>Dispatches a fixture message to its class callback.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
}
