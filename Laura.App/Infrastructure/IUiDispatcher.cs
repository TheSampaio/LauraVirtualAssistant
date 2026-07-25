namespace Laura.App.Infrastructure;

/// <summary>
/// Executa trabalho na thread da interface.
///
/// Os eventos do motor da assistente chegam em threads de segundo plano; tocar em
/// controles Windows Forms a partir delas lançaria exceção, então tudo que mexe na
/// interface passa por aqui.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Agenda uma ação para rodar na thread da interface, sem esperar por ela.
    ///
    /// Args:
    ///     action: Trabalho a executar na thread da interface.
    /// </summary>
    void Post(Action action);
}
