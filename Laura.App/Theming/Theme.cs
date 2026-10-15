using Microsoft.Maui.Controls.Shapes;
using System.Collections.Concurrent;
using MauiButton = Microsoft.Maui.Controls.Button;

namespace Laura.App.Theming;

/// <summary>Shared native MAUI styling, spacing and typography for every screen.</summary>
public static class Theme
{
    private static readonly ConcurrentDictionary<(string Name, bool Accent), ImageSource> IconSources = new();
    /// <summary>Gets the application background.</summary>
    public static Color Background { get; } = Color.FromArgb("#100D12");
    /// <summary>Gets the panel surface.</summary>
    public static Color Surface { get; } = Color.FromArgb("#17141B");
    /// <summary>Gets the input surface.</summary>
    public static Color Field { get; } = Color.FromArgb("#221D27");
    /// <summary>Gets the Laura accent.</summary>
    public static Color Accent { get; } = Color.FromArgb("#F63350");
    /// <summary>Gets the red action fill with readable contrast against the shared light text.</summary>
    public static Color Primary { get; } = Color.FromArgb("#CC1939");
    /// <summary>Gets the shared idle/listening red; listening is distinguished through motion.</summary>
    public static Color Listening => Accent;
    /// <summary>Gets the bright red accent for native speech output.</summary>
    public static Color Speaking { get; } = Color.FromArgb("#FF254C");
    /// <summary>Gets the primary foreground.</summary>
    public static Color Text { get; } = Color.FromArgb("#F5EDF6");
    /// <summary>Gets the secondary foreground.</summary>
    public static Color Muted { get; } = Color.FromArgb("#B1A6B9");
    /// <summary>Gets the panel stroke.</summary>
    public static Color Line { get; } = Color.FromArgb("#332A38");
    /// <summary>Gets the restrained assistant message surface.</summary>
    public static Color AssistantSurface { get; } = Color.FromArgb("#2A1822");
    /// <summary>Gets the shared four-point spacing unit.</summary>
    public const double Space = 4;
    /// <summary>Gets the shared panel radius.</summary>
    public const double PanelRadius = 16;
    /// <summary>Gets the shared control radius.</summary>
    public const int ControlRadius = 12;
    /// <summary>Gets the minimum pointer and touch target.</summary>
    public const double TargetSize = 44;
    /// <summary>Gets the common page-edge inset.</summary>
    public const double PageInset = 16;
    /// <summary>Gets the shared title size for secondary workspaces.</summary>
    public const double PageTitleSize = 22;
    /// <summary>Gets the shared workspace header padding.</summary>
    public static Thickness HeaderPadding => new(PageInset, 12);
    /// <summary>Creates a standard body label.</summary>
    public static Label Label(string text, double size = 14, Color? color = null) => new()
    { Text = text, FontSize = size, TextColor = color ?? Text, LineBreakMode = LineBreakMode.WordWrap };
    /// <summary>Creates a tracked section heading.</summary>
    public static Label Caption(string text) => new()
    { Text = text.ToUpperInvariant(), FontSize = 11, CharacterSpacing = 1.2, FontAttributes = FontAttributes.Bold, TextColor = Muted, VerticalOptions = LayoutOptions.Center };
    /// <summary>Creates a consistent rounded panel.</summary>
    public static Border Card(View content, double padding = 18) => new()
    { Content = content, BackgroundColor = Surface, Stroke = Line, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = PanelRadius }, Padding = padding };
    /// <summary>Creates an asynchronous action button and prevents duplicate submissions.</summary>
    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = text, TextColor = Text, BackgroundColor = primary ? Primary : Field,
            CornerRadius = ControlRadius, Padding = new Thickness(12, 8), FontSize = 13, MinimumHeightRequest = TargetSize };
        ApplyActionStates(button, MauiButton.BorderWidthProperty, MauiButton.BorderColorProperty);
        button.Clicked += async (_, _) => await RunActionAsync(button, action).ConfigureAwait(true);
        return button;
    }
    /// <summary>Creates a consistent SVG action in labeled or compact toolbar form.</summary>
    public static Button ActionButton(string text, string icon, Func<Task> action, bool primary = false, bool iconOnly = false)
    {
        var button = Button(iconOnly ? "" : text, action, primary);
        button.ImageSource = IconSource(icon); button.ContentLayout = new MauiButton.ButtonContentLayout(MauiButton.ButtonContentLayout.ImagePosition.Left, 8);
        button.Padding = new Thickness(10, 8);
        if (iconOnly) { button.WidthRequest = TargetSize; button.Padding = 10; }
        DescribeAction(button, text); return button;
    }
    /// <summary>Applies one selected-tab treatment across all workspaces.</summary>
    public static void SetSelected(Button button, bool selected, string description)
    {
        button.BackgroundColor = selected ? Primary : Field; button.TextColor = Text;
        button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        SemanticProperties.SetDescription(button, description);
    }
    /// <summary>Creates a local Lucide SVG image, rasterized by MAUI for the native backend.</summary>
    public static Image Icon(string name, bool accent = false, double size = 20) => new()
    { Source = IconSource(name, accent), WidthRequest = size, HeightRequest = size, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center };
    /// <summary>Resolves a bundled icon without a runtime network request.</summary>
    public static ImageSource IconSource(string name, bool accent = false) => IconSources.GetOrAdd((name, accent), static icon => ImageSource.FromFile("hud_" + icon.Name + (icon.Accent ? "_accent" : "") + ".png"));
    /// <summary>Creates an accessible icon action with native hover, focus and pressed feedback.</summary>
    public static ImageButton IconButton(string icon, Func<Task> action, bool primary = false, Func<bool>? canExecute = null)
    {
        var button = new ImageButton { Source = IconSource(icon), BackgroundColor = primary ? Primary : Field,
            CornerRadius = ControlRadius, Padding = 12, WidthRequest = TargetSize, HeightRequest = TargetSize,
            MinimumWidthRequest = TargetSize, MinimumHeightRequest = TargetSize, Aspect = Aspect.AspectFit };
        ApplyActionStates(button, ImageButton.BorderWidthProperty, ImageButton.BorderColorProperty);
        button.Clicked += async (_, _) => await RunActionAsync(button, action, canExecute).ConfigureAwait(true);
        return button;
    }
    /// <summary>Shares pointer, keyboard, pressed and disabled feedback across native button types.</summary>
    private static void ApplyActionStates(VisualElement button, BindableProperty borderWidth, BindableProperty borderColor)
    {
        var states = new VisualStateGroup { Name = "CommonStates" };
        var normal = new VisualState { Name = "Normal" };
        normal.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = 1d });
        normal.Setters.Add(new Setter { Property = borderWidth, Value = 0d });
        var hover = new VisualState { Name = "PointerOver" };
        hover.Setters.Add(new Setter { Property = borderWidth, Value = 1d });
        hover.Setters.Add(new Setter { Property = borderColor, Value = Muted });
        var focused = new VisualState { Name = "Focused" };
        focused.Setters.Add(new Setter { Property = borderWidth, Value = 1d });
        focused.Setters.Add(new Setter { Property = borderColor, Value = Accent });
        var pressed = new VisualState { Name = "Pressed" };
        pressed.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = .8d });
        var disabled = new VisualState { Name = "Disabled" };
        disabled.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = .38d });
        states.States.Add(normal); states.States.Add(hover); states.States.Add(focused); states.States.Add(pressed); states.States.Add(disabled);
        VisualStateManager.SetVisualStateGroups(button, [states]);
    }
    /// <summary>Creates the single compact back action used by all secondary workspaces.</summary>
    public static ImageButton BackButton(string description, Func<Task> goBack)
    {
        var button = IconButton("arrow_left", goBack); button.AutomationId = "workspace-back"; button.VerticalOptions = LayoutOptions.Center;
        DescribeAction(button, description); return button;
    }
    /// <summary>Creates shared title, subtitle and navigation geometry without constraining workspace-specific content.</summary>
    public static Grid PageHeading(string title, string subtitle, string icon, string backDescription, Func<Task> goBack)
    {
        var heading = new Grid { ColumnDefinitions = [new(new GridLength(TargetSize)), new(new GridLength(28)), new(GridLength.Star)], ColumnSpacing = 12, MinimumHeightRequest = TargetSize };
        var name = Label(title, PageTitleSize); name.LineBreakMode = LineBreakMode.TailTruncation;
        var detail = Label(subtitle, 12, Muted); detail.LineBreakMode = LineBreakMode.TailTruncation; DescribeAction(detail, subtitle);
        var text = Stack(name, detail); text.Spacing = Space; text.VerticalOptions = LayoutOptions.Center;
        heading.Add(BackButton(backDescription, goBack)); heading.Add(Icon(icon, true, 28), 1); heading.Add(text, 2); return heading;
    }
    /// <summary>Creates the same compact brand lockup in secondary workspace headers.</summary>
    public static VerticalStackLayout WorkspaceBrand(string subtitle)
    {
        var name = Label("L A U R A", 23, Accent); name.FontAttributes = FontAttributes.Bold;
        var brand = Stack(name, Label(subtitle.ToUpperInvariant(), 9, Muted)); brand.Spacing = Space; brand.VerticalOptions = LayoutOptions.Center;
        return brand;
    }
    /// <summary>Provides the same translated action name to tooltips and assistive technology.</summary>
    public static void DescribeAction(View view, string description)
    { ToolTipProperties.SetText(view, description); SemanticProperties.SetDescription(view, description); }
    /// <summary>Runs either native button type with duplicate submission protection.</summary>
    private static async Task RunActionAsync(VisualElement button, Func<Task> action, Func<bool>? canExecute = null)
    {
        button.IsEnabled = false;
        try { await action().ConfigureAwait(true); }
        catch (Exception error) { Laura.Core.Diagnostics.LauraDiagnostics.Record("ui.action_failed", outcome: error.GetType().Name); if (Application.Current is { Windows.Count: > 0 } app && app.Windows[0].Page is { } page) { await Laura.App.Platforms.Windows.WindowsDialogs.AlertAsync("Laura", "The operation failed. Check the configuration and try again.", "OK").ConfigureAwait(true); } }
        finally { button.IsEnabled = canExecute?.Invoke() ?? true; }
    }
    /// <summary>Creates a native input with the shared dark palette.</summary>
    public static Entry Entry(string placeholder = "") => new()
    { Placeholder = placeholder, PlaceholderColor = Muted, TextColor = Text, BackgroundColor = Field, FontSize = 14, MinimumHeightRequest = 44 };
    /// <summary>Creates a consistent vertical stack.</summary>
    public static VerticalStackLayout Stack(params View[] views)
    { var stack = new VerticalStackLayout { Spacing = 12 }; foreach (View view in views) { stack.Add(view); } return stack; }
    /// <summary>Creates a picker with a finite list of known values.</summary>
    public static Picker Picker(IEnumerable<string> items, string selected)
    {
        var picker = new Picker { TextColor = Text, BackgroundColor = Field, FontSize = 14, MinimumHeightRequest = 44 };
        foreach (string item in items) { picker.Items.Add(item); }
        picker.SelectedIndex = picker.Items.IndexOf(selected);
        return picker;
    }
    /// <summary>Creates a consistent page header and native back button without platform navigation styling.</summary>
    public static View Workspace(string title, string back, View body, Func<Task> goBack)
    {
        var header = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12, Padding = HeaderPadding };
        header.Add(BackButton(back, goBack)); header.Add(Label(title, PageTitleSize), 1);
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], BackgroundColor = Background };
        root.Add(header); root.Add(body, 0, 1); return root;
    }
}
