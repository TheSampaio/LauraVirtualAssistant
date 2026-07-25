using System.Runtime.InteropServices;

namespace Laura.App.Interop;

/// <summary>
/// Ajustes na moldura nativa das janelas que o Windows Forms não expõe.
/// </summary>
public static partial class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>
    /// Pinta a barra de título de uma janela no modo escuro.
    ///
    /// Sem isso, a barra de título permaneceria clara sobre uma interface escura,
    /// quebrando a impressão de um tema único.
    ///
    /// Args:
    ///     handle: Identificador nativo da janela.
    ///     enabled: <see langword="true"/> para a barra escura.
    /// </summary>
    public static void UseDarkTitleBar(nint handle, bool enabled = true)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        int value = enabled ? 1 : 0;

        // Silenciosamente ignorado em versões do Windows anteriores ao suporte ao modo escuro.
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
