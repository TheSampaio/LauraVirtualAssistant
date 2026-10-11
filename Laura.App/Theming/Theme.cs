using Microsoft.Maui.Controls.Shapes;

namespace Laura.App.Theming;

/// <summary>Shared native MAUI styling, spacing and typography for every screen.</summary>
public static class Theme
{
    /// <summary>Gets the application background.</summary>
    public static Color Background { get; } = Color.FromArgb("#100D12");
    /// <summary>Gets the panel surface.</summary>
    public static Color Surface { get; } = Color.FromArgb("#19141C");
    /// <summary>Gets the input surface.</summary>
    public static Color Field { get; } = Color.FromArgb("#241C28");
    /// <summary>Gets the Laura accent.</summary>
    public static Color Accent { get; } = Color.FromArgb("#F63350");
    /// <summary>Gets the shared idle/listening red; listening is distinguished through motion.</summary>
    public static Color Listening => Accent;
    /// <summary>Gets the bright red accent for native speech output.</summary>
    public static Color Speaking { get; } = Color.FromArgb("#FF254C");
    /// <summary>Gets the primary foreground.</summary>
    public static Color Text { get; } = Color.FromArgb("#F5EDF6");
    /// <summary>Gets the secondary foreground.</summary>
    public static Color Muted { get; } = Color.FromArgb("#A598AF");
    /// <summary>Gets the panel stroke.</summary>
    public static Color Line { get; } = Color.FromArgb("#392331");
    /// <summary>Creates a standard body label.</summary>
    public static Label Label(string text, double size = 14, Color? color = null) => new()
    { Text = text, FontSize = size, TextColor = color ?? Text, LineBreakMode = LineBreakMode.WordWrap };
    /// <summary>Creates a tracked section heading.</summary>
    public static Label Caption(string text) => new()
    { Text = text.ToUpperInvariant(), FontSize = 11, CharacterSpacing = 2, TextColor = Accent, Margin = new Thickness(0, 0, 0, 10) };
    /// <summary>Creates a consistent rounded panel.</summary>
    public static Border Card(View content, double padding = 18) => new()
    { Content = content, BackgroundColor = Surface, Stroke = Line, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = padding };
    /// <summary>Creates an asynchronous action button and prevents duplicate submissions.</summary>
    public static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Text = text, TextColor = Text, BackgroundColor = primary ? Accent : Field,
            CornerRadius = 10, Padding = new Thickness(16, 10), FontSize = 13, MinimumHeightRequest = 42 };
        button.Clicked += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action().ConfigureAwait(true); }
            catch (Exception) { if (Application.Current is { Windows.Count: > 0 } app && app.Windows[0].Page is { } page) { await page.DisplayAlertAsync("Laura", "The operation failed. Check the configuration and try again.", "OK").ConfigureAwait(true); } }
            finally { button.IsEnabled = true; }
        };
        return button;
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
        var header = new HorizontalStackLayout { Spacing = 18, Padding = new Thickness(26, 18) };
        header.Add(Button("← " + back, goBack)); header.Add(new Label { Text = title, FontSize = 24, TextColor = Text, VerticalOptions = LayoutOptions.Center });
        var root = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], BackgroundColor = Background };
        root.Add(header); root.Add(body, 0, 1); return root;
    }
}
