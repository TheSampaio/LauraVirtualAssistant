using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Creates input fields already dressed with the dark theme.
///
/// Windows Forms text boxes and combo boxes draw their own border with
/// system colors, which appear bright over the dark interface. The way out is
/// remover a borda nativa e apoiar o campo sobre um painel com a cor de campo,
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
    ///     width: Largura total do campo.
    ///     textBox: Recebe a caixa de texto criada.
    ///
    /// Returns:
    ///     O painel que embala a caixa de texto.
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
    ///     height: Altura total do campo.
    ///     textBox: Recebe a caixa de texto criada.
    ///
    /// Returns:
    ///     The panel wrapping the text box, occupying the available width.
    /// </summary>
    internal static Control CreateMultilineTextBox(int height, out TextBox textBox)
    {
        textBox = NewTextBox(multiline: true);
        textBox.ScrollBars = ScrollBars.Vertical;

        Panel wrapper = WrapField(textBox, width: 0, height);
        wrapper.Dock = DockStyle.Fill;
        wrapper.Height = height;

        return wrapper;
    }

    /// <summary>
    /// Creates a themed combo box.
    ///
    /// Args:
    ///     width: Largura do campo.
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
    /// Cria uma caixa de texto sem borda nativa.
    ///
    /// Args:
    ///     multiline: <see langword="true"/> to accept multiple lines.
    ///
    /// Returns:
    ///     A caixa de texto configurada.
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
    ///     input: Controle a embalar.
    ///     width: Largura do painel; zero deixa a largura a cargo do layout.
    ///     height: Altura do painel.
    ///
    /// Returns:
    ///     O painel resultante.
    /// </summary>
    private static Panel WrapField(Control input, int width, int height)
    {
        var wrapper = new Panel
        {
            BackColor = Palette.Field,
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
