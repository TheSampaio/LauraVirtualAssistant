namespace Laura.Core.Abstractions;

/// <summary>
/// Controla a camada visual da aplicação a partir do domínio.
///
/// Permite que uma habilidade abra o painel de configurações ou que o motor peça
/// o encerramento sem que <c>Laura.Core</c> conheça WPF.
/// </summary>
public interface IShellController
{
    /// <summary>
    /// Exibe o painel de configurações e traz a janela para frente.
    /// </summary>
    void ShowSettings();

    /// <summary>
    /// Alterna a visibilidade do painel de configurações.
    ///
    /// É o que a tecla de atalho global aciona.
    /// </summary>
    void ToggleSettings();

    /// <summary>
    /// Encerra a aplicação de forma ordenada.
    /// </summary>
    void Shutdown();
}
