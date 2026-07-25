namespace Laura.App.Views;

/// <summary>
/// Par de rótulo e valor exibido em uma caixa de seleção.
/// </summary>
/// <param name="Display">Texto mostrado ao usuário.</param>
/// <param name="Value">Valor associado, devolvido quando o item é escolhido.</param>
public sealed record ComboItem(string Display, string? Value)
{
    /// <summary>
    /// Devolve o texto exibido pela caixa de seleção.
    ///
    /// Returns:
    ///     O rótulo do item.
    /// </summary>
    public override string ToString() => Display;
}
