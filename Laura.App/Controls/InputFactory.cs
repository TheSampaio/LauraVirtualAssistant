using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Creates input fields already dressed with the dark theme.
///
/// Windows Forms text boxes and combo boxes draw their own border with
/// system colors, which appear bright over the dark interface. The way out is
/// removing the native border and placing the field over a themed panel,
/// letting contrast with the surface define the editable area.
/// </summary>
internal static class InputFactory
{
    private const int FieldPadding = 6;
    private const int SingleLineHeight = 30;

    /// <summary>
    /// Creates a single-line text box over a themed panel.
    ///
    /// Args:
    ///     width: Total field width.
    ///     textBox: Receives the created text box.
    ///
    /// Returns:
    ///     The panel wrapping the text box.
    /// </summary>
    internal static Control CreateTextBox(int width, out TextBox textBox)
    {
        textBox = NewTextBox(multiline: false);
        return WrapField(textBox, width, SingleLineHeight);
    }

    /// <summary>
    /// Creates a multiline text box over a themed panel.
    ///
    /// Args:
    ///     height: Total field height.
    ///     textBox: Receives the created text box.
    ///
    /// Returns:
    ///     The panel wrapping the text box, occupying the available width.
    /// </summary>
    internal static Control CreateMultilineTextBox(int height, out TextBox textBox)
    {
        textBox = NewTextBox(multiline: true);
        textBox.ScrollBars = ScrollBars.Vertical;

        RoundedPanel wrapper = WrapField(textBox, width: 0, height);
        wrapper.Dock = DockStyle.Fill;
        wrapper.Height = height;

        return wrapper;
    }

    /// <summary>
    /// Creates a themed combo box.
    ///
    /// Args:
    ///     width: Field width.
    ///
    /// Returns:
    ///     The combo box ready to receive items.
    /// </summary>
    internal static ComboBox CreateComboBox(int width) => new ThemedComboBox
    {
        Font = FontFactory.Create(9.5f),
        Width = width,
    };

    /// <summary>
    /// Creates a text box without its native border.
    ///
    /// Args:
    ///     multiline: <see langword="true"/> to accept multiple lines.
    ///
    /// Returns:
    ///     The configured text box.
    /// </summary>
    private static TextBox NewTextBox(bool multiline) => new()
    {
        Multiline = multiline,
        BorderStyle = BorderStyle.None,
        BackColor = Palette.Field,
        ForeColor = Palette.TextPrimary,
        Font = FontFactory.Create(9.5f),
        Dock = DockStyle.Fill,
    };

    /// <summary>
    /// Places an input control on a panel with the field color.
    ///
    /// Args:
    ///     input: Control to wrap.
    ///     width: Panel width; zero leaves the width to the layout.
    ///     height: Panel height.
    ///
    /// Returns:
    ///     The resulting panel.
    /// </summary>
    private static RoundedPanel WrapField(Control input, int width, int height)
    {
        var wrapper = new RoundedPanel
        {
            BackColor = Palette.Field,
            CornerRadius = 8,
            Padding = new Padding(FieldPadding, FieldPadding - 1, FieldPadding, FieldPadding - 1),
            Height = height,
        };

        if (width > 0)
        {
            wrapper.Width = width;
        }

        wrapper.Controls.Add(input);
        return wrapper;
    }
}
