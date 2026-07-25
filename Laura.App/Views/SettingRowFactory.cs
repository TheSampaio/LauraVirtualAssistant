using System.Drawing;
using System.Windows.Forms;
using Laura.App.Controls;
using Laura.App.Theming;

namespace Laura.App.Views;

/// <summary>
/// Monta as linhas de configuração com aparência e espaçamento uniformes.
///
/// Cada cartão é um <see cref="TableLayoutPanel"/> ancorado por <see cref="DockStyle.Fill"/>:
/// acoplado a uma coluna de 100% na seção, todos ficam exatamente com a mesma largura.
/// Uma versão anterior usava painéis com dimensionamento automático em largura, o que
/// encolhia cada cartão até o próprio conteúdo e deixava a lista desalinhada.
/// </summary>
internal static class SettingRowFactory
{
    private const int HintMaximumWidth = 380;
    private const int InputWidth = 240;

    /// <summary>
    /// Cria um cabeçalho de seção.
    ///
    /// Args:
    ///     text: Título da seção.
    ///
    /// Returns:
    ///     Um rótulo estilizado como título.
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
    ///     title: Título da opção.
    ///     hint: Descrição auxiliar, ou vazio.
    ///     toggle: Recebe o interruptor criado, para leitura posterior.
    ///
    /// Returns:
    ///     O cartão da linha.
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
    ///     title: Título da opção.
    ///     minimum: Menor valor da faixa.
    ///     maximum: Maior valor da faixa.
    ///     format: Função que formata o valor exibido ao lado do título.
    ///     slider: Recebe o controle deslizante criado.
    ///
    /// Returns:
    ///     O cartão da linha.
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
            // Lido do próprio controle: usar o mínimo da faixa deixava o rótulo
            // mostrando "-10" com o cursor no centro até o primeiro arrasto.
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
    /// Cria uma linha com uma caixa de seleção suspensa.
    ///
    /// Args:
    ///     title: Título da opção.
    ///     hint: Descrição auxiliar, ou vazio.
    ///     combo: Recebe a caixa de seleção criada.
    ///
    /// Returns:
    ///     O cartão da linha.
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
    /// Cria uma linha com uma caixa de texto de linha única.
    ///
    /// Args:
    ///     title: Título da opção.
    ///     hint: Descrição auxiliar, ou vazio.
    ///     textBox: Recebe a caixa de texto criada.
    ///
    /// Returns:
    ///     O cartão da linha.
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
    /// Cria uma linha com uma caixa de texto de várias linhas ocupando a largura toda.
    ///
    /// Args:
    ///     title: Título da opção.
    ///     hint: Descrição auxiliar, ou vazio.
    ///     height: Altura da caixa de texto, em pixels.
    ///     textBox: Recebe a caixa de texto criada.
    ///
    /// Returns:
    ///     O cartão da linha.
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
    /// Cria um cartão de duas colunas: texto à esquerda, controle à direita.
    ///
    /// Returns:
    ///     A grade do cartão, pronta para receber os controles.
    /// </summary>
    private static TableLayoutPanel CreateSplitCard()
    {
        TableLayoutPanel card = CreateCard(columns: 2);
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        return card;
    }

    /// <summary>
    /// Cria a grade base de um cartão.
    ///
    /// Args:
    ///     columns: Número de colunas.
    ///
    /// Returns:
    ///     A grade, ocupando toda a largura disponível e crescendo em altura.
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
    /// Empilha título e dica em uma coluna, alinhada à esquerda da célula.
    ///
    /// Args:
    ///     title: Título da opção.
    ///     hint: Descrição auxiliar; quando vazia, nenhuma linha extra é criada.
    ///
    /// Returns:
    ///     Um painel com o texto empilhado.
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
                // Sem isto, o "&" de textos como "Time & language" é interpretado
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
    /// Cria o rótulo de título de uma opção.
    ///
    /// Args:
    ///     title: Texto do título.
    ///
    /// Returns:
    ///     Um rótulo estilizado, alinhado à esquerda e centrado verticalmente.
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
