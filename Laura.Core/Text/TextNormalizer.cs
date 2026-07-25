using System.Globalization;
using System.Text;

namespace Laura.Core.Text;

/// <summary>
/// Normaliza texto falado para comparação tolerante a acentuação, caixa e pontuação.
///
/// O reconhecedor de fala devolve transcrições com variações irrelevantes para o
/// casamento de comandos ("Ok, Laura!" e "ok laura" são a mesma intenção). Toda
/// comparação de frases no domínio passa por aqui para operar sobre uma forma única.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Converte o texto para a forma canônica usada em comparações de comando.
    ///
    /// Remove diacríticos, converte para minúsculas usando a cultura invariante,
    /// descarta pontuação e colapsa espaços em branco consecutivos.
    ///
    /// Args:
    ///     text: Texto bruto transcrito pelo reconhecedor. Pode ser nulo ou vazio.
    ///
    /// Returns:
    ///     O texto canônico, ou uma string vazia quando a entrada não contém
    ///     nenhum caractere significativo.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool previousWasSeparator = false;

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            // Pontuação e espaços viram um único separador, nunca no início.
            if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Indica se o texto contém a frase informada como sequência completa de palavras.
    ///
    /// Diferente de <see cref="string.Contains(string, StringComparison)"/>, evita
    /// falsos positivos em prefixos ("laura" não casa dentro de "lauraceas").
    ///
    /// Args:
    ///     text: Texto já normalizado onde a busca ocorre.
    ///     phrase: Frase já normalizada a procurar.
    ///
    /// Returns:
    ///     <see langword="true"/> quando a frase aparece delimitada por fronteiras
    ///     de palavra; caso contrário, <see langword="false"/>.
    /// </summary>
    public static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase))
        {
            return false;
        }

        int index = text.IndexOf(phrase, StringComparison.Ordinal);

        while (index >= 0)
        {
            bool startsAtBoundary = index == 0 || text[index - 1] == ' ';
            int endIndex = index + phrase.Length;
            bool endsAtBoundary = endIndex == text.Length || text[endIndex] == ' ';

            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            index = text.IndexOf(phrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Remove a primeira ocorrência da frase e devolve o restante do texto.
    ///
    /// Usado para separar o gatilho do conteúdo do comando, como em
    /// "ok laura que horas sao" onde o gatilho precisa ser descartado.
    ///
    /// Args:
    ///     text: Texto já normalizado.
    ///     phrase: Frase já normalizada a remover.
    ///
    /// Returns:
    ///     O texto sem a frase e sem espaços nas extremidades. Quando a frase não
    ///     é encontrada, devolve o texto original inalterado.
    /// </summary>
    public static string RemovePhrase(string text, string phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase))
        {
            return text;
        }

        int index = text.IndexOf(phrase, StringComparison.Ordinal);

        return index < 0
            ? text
            : string.Concat(text.AsSpan(0, index), " ", text.AsSpan(index + phrase.Length)).Trim();
    }
}
