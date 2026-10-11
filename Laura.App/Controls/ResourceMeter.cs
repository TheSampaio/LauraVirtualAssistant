using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>Shows real utilization alongside its measured resource value.</summary>
public sealed class ResourceMeter : VerticalStackLayout
{
    private readonly Label _title = Theme.Label("", 11, Theme.Muted);
    private readonly Label _value = Theme.Label("", 12);
    private readonly ProgressBar _bar;
    /// <summary>Creates a compact progress indicator.</summary>
    public ResourceMeter(Color color)
    { Spacing = 7; _bar = new ProgressBar { ProgressColor = color, BackgroundColor = Theme.Field, HeightRequest = 5 }; Add(_title); Add(_value); Add(_bar); }
    /// <summary>Applies a measured fraction without fake animation.</summary>
    public void Update(string title, string value, double fraction)
    { _title.Text = title; _value.Text = value; _bar.Progress = Math.Clamp(fraction, 0, 1); }
}
