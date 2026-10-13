using Laura.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using XWindow = Microsoft.UI.Xaml.Window;
using XButton = Microsoft.UI.Xaml.Controls.Button;
using XStack = Microsoft.UI.Xaml.Controls.StackPanel;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace Laura.App.Platforms.Windows;

/// <summary>Shared Laura styled native secondary windows; never depends on the primary window's XamlRoot.</summary>
public static class WindowsDialogs
{
    private static readonly HashSet<XWindow> OpenDialogs = [];
    internal static XWindow? DialogForSmoke
    {
        get
        {
            if (!Environment.GetCommandLineArgs().Contains("--screenshot-smoke", StringComparer.Ordinal)) { throw new InvalidOperationException("Isolated screenshot diagnostics only."); }
            return OpenDialogs.SingleOrDefault();
        }
    }
    /// <summary>Closes pending user prompts on application shutdown; completion defaults to denial.</summary>
    public static void CloseAll() { foreach (var dialog in OpenDialogs.ToArray()) { dialog.Close(); } }
    /// <summary>Shows an informational dialog without creating a security permission.</summary>
    public static async Task AlertAsync(string title, string message, string close, CancellationToken token = default) =>
        _ = await ConfirmAsync(title, message, close, null, token).ConfigureAwait(false);

    /// <summary>Asynchronous user confirmation with close/escape defaulting to denial.</summary>
    public static Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel, CancellationToken token = default) =>
        MainThread.InvokeOnMainThreadAsync(() => ShowAsync(title, message, accept, cancel, null, token));

    /// <summary>Confirmation overload used when replacing existing UI alerts.</summary>
    public static Task<bool> AlertAsync(string title, string message, string accept, string cancel) => ConfirmAsync(title, message, accept, cancel);

    /// <summary>Shows a bounded text prompt with the same dialog host.</summary>
    public static Task<string?> PromptAsync(string title, string message, string initialValue = "", Keyboard? keyboard = null, CancellationToken token = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var input = new Microsoft.UI.Xaml.Controls.TextBox { Text = initialValue, MaxLength = 4000, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(input, message);
            return await ShowAsync(title, message, "OK", "Cancel / Cancelar", input, token).ConfigureAwait(true) ? input.Text : null;
        });

    /// <summary>Shows finite choices without using model text as a policy.</summary>
    public static Task<string?> ChooseAsync(string title, string cancel, string? destructive, params string[] choices) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var input = new Microsoft.UI.Xaml.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch };
        AutomationProperties.SetName(input, title);
        return await ShowAsync(title, "", "OK", cancel, input, CancellationToken.None).ConfigureAwait(true) ? input.SelectedItem as string : null;
    });

    internal static async Task<bool> ShowAsync(string title, string message, string accept, string? cancel, FrameworkElement? input, CancellationToken token, Func<bool>? validate = null, int height = 480)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        XWindow window = Create(title);
        OpenDialogs.Add(window);
        var body = new XStack { Spacing = 16, Padding = new Microsoft.UI.Xaml.Thickness(24) };
        body.Children.Add(Text(title, 22)); body.Children.Add(Text(message));
        if (input is not null) { body.Children.Add(input); }
        var actions = new XStack { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
        XButton? deny = cancel is null ? null : Button(cancel, () => Finish(false));
        if (deny is not null) { actions.Children.Add(deny); }
        var approve = Button(accept, () => Finish(true), true); actions.Children.Add(approve); body.Children.Add(actions);
        window.Content = new ScrollViewer { Content = body, Background = Brush(Theme.Background), VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto };
        window.Closed += (_, _) => { OpenDialogs.Remove(window); completion.TrySetResult(false); };
        var escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) => { Finish(false); args.Handled = true; }; body.KeyboardAccelerators.Add(escape);
        body.Loaded += (_, _) => { if (input is Microsoft.UI.Xaml.Controls.TextBox box) { box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); } else { (deny ?? approve).Focus(Microsoft.UI.Xaml.FocusState.Programmatic); } };
        Position(window, 640, height); window.Activate();
        using var registration = token.Register(() => MainThread.BeginInvokeOnMainThread(() => Finish(false)));
        bool result = await completion.Task.ConfigureAwait(true);
        token.ThrowIfCancellationRequested(); return result;
        void Finish(bool result) { if (result && validate is not null && !validate()) { return; } if (completion.TrySetResult(result)) { window.Close(); } }
    }
    internal static XWindow Create(string title)
    {
        var window = new XWindow { Title = title, Content = new Grid { RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark, Background = Brush(Theme.Background) } };
        WindowsWindowTheme.Apply(window);
        return window;
    }
    internal static void Position(XWindow window, int width, int height)
    {
        // Foreground monitor coordinates may be negative. Recompute against the current display inventory on every reveal.
        nint foreground = GetForegroundWindow();
        var monitor = new MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, 2), ref monitor)) { throw new InvalidOperationException("Monitor work area unavailable."); }
        double scale = Math.Max(96, GetDpiForWindow(foreground)) / 96d;
        int w = Math.Min((int)(width * scale), monitor.Work.Right - monitor.Work.Left), h = Math.Min((int)(height * scale), monitor.Work.Bottom - monitor.Work.Top);
        window.AppWindow.MoveAndResize(new RectInt32(monitor.Work.Left + (monitor.Work.Right - monitor.Work.Left - w) / 2,
            monitor.Work.Top + (monitor.Work.Bottom - monitor.Work.Top - h) / 2, w, h));
    }
    internal static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(Color color) => new(global::Windows.UI.Color.FromArgb(255, (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255)));
    internal static TextBlock Text(string text, double size = 14, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = Brush(muted ? Theme.Muted : Theme.Text), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    internal static XButton Button(string text, Action action, bool primary = false)
    {
        var button = new XButton { Content = text, MinHeight = Theme.TargetSize, Padding = new Microsoft.UI.Xaml.Thickness(12, 8, 12, 8), CornerRadius = new Microsoft.UI.Xaml.CornerRadius(Theme.ControlRadius),
            Background = Brush(primary ? Theme.Primary : Theme.Field), Foreground = Brush(Theme.Text) };
        AutomationProperties.SetName(button, text); ToolTipService.SetToolTip(button, text); button.Click += (_, _) => action(); return button;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal NativeRect Monitor, Work; internal uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
