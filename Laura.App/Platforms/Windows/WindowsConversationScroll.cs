using Laura.Core.Conversation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Laura.App.Platforms.Windows;

/// <summary>Follows native layout/extent changes, preserving explicit user navigation with no scroll timers or animations.</summary>
public sealed class WindowsConversationScroll : IDisposable
{
    private readonly ConversationScrollState _state = new();
    private readonly Action<bool, int> _changed;
    private ScrollViewer? _viewer;
    private FrameworkElement? _host;
    private bool _manual;
    private bool _pending;
    private double _extent = -1, _viewport = -1;
    private readonly PointerEventHandler _pointer;
    private readonly KeyEventHandler _key;
    /// <summary>Provides discrete return-to-latest visibility and unread count to the existing chat header.</summary>
    public WindowsConversationScroll(Action<bool, int> changed) { _changed = changed; _pointer = ManualPointer; _key = ManualKey; }
    /// <summary>Gets whether native layout changes currently follow new content.</summary>
    public bool Following => _state.Following;
    /// <summary>Attaches after the CollectionView's native template is loaded.</summary>
    public void Attach(FrameworkElement host)
    {
        if (ReferenceEquals(_host, host)) { return; } Detach(); _host = host; host.LayoutUpdated += Layout;
        FindViewer(); Request();
    }
    private void FindViewer()
    {
        if (_viewer is not null || _host is null) { return; }
        _viewer = Descendant<ScrollViewer>(_host);
        if (_viewer is null) { return; }
        _viewer.ViewChanged += Position;
        _viewer.AddHandler(UIElement.PointerWheelChangedEvent, _pointer, true);
        _viewer.AddHandler(UIElement.PointerPressedEvent, _pointer, true);
        _viewer.AddHandler(UIElement.KeyDownEvent, _key, true);
    }
    private void ManualPointer(object sender, PointerRoutedEventArgs args)
    {
        int wheel = args.GetCurrentPoint(_viewer).Properties.MouseWheelDelta;
        if (wheel > 0) { _state.ManualPosition(5); _changed(true, _state.Unread); }
        if (wheel < 0 && _viewer is { } viewer && viewer.ScrollableHeight - viewer.VerticalOffset <= 4) { _state.Follow(); _changed(false, 0); }
        if (wheel != 0 || args.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse
            || HasScrollBarAncestor(args.OriginalSource as DependencyObject)) { _manual = true; }
    }
    private static bool HasScrollBarAncestor(DependencyObject? source)
    {
        while (source is not null) { if (source is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar) { return true; } source = VisualTreeHelper.GetParent(source); }
        return false;
    }
    private void ManualKey(object sender, KeyRoutedEventArgs args)
    { if (args.Key is global::Windows.System.VirtualKey.Up or global::Windows.System.VirtualKey.Down or global::Windows.System.VirtualKey.PageUp or global::Windows.System.VirtualKey.PageDown or global::Windows.System.VirtualKey.Home or global::Windows.System.VirtualKey.End) { _manual = true; } }
    private void Position(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_viewer is null || !_manual) { return; }
        _state.ManualPosition(_viewer.ScrollableHeight - _viewer.VerticalOffset);
        if (!args.IsIntermediate) { _manual = false; }
        _changed(!_state.Following, _state.Unread);
    }
    private void Layout(object? sender, object args)
    {
        FindViewer();
        if (_viewer is null || !_state.Following) { return; }
        if (_extent != _viewer.ExtentHeight || _viewport != _viewer.ViewportHeight) { _pending = true; }
        _extent = _viewer.ExtentHeight; _viewport = _viewer.ViewportHeight;
        if (!_pending || _viewer.ViewportHeight <= 0) { return; }
        _pending = false;
        _viewer.ChangeView(null, _viewer.ScrollableHeight, null, disableAnimation: true);
    }
    /// <summary>Records new content and follows only while the reader has not navigated upward.</summary>
    public void MessageArrived() { _state.MessageArrived(); _changed(!_state.Following, _state.Unread); Request(); }
    /// <summary>Coalesces until native layout is valid; content/viewport growth also triggers a follow.</summary>
    public void Request() { if (!_state.Following) { return; } _pending = true; _host?.InvalidateArrange(); }
    /// <summary>Explicitly returns to the latest message and resumes following.</summary>
    public void Follow() { _manual = false; _state.Follow(); _changed(false, 0); Request(); }
    internal void ObserveManualForSmoke(double distance)
    {
        if (!Environment.GetCommandLineArgs().Contains("--ui-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated UI diagnostics only."); }
        _state.ManualPosition(distance); _changed(!_state.Following, _state.Unread);
    }
    private void Detach()
    {
        if (_host is not null) { _host.LayoutUpdated -= Layout; }
        if (_viewer is not null)
        { _viewer.ViewChanged -= Position; _viewer.RemoveHandler(UIElement.PointerWheelChangedEvent, _pointer); _viewer.RemoveHandler(UIElement.PointerPressedEvent, _pointer); _viewer.RemoveHandler(UIElement.KeyDownEvent, _key); }
        _viewer = null; _host = null; _extent = -1; _viewport = -1;
    }
    internal static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) { return match; }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { if (Descendant<T>(VisualTreeHelper.GetChild(root, index)) is { } found) { return found; } }
        return null;
    }
    /// <inheritdoc />
    public void Dispose() => Detach();
}
