using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Laura.Core.Screenshots;

namespace Laura.App.Platforms.Windows;

/// <summary>Captures the primary desktop with GDI and encodes PNG through WinRT, without Forms or System.Drawing.</summary>
public static class WindowsScreenCapture
{
    /// <summary>Returns a desktop PNG after the caller's trusted confirmation.</summary>
    public static async Task<byte[]> CaptureAsync(CancellationToken cancellationToken, bool jpeg = false)
    {
        using var frame = await CaptureFrameAsync(new(0, 0, GetSystemMetrics(0), GetSystemMetrics(1)), cancellationToken).ConfigureAwait(false);
        return await EncodeAsync(frame.Pixels.ToArray(), frame.Bounds.Width, frame.Bounds.Height, jpeg, cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Captures a bounded physical desktop rectangle, including negative monitor origins.</summary>
    public static Task<ScreenshotFrame> CaptureFrameAsync(ScreenshotBounds bounds, CancellationToken cancellationToken) =>
        Task.Run(() => CapturePixels(bounds), cancellationToken);
    /// <summary>Gets current physical monitor geometry; recalculate after display/DPI changes.</summary>
    public static ScreenshotBounds DesktopBounds(bool allDisplays = true)
    {
        if (allDisplays) { return new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(GetForegroundWindow(), 2), ref monitor)) { throw new InvalidOperationException("Display unavailable."); }
        return new(monitor.Monitor.Left, monitor.Monitor.Top, monitor.Monitor.Right - monitor.Monitor.Left, monitor.Monitor.Bottom - monitor.Monitor.Top);
    }
    /// <summary>Encodes flattened BGRA pixels in memory using the existing Windows codec; never creates temporary files.</summary>
    public static async Task<byte[]> EncodeAsync(byte[] pixels, int width, int height, bool jpeg, CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 32000000 || pixels.Length != checked(width * height * 4)) { throw new ArgumentException("Invalid screenshot pixels.", nameof(pixels)); }
        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);
        byte[] png = new byte[checked((int)stream.Size)];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)png.Length).AsTask(cancellationToken).ConfigureAwait(false);
        reader.ReadBytes(png);
        return png;
    }
    /// <summary>Reads top-down 32-bit primary-display pixels and releases every GDI resource.</summary>
    private static ScreenshotFrame CapturePixels(ScreenshotBounds bounds)
    {
        int width = bounds.Width, height = bounds.Height;
        if (width <= 0 || height <= 0 || (long)width * height > 32000000) { throw new InvalidOperationException("Screenshot exceeds the 32-megapixel limit."); }
        nint desktop = GetDC(0), memory = CreateCompatibleDC(desktop), bitmap = CreateCompatibleBitmap(desktop, width, height);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            if (desktop == 0 || memory == 0 || bitmap == 0 || !BitBlt(memory, 0, 0, width, height, desktop, bounds.X, bounds.Y, 0x40CC0020)) { throw new InvalidOperationException("Desktop capture failed."); }
            SelectObject(memory, previous);
            byte[] pixels = new byte[checked(width * height * 4)];
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) { throw new InvalidOperationException("Desktop pixels are unavailable."); }
            for (int index = 3; index < pixels.Length; index += 4) { pixels[index] = 255; }
            return new(bounds, pixels);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); _ = ReleaseDC(0, desktop); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        internal uint Size; internal int Width; internal int Height; internal ushort Planes; internal ushort BitCount;
        internal uint Compression; internal uint ImageSize; internal int XPixelsPerMeter; internal int YPixelsPerMeter; internal uint ColorsUsed; internal uint ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal NativeRect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}
