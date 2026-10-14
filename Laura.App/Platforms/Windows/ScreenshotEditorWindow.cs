using System.Collections.Immutable;
using Laura.Core.Screenshots;
using Laura.Core.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using XButton = Microsoft.UI.Xaml.Controls.Button;
using GridLength = Microsoft.UI.Xaml.GridLength;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;
using ScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode;

namespace Laura.App.Platforms.Windows;

/// <summary>Compact native image editor with image-coordinate annotations, bounded history and original-resolution export.</summary>
internal sealed class ScreenshotEditorWindow : IDisposable
{
    private readonly Microsoft.UI.Xaml.Window _window;
    private readonly XCanvas _image = new(), _annotations = new(), _selection = new();
    private readonly List<ScreenshotPoint> _points = [];
    private readonly Dictionary<ScreenshotTool, XButton> _tools = [];
    private readonly List<Microsoft.UI.Xaml.Controls.Control> _busyControls = [];
    private readonly TextBlock _status = WindowsDialogs.Text("", 12, true);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IUiLocalizer _ui;
    private ScreenshotTool _tool = ScreenshotTool.Arrow;
    private uint _color = 0xfff63350;
    private double _thickness = 3;
    private bool _drawing, _resize, _busy, _disposed;
    private Guid? _selected;
    private ScreenshotAnnotation? _moving, _preview;
    private ImmutableArray<ScreenshotAnnotation> _encodedState;
    private byte[]? _encodedPng, _encodedJpeg;
    private readonly Func<string, Task> _action;
    internal ScreenshotDocument Document { get; }
    internal CancellationToken Lifetime => _lifetime.Token;
    internal event EventHandler? Closed;
    internal ScreenshotEditorWindow(ScreenshotDocument document, IUiLocalizer ui, Func<string, Task> action)
    {
        Document = document; _ui = ui; _action = action; _window = WindowsDialogs.Create("LAURA Screenshot");
        var root = new Microsoft.UI.Xaml.Controls.Grid { RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } }, Background = WindowsDialogs.Brush(Theming.Theme.Background) };
        var tools = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 4, Margin = new Microsoft.UI.Xaml.Thickness(12) };
        string[] icons = ["focus", "square", "circle", "minus", "arrow_up_right", "pencil", "type", "highlighter", "trash"];
        foreach (ScreenshotTool tool in Enum.GetValues<ScreenshotTool>())
        {
            var button = Icon(T("tool." + tool), icons[(int)tool], () => { _tool = tool; UpdateTools(); });
            _tools.Add(tool, button); tools.Children.Add(button);
        }
        tools.Children.Add(Icon(T("undo"), "undo_2", () => { Document.Undo(); _selected = null; Render(); }));
        tools.Children.Add(Icon(T("redo"), "redo_2", () => { Document.Redo(); _selected = null; Render(); }));
        var colors = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = false, Color = global::Windows.UI.Color.FromArgb(255, 246, 51, 80) };
        colors.ColorChanged += (_, args) => { _color = ((uint)args.NewColor.A << 24) | ((uint)args.NewColor.R << 16) | ((uint)args.NewColor.G << 8) | args.NewColor.B; };
        var color = Icon(T("color"), "palette", () => { }); color.Flyout = new Flyout { Content = colors }; tools.Children.Add(color);
        var thickness = new Microsoft.UI.Xaml.Controls.ComboBox { ItemsSource = new[] { 1, 2, 3, 5, 8, 12 }, SelectedIndex = 2, MinWidth = 68, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thickness, T("thickness")); ToolTipService.SetToolTip(thickness, T("thickness"));
        thickness.SelectionChanged += (_, _) => _thickness = thickness.SelectedItem is int width ? width : 3; tools.Children.Add(thickness);
        _busyControls.Add(thickness);
        var toolbar = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled };
        root.Children.Add(toolbar);
        int width = document.Frame.Bounds.Width, height = document.Frame.Bounds.Height;
        _image.Width = _annotations.Width = _selection.Width = width; _image.Height = _annotations.Height = _selection.Height = height;
        _image.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new(0, 0, width, height) };
        _image.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
        _image.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = ScreenshotRendering.Bitmap(document.Frame), Width = width, Height = height, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill });
        _image.Children.Add(_annotations); _image.Children.Add(_selection); _annotations.IsHitTestVisible = _selection.IsHitTestVisible = false;
        var viewport = new Viewbox { Child = _image, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Margin = new Microsoft.UI.Xaml.Thickness(16) };
        Microsoft.UI.Xaml.Controls.Grid.SetRow(viewport, 1); root.Children.Add(viewport);
        var footer = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 8, Margin = new Microsoft.UI.Xaml.Thickness(12) };
        var actions = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        foreach (var entry in new[] { ("copy", "copy"), ("save", "save"), ("analyze", "scan"), ("brain", "brain"), ("cancel", "x") })
        { string command = entry.Item1; actions.Children.Add(Icon(T(command), entry.Item2, () => { if (command == "cancel") { _window.Close(); } else { _ = RunAsync(command); } }, command == "copy", command == "cancel")); }
        actions.Children.Add(WindowsDialogs.Text($"{width} × {height} px", 12, true)); footer.Children.Add(actions); footer.Children.Add(_status);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(footer, 2); root.Children.Add(footer); _window.Content = root;
        _image.PointerPressed += Pressed; _image.PointerMoved += Moved; _image.PointerReleased += Released;
        _image.PointerCanceled += (_, _) => CancelDrawing(); _image.PointerCaptureLost += (_, _) => { if (_drawing) { CancelDrawing(); } };
        AddKey(root, global::Windows.System.VirtualKey.Escape, 0, () => _window.Close());
        AddKey(root, global::Windows.System.VirtualKey.Delete, 0, () => { if (_selected is { } id) { Document.Remove(id); _selected = null; Render(); } });
        AddKey(root, global::Windows.System.VirtualKey.Z, global::Windows.System.VirtualKeyModifiers.Control, () => { Document.Undo(); Render(); });
        AddKey(root, global::Windows.System.VirtualKey.Y, global::Windows.System.VirtualKeyModifiers.Control, () => { Document.Redo(); Render(); });
        AddKey(root, global::Windows.System.VirtualKey.C, global::Windows.System.VirtualKeyModifiers.Control, () => _ = RunAsync("copy"));
        AddKey(root, global::Windows.System.VirtualKey.S, global::Windows.System.VirtualKeyModifiers.Control, () => _ = RunAsync("save"));
        _window.Closed += (_, _) => { Dispose(); Closed?.Invoke(this, EventArgs.Empty); };
        UpdateTools(); WindowsDialogs.Position(_window, 1000, 720); _window.Activate();
    }
    internal Microsoft.UI.Xaml.Window NativeWindow => _window;
    internal void Show() => _window.Activate();
    internal void SetStatus(string message) => _status.Text = message;
    internal void SetBusy(bool value) { _busy = value; if (!_disposed) { foreach (var control in _busyControls) { control.IsEnabled = !value; } } }
    internal void Close() => _window.Close();
    private string T(string key) => _ui.Get("ui.screenshot." + key);
    private XButton Icon(string tooltip, string icon, Action action, bool primary = false, bool allowWhileBusy = false)
    {
        var button = WindowsDialogs.Button(tooltip, () => { if (!_disposed && (!_busy || allowWhileBusy)) { action(); } }, primary); button.Width = button.Height = 44; button.Padding = new Microsoft.UI.Xaml.Thickness(10);
        if (!allowWhileBusy) { _busyControls.Add(button); }
        button.Content = new Microsoft.UI.Xaml.Controls.Image { Source = new BitmapImage(new Uri("ms-appx:///hud_" + icon + ".png")), Width = 22, Height = 22 }; return button;
    }
    private void AddKey(UIElement root, global::Windows.System.VirtualKey key, global::Windows.System.VirtualKeyModifiers modifiers, Action action)
    { var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers }; accelerator.Invoked += (_, args) => { if (key == global::Windows.System.VirtualKey.Escape || (!_busy && !_disposed)) { action(); } args.Handled = true; }; root.KeyboardAccelerators.Add(accelerator); }
    private async Task RunAsync(string action)
    {
        if (_busy || _disposed) { return; } SetBusy(true); _status.Text = T("working");
        try { await _action(action).ConfigureAwait(true); }
        catch (OperationCanceledException) { if (!_disposed) { _status.Text = T("cancelled"); } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        { if (!_disposed) { _status.Text = T("error") + " · " + error.GetType().Name; } }
        finally { SetBusy(false); }
    }
    private ScreenshotPoint Point(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    { var point = args.GetCurrentPoint(_image).Position; return new(Math.Clamp(point.X, 0, Document.Frame.Bounds.Width), Math.Clamp(point.Y, 0, Document.Frame.Bounds.Height)); }
    private void Pressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (_busy || !args.GetCurrentPoint(_image).Properties.IsLeftButtonPressed) { return; } ScreenshotPoint point = Point(args); _points.Clear(); _points.Add(point); _preview = null; _moving = null;
        if (_tool == ScreenshotTool.Eraser) { var hit = Document.Hit(point); if (hit is not null) { Document.Remove(hit.Id); Render(); } return; }
        if (_tool == ScreenshotTool.Select)
        {
            _moving = Document.Hit(point); _selected = _moving?.Id;
            if (_moving is null) { Render(); return; }
            var box = _moving.Box(); _resize = Math.Abs(point.X - box.X - box.Width) < 12 && Math.Abs(point.Y - box.Y - box.Height) < 12;
        }
        _drawing = true; _image.CapturePointer(args.Pointer); Render();
    }
    private void Moved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_drawing || _busy) { return; } ScreenshotPoint point = Point(args);
        if (_moving is not null)
        { var box = _moving.Box(); _preview = _resize ? _moving.Resize(point.X - box.X, point.Y - box.Y) : _moving.Move(point.X - _points[0].X, point.Y - _points[0].Y); }
        else if (_tool is ScreenshotTool.Pen or ScreenshotTool.Highlight)
        { if (_points.Count < 4096) { _points.Add(point); } _preview = NewAnnotation(_points.ToImmutableArray()); }
        else if (_tool != ScreenshotTool.Text) { _preview = NewAnnotation([_points[0], point]); }
        Render();
    }
    private async void Released(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_drawing) { return; } _drawing = false; _image.ReleasePointerCapture(args.Pointer);
        try
        {
            if (_tool == ScreenshotTool.Text)
            {
                SetBusy(true); var anchor = _points[0]; string? text = await WindowsDialogs.PromptAsync(T("tool.Text"), T("textHelp"), token: _lifetime.Token).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(text)) { Document.Add(NewAnnotation([anchor]) with { Text = text[..Math.Min(text.Length, 2000)] }); }
            }
            else if (_preview is not null) { if (_moving is not null) { Document.Update(_preview); } else { Document.Add(_preview); } }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { if (!_disposed) { _status.Text = T("error"); } }
        finally { SetBusy(false); _preview = null; _moving = null; if (!_disposed) { Render(); } }
    }
    private ScreenshotAnnotation NewAnnotation(ImmutableArray<ScreenshotPoint> points) => new(Guid.NewGuid(), _tool, points, _color, _thickness);
    private void CancelDrawing() { _drawing = false; _preview = null; _moving = null; _points.Clear(); if (!_disposed) { Render(); } }
    private void UpdateTools() { foreach (var (tool, button) in _tools) { button.Background = WindowsDialogs.Brush(tool == _tool ? Theming.Theme.Primary : Theming.Theme.Field); } }
    internal void Render()
    {
        _annotations.Children.Clear(); _selection.Children.Clear();
        foreach (var annotation in Document.Annotations) { ScreenshotRendering.Annotation(_annotations, _moving?.Id == annotation.Id && _preview is not null ? _preview : annotation); }
        if (_preview is not null && _moving is null) { ScreenshotRendering.Annotation(_annotations, _preview); }
        var selected = _preview ?? Document.Annotations.FirstOrDefault(item => item.Id == _selected);
        if (selected is not null && _tool == ScreenshotTool.Select)
        {
            var box = selected.Box(); var border = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = box.Width + 8, Height = box.Height + 8, StrokeThickness = 1, Stroke = WindowsDialogs.Brush(Theming.Theme.Accent), StrokeDashArray = [4, 4] };
            XCanvas.SetLeft(border, box.X - 4); XCanvas.SetTop(border, box.Y - 4); _selection.Children.Add(border);
            var handle = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 10, Height = 10, Fill = WindowsDialogs.Brush(Theming.Theme.Accent) };
            XCanvas.SetLeft(handle, box.X + box.Width - 5); XCanvas.SetTop(handle, box.Y + box.Height - 5); _selection.Children.Add(handle);
        }
    }
    internal async Task<byte[]> ExportAsync(bool jpeg, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
        if (_drawing) { throw new InvalidOperationException("Finish the annotation before exporting."); }
        if (!_encodedState.Equals(Document.Annotations)) { ClearEncoded(); _encodedState = Document.Annotations; }
        byte[]? cached = jpeg ? _encodedJpeg : _encodedPng;
        if (cached is not null) { return (byte[])cached.Clone(); }
        _selection.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        try
        {
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(_image, Document.Frame.Bounds.Width, Document.Frame.Bounds.Height).AsTask(token).ConfigureAwait(true);
            var buffer = await bitmap.GetPixelsAsync().AsTask(token).ConfigureAwait(true); byte[] pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer)) { reader.ReadBytes(pixels); }
            byte[] encoded = await WindowsScreenCapture.EncodeAsync(pixels, bitmap.PixelWidth, bitmap.PixelHeight, jpeg, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (!_disposed && encoded.Length <= 32 * 1024 * 1024)
            { if (jpeg) { _encodedJpeg = (byte[])encoded.Clone(); } else { _encodedPng = (byte[])encoded.Clone(); } }
            return encoded;
        }
        finally { if (!_disposed) { _selection.Visibility = Microsoft.UI.Xaml.Visibility.Visible; } }
    }
    private void ClearEncoded()
    {
        if (_encodedPng is not null) { Array.Clear(_encodedPng); _encodedPng = null; }
        if (_encodedJpeg is not null) { Array.Clear(_encodedJpeg); _encodedJpeg = null; }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) { return; } _disposed = true; _lifetime.Cancel(); ClearEncoded(); _encodedState = default; _points.Clear(); _image.Children.Clear(); _annotations.Children.Clear(); _selection.Children.Clear(); _busyControls.Clear(); _tools.Clear(); Document.Dispose(); _lifetime.Dispose();
    }
}
