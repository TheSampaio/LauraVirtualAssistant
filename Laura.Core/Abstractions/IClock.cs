namespace Laura.Core.Abstractions;

/// <summary>
/// Fonte de tempo da aplicação.
///
/// Existe para que regras dependentes de data e hora — saudações por período do
/// dia, anúncio de hora cheia — sejam verificáveis em teste sem depender do
/// relógio real da máquina.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Obtém o instante atual no fuso horário local.
    /// </summary>
    DateTimeOffset Now { get; }
}
