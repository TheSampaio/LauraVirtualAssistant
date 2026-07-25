using System.Drawing;
using System.Windows.Forms;
using Laura.App.Theming;

namespace Laura.App.Controls;

/// <summary>
/// Cria campos de entrada já vestidos com o tema escuro.
///
/// Caixas de texto e de seleção do Windows Forms desenham a própria borda com as
/// cores do sistema, que aparecem claras sobre a interface escura. A saída é
/// remover a borda nativa e apoiar o campo sobre um painel com a cor de campo,
/// deixando o contraste com a superfície delimitar a área editável.
/// </summary>
internal static class InputFactory
{
    private const int FieldPadding = 6;
    private const int SingleLineHeight = 30;

    /// <summary>
    /// Cria uma caixa de texto de linha única sobre um painel temático.
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
    /// Cria uma caixa de texto de várias linhas sobre um painel temático.
    ///
    /// Args:
    ///     height: Altura total do campo.
    ///     textBox: Recebe a caixa de texto criada.
    ///
    /// Returns:
    ///     O painel que embala a caixa de texto, ocupando a largura disponível.
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
    /// Cria uma caixa de seleção suspensa vestida com o tema.
    ///
    /// Args:
    ///     width: Largura do campo.
    ///
    /// Returns:
    ///     A caixa de seleção pronta para receber itens.
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
    ///     multiline: <see langword="true"/> para aceitar várias linhas.
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
    /// Apoia um controle de entrada sobre um painel com a cor de campo.
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
