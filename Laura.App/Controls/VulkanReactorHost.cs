using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Laura.App.Controls;

/// <summary>Hosts the optional native Vulkan child surface while keeping GPU work outside the MAUI dispatcher.</summary>
public sealed class VulkanReactorHost : IAsyncDisposable
{
    private nint _handle;
    private bool _unavailable, _disposed;
    private int _x, _y, _width = 1, _height = 1;
    /// <summary>Gets whether the hardware swapchain has presented at least one frame.</summary>
    public bool IsRendering => _handle != 0 && LauraVulkanStatus(_handle, out ulong frames) == 1 && frames > 0;
    /// <summary>Gets the observed Vulkan presentation count for isolated diagnostics.</summary>
    public ulong PresentedFrames { get { if (_handle == 0) { return 0; } _ = LauraVulkanStatus(_handle, out ulong frames); return frames; } }
    /// <summary>Positions the native child only when needed and publishes bounded visual inputs without a GPU wait.</summary>
    public bool Update(GraphicsView view, bool enabled, ReactorMode mode, float level)
    {
        if (!enabled || _unavailable || _disposed || view.Handler?.PlatformView is not FrameworkElement element || element.XamlRoot is null)
        { Hide(); return false; }
        try
        {
            if (_handle == 0)
            {
                if (view.Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window) { return false; }
                _handle = LauraVulkanCreate(WindowNative.GetWindowHandle(window));
                if (_handle == 0) { _unavailable = true; return false; }
            }
            if (LauraVulkanStatus(_handle, out _) < 0) { _unavailable = true; Hide(); return false; }
            var position = element.TransformToVisual(null).TransformPoint(new global::Windows.Foundation.Point());
            double scale = element.XamlRoot.RasterizationScale;
            _x = (int)Math.Round(position.X * scale); _y = (int)Math.Round(position.Y * scale);
            _width = Math.Max(1, (int)Math.Round(element.ActualWidth * scale)); _height = Math.Max(1, (int)Math.Round(element.ActualHeight * scale));
            LauraVulkanUpdate(_handle, _x, _y, _width, _height, 1, (int)mode, Math.Clamp(level, 0, 1));
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { _unavailable = true; return false; }
    }
    /// <summary>Suspends native presentation immediately when navigating away, hiding or disabling acceleration.</summary>
    public void Hide() { if (_handle != 0) { LauraVulkanUpdate(_handle, _x, _y, _width, _height, 0, 0, 0); } }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true; Hide(); nint handle = Interlocked.Exchange(ref _handle, 0);
        if (handle == 0) { return; }
        await Task.Run(() => LauraVulkanStop(handle)).ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() => LauraVulkanDestroy(handle)).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
    /// <summary>Creates a child HWND and starts asynchronous hardware initialization.</summary>
    [DllImport("Laura.VulkanRenderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint LauraVulkanCreate(nint parent);
    /// <summary>Publishes placement and animation inputs to the owned native surface.</summary>
    [DllImport("Laura.VulkanRenderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LauraVulkanUpdate(nint renderer, int x, int y, int width, int height, int visible, int mode, float level);
    /// <summary>Reads initialization status and actual frame presentation evidence.</summary>
    [DllImport("Laura.VulkanRenderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LauraVulkanStatus(nint renderer, out ulong frames);
    /// <summary>Drains Vulkan work on the calling worker thread.</summary>
    [DllImport("Laura.VulkanRenderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LauraVulkanStop(nint renderer);
    /// <summary>Destroys the child HWND on its owning UI thread after draining.</summary>
    [DllImport("Laura.VulkanRenderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LauraVulkanDestroy(nint renderer);
}
