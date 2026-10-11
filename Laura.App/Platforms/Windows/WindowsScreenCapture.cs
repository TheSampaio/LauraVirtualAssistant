using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Laura.App.Platforms.Windows;

/// <summary>Captures the primary desktop with GDI and encodes PNG through WinRT, without Forms or System.Drawing.</summary>
public static class WindowsScreenCapture
{
    /// <summary>Returns a desktop PNG after the caller's trusted confirmation.</summary>
    public static async Task<byte[]> CaptureAsync(CancellationToken cancellationToken, bool jpeg = false)
    {
        (byte[] pixels, int width, int height) = await Task.Run(CapturePixels, cancellationToken).ConfigureAwait(false);
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
    private static (byte[], int, int) CapturePixels()
    {
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
        nint desktop = GetDC(0), memory = CreateCompatibleDC(desktop), bitmap = CreateCompatibleBitmap(desktop, width, height);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, desktop, 0, 0, 0x40CC0020)) { throw new InvalidOperationException("Desktop capture failed."); }
            SelectObject(memory, previous);
            byte[] pixels = new byte[checked(width * height * 4)];
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) { throw new InvalidOperationException("Desktop pixels are unavailable."); }
            return (pixels, width, height);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); _ = ReleaseDC(0, desktop); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        internal uint Size; internal int Width; internal int Height; internal ushort Planes; internal ushort BitCount;
        internal uint Compression; internal uint ImageSize; internal int XPixelsPerMeter; internal int YPixelsPerMeter; internal uint ColorsUsed; internal uint ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
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
