using System.Runtime.InteropServices;
using Laura.Core.Screenshots;
using Laura.Core.Abstractions;
using Laura.Platform.Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using XRect = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace Laura.App.Platforms.Windows;

internal sealed record ScreenshotSelection(ScreenshotBounds Bounds, WindowsInputTarget? Window);

/// <summary>Temporary physical-desktop selection, independent of the primary window and DPI transforms.</summary>
internal sealed class ScreenshotSelectionWindow
{
    private readonly Microsoft.UI.Xaml.Window _window;
    private readonly XCanvas _canvas;
    private readonly ScreenshotFrame _frame;
    private readonly ScreenshotMode _mode;
    private readonly XRect _outline = new() { StrokeThickness = 2, Stroke = WindowsDialogs.Brush(Theming.Theme.Accent), Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
    private readonly TextBlock _dimensions = WindowsDialogs.Text("", 14);
    private readonly XRect[] _shade = Enumerable.Range(0, 4).Select(_ => new XRect { Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false }).ToArray();
    private readonly TaskCompletionSource<ScreenshotSelection?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScreenshotPoint _start;
    private ScreenshotBounds _selection, _before;
    private bool _drag, _move;
    private WindowsInputTarget? _target;
    private readonly nint _handle;
    private ScreenshotSelectionWindow(ScreenshotFrame frame, ScreenshotMode mode, IUiLocalizer ui)
    {
        _frame = frame; _mode = mode;
        _window = WindowsDialogs.Create("LAURA Screenshot"); _handle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _canvas = new XCanvas { Width = frame.Bounds.Width, Height = frame.Bounds.Height, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 0)) };
        _canvas.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = ScreenshotRendering.Bitmap(frame), Width = frame.Bounds.Width, Height = frame.Bounds.Height, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false });
        foreach (var shade in _shade) { _canvas.Children.Add(shade); } _canvas.Children.Add(_outline);
        var root = new Microsoft.UI.Xaml.Controls.Grid(); root.Children.Add(new Viewbox { Child = _canvas, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill });
        var actions = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom, Margin = new Microsoft.UI.Xaml.Thickness(16) };
        var edit = WindowsDialogs.Button(ui.Get("ui.screenshot.edit"), Finish, true);
        actions.Children.Add(_dimensions); actions.Children.Add(edit); actions.Children.Add(WindowsDialogs.Button(ui.Get("ui.screenshot.cancel"), _window.Close));
        root.Children.Add(new Microsoft.UI.Xaml.Controls.Border { Child = actions, Background = WindowsDialogs.Brush(Theming.Theme.Background), HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom });
        var help = WindowsDialogs.Text(ui.Get(mode == ScreenshotMode.Window ? "ui.screenshot.windowHelp" : "ui.screenshot.selectionHelp"), 14);
        root.Children.Add(new Microsoft.UI.Xaml.Controls.Border { Child = help, Background = WindowsDialogs.Brush(Theming.Theme.Background), Padding = new Microsoft.UI.Xaml.Thickness(12), Margin = new Microsoft.UI.Xaml.Thickness(16), VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left });
        _window.Content = root; root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter) { presenter.SetBorderAndTitleBar(false, false); presenter.IsAlwaysOnTop = true; presenter.IsResizable = false; }
        _window.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(frame.Bounds.X, frame.Bounds.Y, frame.Bounds.Width, frame.Bounds.Height));
        _canvas.PointerPressed += Pressed; _canvas.PointerMoved += Moved; _canvas.PointerReleased += Released;
        _canvas.PointerCanceled += (_, _) => _drag = false; _canvas.PointerCaptureLost += (_, _) => _drag = false;
        _window.Closed += (_, _) => _completion.TrySetResult(null);
        root.Loaded += (_, _) => edit.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        root.KeyDown += (_, args) =>
        {
            if (args.Key == global::Windows.System.VirtualKey.Escape) { _window.Close(); args.Handled = true; }
            else if (args.Key == global::Windows.System.VirtualKey.Enter) { Finish(); args.Handled = true; }
            else if (_mode == ScreenshotMode.Region && args.Key is global::Windows.System.VirtualKey.Left or global::Windows.System.VirtualKey.Right or global::Windows.System.VirtualKey.Up or global::Windows.System.VirtualKey.Down)
            {
                int dx = args.Key == global::Windows.System.VirtualKey.Left ? -1 : args.Key == global::Windows.System.VirtualKey.Right ? 1 : 0;
                int dy = args.Key == global::Windows.System.VirtualKey.Up ? -1 : args.Key == global::Windows.System.VirtualKey.Down ? 1 : 0;
                _selection = (_selection with { X = _selection.X + dx, Y = _selection.Y + dy }).Intersect(_frame.Bounds); Render(); args.Handled = true;
            }
        };
        _selection = new(frame.Bounds.X, frame.Bounds.Y, 0, 0); Render();
    }
    internal static async Task<ScreenshotSelection?> ChooseAsync(ScreenshotFrame frame, ScreenshotMode mode, IUiLocalizer ui, CancellationToken token)
    {
        var selector = new ScreenshotSelectionWindow(frame, mode, ui); selector._window.Activate();
        using var cancelled = token.Register(() => MainThread.BeginInvokeOnMainThread(selector._window.Close));
        var result = await selector._completion.Task.ConfigureAwait(true); token.ThrowIfCancellationRequested(); return result;
    }
    private static ScreenshotPoint Cursor() { GetCursorPos(out NativePoint point); return new(point.X, point.Y); }
    private void Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) { return; }
        if (_mode == ScreenshotMode.Window) { Finish(); return; }
        _start = Cursor(); _before = _selection; _move = _selection.Width > 0 && _start.X > _selection.X + 10 && _start.X < _selection.X + _selection.Width - 10 && _start.Y > _selection.Y + 10 && _start.Y < _selection.Y + _selection.Height - 10;
        if (!_move && _selection.Width > 0 && Math.Abs(_start.X - _selection.X - _selection.Width) < 14 && Math.Abs(_start.Y - _selection.Y - _selection.Height) < 14)
        { _start = new(_selection.X, _selection.Y); }
        _drag = true; _canvas.CapturePointer(args.Pointer); args.Handled = true;
    }
    private void Moved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        ScreenshotPoint point = Cursor();
        if (_mode == ScreenshotMode.Window)
        {
            _target = WindowAt(point, _handle, out ScreenshotBounds bounds); _selection = bounds.Intersect(_frame.Bounds); Render(); return;
        }
        if (!_drag) { return; }
        _selection = (_move ? _before with { X = _before.X + (int)(point.X - _start.X), Y = _before.Y + (int)(point.Y - _start.Y) } : ScreenshotBounds.Between(_start, point)).Intersect(_frame.Bounds); Render();
    }
    private void Released(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) { _drag = false; _canvas.ReleasePointerCapture(args.Pointer); }
    private void Render()
    {
        double x = _selection.X - _frame.Bounds.X, y = _selection.Y - _frame.Bounds.Y, w = _selection.Width, h = _selection.Height;
        Place(_outline, x, y, w, h);
        Place(_shade[0], 0, 0, _frame.Bounds.Width, y); Place(_shade[1], 0, y + h, _frame.Bounds.Width, _frame.Bounds.Height - y - h);
        Place(_shade[2], 0, y, x, h); Place(_shade[3], x + w, y, _frame.Bounds.Width - x - w, h);
        _dimensions.Text = $"{w:F0} × {h:F0} px";
    }
    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    { XCanvas.SetLeft(element, x); XCanvas.SetTop(element, y); element.Width = Math.Max(0, width); element.Height = Math.Max(0, height); }
    private void Finish()
    {
        if (_selection.Width < 2 || _selection.Height < 2 || _mode == ScreenshotMode.Window && _target is null) { return; }
        if (WindowsScreenCapture.DesktopBounds() != _frame.Bounds) { _completion.TrySetException(new InvalidOperationException("Display geometry changed. Start capture again.")); _window.Close(); return; }
        _completion.TrySetResult(new(_selection, _target)); _window.Close();
    }
    internal static WindowsInputTarget? WindowAt(ScreenshotPoint point, nint excluded, out ScreenshotBounds bounds)
    {
        WindowsInputTarget? selected = null; ScreenshotBounds found = default;
        EnumWindows((window, data) =>
        {
            if (window == excluded || !IsWindowVisible(window) || IsIconic(window) || !GetWindowRect(window, out var rectangle)) { return true; }
            if (DwmGetWindowAttribute(window, 9, out var extended, Marshal.SizeOf<NativeRect>()) == 0) { rectangle = extended; }
            if (point.X < rectangle.Left || point.X >= rectangle.Right || point.Y < rectangle.Top || point.Y >= rectangle.Bottom) { return true; }
            char[] title = new char[512]; int length = GetWindowText(window, title, title.Length); if (length == 0) { return true; }
            _ = GetWindowThreadProcessId(window, out uint process);
            selected = new(window.ToInt64(), process, new string(title, 0, length)); found = new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top); return false;
        }, 0);
        bounds = found; return selected;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    private delegate bool EnumProcedure(nint window, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProcedure callback, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect rect, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, [Out] char[] text, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
