using System.Drawing;
using System.Windows.Forms;
using Laura.App.Controls;
using Laura.App.Theming;

namespace Laura.App.Views;

/// <summary>
/// Builds settings rows with consistent appearance and spacing.
///
/// Each card is a <see cref="TableLayoutPanel"/> anchored with <see cref="DockStyle.Fill"/>:
/// attached to a 100% column in the section, they all get exactly the same width.
/// An earlier version used panels with automatic width sizing, which
/// shrunk each card to its own content and left the list misaligned.
/// </summary>
internal static class SettingRowFactory
{
    private const int HintMaximumWidth = 380;
    private const int InputWidth = 240;

    /// <summary>
    /// Creates a section header.
    ///
    /// Args:
    ///     text: Section title.
    ///
    /// Returns:
    ///     A label styled as a title.
    /// </summary>
    internal static Control Heading(string text) => new Label
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        UseMnemonic = false,
        ForeColor = Palette.TextSecondary,
        Font = FontFactory.Create(8.5f, FontStyle.Bold),
        Margin = new Padding(4, Palette.Unit, 0, Palette.Unit),
        Anchor = AnchorStyles.Left,
    };

    /// <summary>
    /// Cria uma linha com um interruptor liga/desliga.
    ///
    /// Args:
    ///     title: Option title.
    ///     hint: Helper description, or empty.
    ///     toggle: Recebe o interruptor criado, para leitura posterior.
    ///
    /// Returns:
    ///     The row card.
    /// </summary>
    internal static Control Toggle(string title, string hint, out ToggleSwitch toggle)
    {
        toggle = new ToggleSwitch { Anchor = AnchorStyles.Right, Margin = new Padding(Palette.Unit * 2, 0, 0, 0) };

        TableLayoutPanel card = CreateSplitCard();
        card.Controls.Add(CreateTextStack(title, hint), 0, 0);
        card.Controls.Add(toggle, 1, 0);

        return card;
    }

    /// <summary>
    /// Cria uma linha com um controle deslizante e leitura do valor.
    ///
    /// Args:
    ///     title: Option title.
    ///     minimum: Menor valor da faixa.
    ///     maximum: Maior valor da faixa.
    ///     format: Function that formats the value displayed beside the title.
    ///     slider: Recebe o controle deslizante criado.
    ///
    /// Returns:
    ///     The row card.
    /// </summary>
    internal static Control SliderRow(
        string title,
        int minimum,
        int maximum,
        Func<int, string> format,
        out Slider slider)
    {
        slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, Palette.Unit, 0, 0),
        };

        Slider capturedSlider = slider;

        var valueLabel = new Label
        {
            // Read from the control itself: using the range minimum left the label
            // showing "-10" with the thumb centered until the first drag.
            Text = format(capturedSlider.Value),
            AutoSize = true,
            UseMnemonic = false,
            ForeColor = Palette.Accent,
            Font = FontFactory.Create(9.5f, FontStyle.Bold),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(Palette.Unit * 2, 0, 0, 0),
        };

        slider.ValueChanged += (_, _) => valueLabel.Text = format(capturedSlider.Value);

        TableLayoutPanel card = CreateSplitCard();
        card.RowCount = 2;
        card.Controls.Add(CreateTitleLabel(title), 0, 0);
        card.Controls.Add(valueLabel, 1, 0);
        card.Controls.Add(slider, 0, 1);
        card.SetColumnSpan(slider, 2);

        return card;
    }

    /// <summary>
    /// Creates a row with a combo box.
    ///
    /// Args:
    ///     title: Option title.
    ///     hint: Helper description, or empty.
    ///     combo: Receives the created combo box.
    ///
    /// Returns:
    ///     The row card.
    /// </summary>
    internal static Control ComboRow(string title, string hint, out ComboBox combo)
    {
        combo = InputFactory.CreateComboBox(InputWidth);
        combo.Anchor = AnchorStyles.Right;
        combo.Margin = new Padding(Palette.Unit * 2, 0, 0, 0);

        TableLayoutPanel card = CreateSplitCard();
        card.Controls.Add(CreateTextStack(title, hint), 0, 0);
        card.Controls.Add(combo, 1, 0);

        return card;
    }

    /// <summary>
    /// Creates a row with a single-line text box.
    ///
    /// Args:
    ///     title: Option title.
    ///     hint: Helper description, or empty.
    ///     textBox: Receives the created text box.
    ///
    /// Returns:
    ///     The row card.
    /// </summary>
    internal static Control TextRow(string title, string hint, out TextBox textBox)
    {
        Control field = InputFactory.CreateTextBox(InputWidth, out textBox);
        field.Anchor = AnchorStyles.Right;
        field.Margin = new Padding(Palette.Unit * 2, 0, 0, 0);

        TableLayoutPanel card = CreateSplitCard();
        card.Controls.Add(CreateTextStack(title, hint), 0, 0);
        card.Controls.Add(field, 1, 0);

        return card;
    }

    /// <summary>
    /// Creates a row with a multiline text box occupying the full width.
    ///
    /// Args:
    ///     title: Option title.
    ///     hint: Helper description, or empty.
    ///     height: Text box height, in pixels.
    ///     textBox: Receives the created text box.
    ///
    /// Returns:
    ///     The row card.
    /// </summary>
    internal static Control MultilineRow(string title, string hint, int height, out TextBox textBox)
    {
        Control field = InputFactory.CreateMultilineTextBox(height, out textBox);
        field.Margin = new Padding(0, Palette.Unit, 0, 0);

        TableLayoutPanel card = CreateCard(columns: 1);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowCount = 2;
        card.Controls.Add(CreateTextStack(title, hint), 0, 0);
        card.Controls.Add(field, 0, 1);

        return card;
    }

    /// <summary>
    /// Creates a two-column card: text on the left, control on the right.
    ///
    /// Returns:
    ///     The card grid, ready to receive controls.
    /// </summary>
    private static TableLayoutPanel CreateSplitCard()
    {
        TableLayoutPanel card = CreateCard(columns: 2);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        return card;
    }

    /// <summary>
    /// Creates the base grid for a card.
    ///
    /// Args:
    ///     columns: Number of columns.
    ///
    /// Returns:
    ///     The grid, occupying all available width and growing in height.
    /// </summary>
    private static TableLayoutPanel CreateCard(int columns) => new()
    {
        ColumnCount = columns,
        RowCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
        BackColor = Palette.Surface,
        Padding = new Padding(Palette.Unit * 2),
        Margin = new Padding(0, 0, 0, Palette.Unit),
    };

    /// <summary>
    /// Stacks title and hint in a column, left-aligned in the cell.
    ///
    /// Args:
    ///     title: Option title.
    ///     hint: Helper description; when empty, no extra line is created.
    ///
    /// Returns:
    ///     A panel with stacked text.
    /// </summary>
    private static Control CreateTextStack(string title, string hint)
    {
        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Surface,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left,
        };

        stack.Controls.Add(CreateTitleLabel(title));

        if (!string.IsNullOrEmpty(hint))
        {
            stack.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                // Without this, "&" in text such as "Time & language" is interpreted
                // como marcador de tecla de acesso e desaparece da tela.
                UseMnemonic = false,
                MaximumSize = new Size(HintMaximumWidth, 0),
                ForeColor = Palette.TextSecondary,
                Font = FontFactory.Create(8.5f),
                Margin = new Padding(0, 3, 0, 0),
            });
        }

        return stack;
    }

    /// <summary>
    /// Creates an option title label.
    ///
    /// Args:
    ///     title: Title text.
    ///
    /// Returns:
    ///     A styled label, left-aligned and vertically centered.
    /// </summary>
    private static Label CreateTitleLabel(string title) => new()
    {
        Text = title,
        AutoSize = true,
        UseMnemonic = false,
        ForeColor = Palette.TextPrimary,
        Font = FontFactory.Create(10f),
        Margin = Padding.Empty,
        Anchor = AnchorStyles.Left,
    };
}
