using System.Drawing;

namespace Laura.App.Theming;

/// <summary>
/// Paleta e métricas de um tema escuro moderno para a janela de configurações.
///
/// O Windows Forms não tem sistema de temas embutido; centralizar cores e espaçamentos
/// aqui é o que mantém a interface coesa e fácil de reajustar.
/// </summary>
internal static class Palette
{
    /// <summary>Fundo principal da janela.</summary>
    internal static Color Background { get; } = Color.FromArgb(0x1B, 0x1B, 0x1F);

    /// <summary>Fundo dos cartões e painéis elevados.</summary>
    internal static Color Surface { get; } = Color.FromArgb(0x25, 0x25, 0x2B);

    /// <summary>Fundo de campos de entrada.</summary>
    internal static Color Field { get; } = Color.FromArgb(0x2E, 0x2E, 0x35);

    /// <summary>Cor de destaque, usada em foco, seleção e ações primárias.</summary>
    internal static Color Accent { get; } = Color.FromArgb(0x7C, 0x5C, 0xFF);

    /// <summary>Variante mais clara do destaque, para estados de passagem do mouse.</summary>
    internal static Color AccentHover { get; } = Color.FromArgb(0x8E, 0x72, 0xFF);

    /// <summary>Texto principal.</summary>
    internal static Color TextPrimary { get; } = Color.FromArgb(0xF2, 0xF2, 0xF5);

    /// <summary>Texto secundário, para dicas e descrições.</summary>
    internal static Color TextSecondary { get; } = Color.FromArgb(0x9A, 0x9A, 0xA6);

    /// <summary>Linhas divisórias e contornos discretos.</summary>
    internal static Color Border { get; } = Color.FromArgb(0x3A, 0x3A, 0x42);

    /// <summary>Cor de indicação de escuta ativa.</summary>
    internal static Color Listening { get; } = Color.FromArgb(0x4C, 0xC2, 0x8C);

    /// <summary>Cor de indicação de fala.</summary>
    internal static Color Speaking { get; } = Color.FromArgb(0x5A, 0x9C, 0xFF);

    /// <summary>Cor de indicação de repouso.</summary>
    internal static Color Muted { get; } = Color.FromArgb(0x6C, 0x6C, 0x78);

    /// <summary>Cor de alerta, para condições que exigem atenção do usuário.</summary>
    internal static Color Warning { get; } = Color.FromArgb(0xE8, 0xA3, 0x3D);

    /// <summary>Família tipográfica preferida, com recuo para uma fonte sempre presente.</summary>
    internal const string FontFamily = "Segoe UI Variable Display";

    /// <summary>Família tipográfica de recuo, disponível em toda instalação do Windows.</summary>
    internal const string FallbackFontFamily = "Segoe UI";

    /// <summary>Espaçamento base, em pixels, do qual as demais medidas derivam.</summary>
    internal const int Unit = 8;
}
