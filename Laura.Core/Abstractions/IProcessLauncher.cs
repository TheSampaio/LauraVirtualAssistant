namespace Laura.Core.Abstractions;

/// <summary>
/// Abre programas e endereços externos em nome da assistente.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Abre um endereço no aplicativo padrão do sistema.
    ///
    /// Args:
    ///     uri: Endereço a abrir; aceita esquemas <c>http</c>, <c>https</c> e
    ///     protocolos registrados como <c>ms-settings:</c>.
    ///
    /// Returns:
    ///     <see langword="true"/> quando o sistema aceitou abrir o endereço.
    /// </summary>
    bool TryOpen(Uri uri);

    /// <summary>
    /// Inicia um executável.
    ///
    /// Args:
    ///     fileName: Nome ou caminho do executável.
    ///     arguments: Argumentos de linha de comando, ou <see langword="null"/>.
    ///
    /// Returns:
    ///     <see langword="true"/> quando o processo foi iniciado.
    /// </summary>
    bool TryStart(string fileName, string? arguments = null);
}
