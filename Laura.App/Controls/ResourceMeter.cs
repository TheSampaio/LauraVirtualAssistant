using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>Shows real utilization alongside its measured resource value.</summary>
public sealed class ResourceMeter : Grid
{
    private readonly Label _title = Theme.Caption("");
    private readonly Label _value = Theme.Label("—", 18);
    private readonly Label _detail = Theme.Label("", 11, Theme.Muted);
    private readonly ProgressBar _bar = new() { ProgressColor = Theme.Accent, BackgroundColor = Theme.Field, HeightRequest = 4 };
    /// <summary>Creates a resource row with a bundled SVG and measured progress.</summary>
    public ResourceMeter(string icon)
    {
        ColumnDefinitions = [new(new GridLength(28)), new(GridLength.Star)];
        ColumnSpacing = Theme.Space * 3; Padding = new Thickness(0, 12);
        _value.FontAttributes = FontAttributes.Bold;
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        heading.Add(_title); heading.Add(_value, 1);
        var content = Theme.Stack(heading, _bar, _detail); content.Spacing = 8;
        this.Add(Theme.Icon(icon, true, 22)); this.Add(content, 1);
    }
    /// <summary>Applies actual values and hides progress when measurement is unavailable.</summary>
    public void Update(string title, string value, double? fraction, string detail = "")
    {
        _title.Text = title.ToUpperInvariant(); _value.Text = value; _detail.Text = detail; _detail.IsVisible = detail.Length > 0;
        _bar.IsVisible = fraction.HasValue; _bar.Progress = Math.Clamp(fraction ?? 0, 0, 1);
        SemanticProperties.SetDescription(this, title + ": " + value + ". " + detail);
    }
}
