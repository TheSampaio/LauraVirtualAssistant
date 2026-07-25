using System.Globalization;

namespace Laura.Core.Abstractions;

/// <summary>
/// Fornece os textos que Laura fala e exibe, no idioma ativo.
///
/// Além das mensagens, guarda as listas de frases que cada habilidade reconhece —
/// traduzir Laura é editar um arquivo de idioma, nunca recompilar as habilidades.
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// Ocorre depois que o idioma ativo muda.
    /// </summary>
    event EventHandler<CultureInfo>? CultureChanged;

    /// <summary>
    /// Obtém a cultura ativa.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// Obtém as culturas para as quais existe um arquivo de idioma.
    /// </summary>
    IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <summary>
    /// Troca o idioma ativo.
    ///
    /// Args:
    ///     culture: Cultura desejada. Quando não há arquivo correspondente, a
    ///     cultura mais próxima disponível é usada.
    /// </summary>
    void SetCulture(CultureInfo culture);

    /// <summary>
    /// Obtém uma mensagem formatada.
    ///
    /// Quando a chave define várias redações, uma delas é sorteada — a variação
    /// é o que impede Laura de soar como uma gravação.
    ///
    /// Args:
    ///     key: Chave da mensagem, como <c>skill.time.response</c>.
    ///     arguments: Valores aplicados ao formato da mensagem.
    ///
    /// Returns:
    ///     A mensagem formatada na cultura ativa, ou a própria chave entre
    ///     colchetes quando ela não existe no arquivo de idioma.
    /// </summary>
    string Get(string key, params object?[] arguments);

    /// <summary>
    /// Obtém a lista de frases associada a uma chave.
    ///
    /// Args:
    ///     key: Chave da lista, como <c>phrases.time</c>.
    ///
    /// Returns:
    ///     As frases cadastradas, ou uma lista vazia quando a chave não existe.
    /// </summary>
    IReadOnlyList<string> GetPhrases(string key);
}
