using System.Runtime.InteropServices;
using Laura.Core.Screenshots;
using Laura.Platform.Windows.System;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Laura.App.Platforms.Windows;

/// <summary>One-shot native WGC window capture with a bounded frame wait and deterministic COM/graphics cleanup.</summary>
public static class WindowsWindowCapture
{
    /// <summary>Captures only an identity-verified window; protected/unavailable windows are rejected, never bypassed with PrintWindow.</summary>
    public static async Task<ScreenshotFrame> CaptureAsync(WindowsInputTarget target, CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported()) { throw new InvalidOperationException("Windows Graphics Capture is unavailable."); }
        nint handle = new(target.Handle);
        _ = GetWindowThreadProcessId(handle, out uint process);
        if (!IsWindow(handle) || process != target.ProcessId || GetWindowDisplayAffinity(handle, out uint affinity) && affinity != 0)
        { throw new InvalidOperationException("The selected window closed, changed or prohibits capture."); }
        GraphicsCaptureItem item = CreateItem(handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0 || (long)item.Size.Width * item.Size.Height > 32000000) { throw new InvalidOperationException("Invalid window capture dimensions."); }
        using IDirect3DDevice device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var completed = new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        int processing = 0;
        async void Arrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (Interlocked.Exchange(ref processing, 1) != 0) { return; }
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) { throw new InvalidOperationException("Window frame unavailable."); }
                if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height) { throw new InvalidOperationException("Window dimensions changed during capture."); }
                var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask(cancellationToken).ConfigureAwait(false);
                if (!completed.TrySetResult(bitmap)) { bitmap.Dispose(); }
            }
            catch (Exception error) { completed.TrySetException(error); }
        }
        pool.FrameArrived += Arrived;
        SoftwareBitmap? acquired = null;
        try
        {
            session.StartCapture();
            using var bitmap = acquired = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            _ = GetWindowThreadProcessId(handle, out uint currentProcess);
            if (!IsWindow(handle) || currentProcess != target.ProcessId || GetWindowDisplayAffinity(handle, out uint currentAffinity) && currentAffinity != 0) { throw new InvalidOperationException("Selected window became unavailable during capture."); }
            using var bgra = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            byte[] pixels = new byte[checked(bgra.PixelWidth * bgra.PixelHeight * 4)];
            var buffer = new global::Windows.Storage.Streams.Buffer((uint)pixels.Length); bgra.CopyToBuffer(buffer);
            using (var reader = DataReader.FromBuffer(buffer)) { reader.ReadBytes(pixels); }
            for (int index = 3; index < pixels.Length; index += 4) { pixels[index] = 255; }
            return new(new(0, 0, bgra.PixelWidth, bgra.PixelHeight), pixels);
        }
        finally
        {
            pool.FrameArrived -= Arrived; completed.TrySetCanceled(cancellationToken);
            if (acquired is null && completed.Task.IsCompletedSuccessfully) { completed.Task.Result.Dispose(); }
        }
    }
    private static unsafe GraphicsCaptureItem CreateItem(nint window)
    {
        nint name = 0, factory = 0, item = 0;
        try
        {
            string runtime = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtime, (uint)runtime.Length, out name));
            Guid interop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"); Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref interop, out factory));
            Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            Marshal.ThrowExceptionForHR(create(factory, window, &itemId, &item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally { if (item != 0) { Marshal.Release(item); } if (factory != 0) { Marshal.Release(factory); } if (name != 0) { _ = WindowsDeleteString(name); } }
    }
    private static IDirect3DDevice CreateDevice()
    {
        nint native = 0, context = 0, dxgi = 0, projected = 0;
        try
        {
            int result = D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out native, out _, out context);
            if (result < 0) { result = D3D11CreateDevice(0, 5, 0, 0x20, 0, 0, 7, out native, out _, out context); }
            Marshal.ThrowExceptionForHR(result); Guid iid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(projected);
        }
        finally { if (projected != 0) { Marshal.Release(projected); } if (dxgi != 0) { Marshal.Release(dxgi); } if (context != 0) { Marshal.Release(context); } if (native != 0) { Marshal.Release(native); } }
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, uint length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driver, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint feature, out nint context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
