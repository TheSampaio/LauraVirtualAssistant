namespace Laura.App.Views;

/// <summary>
/// Display label and value pair shown in a combo box.
/// </summary>
/// <param name="Display">Text shown to the user.</param>
/// <param name="Value">Associated value, returned when the item is selected.</param>
public sealed record ComboItem(string Display, string? Value)
{
    /// <summary>
    /// Returns the text displayed by the combo box.
    ///
    /// Returns:
    ///     The item label.
    /// </summary>
    public override string ToString() => Display;
}
