using System.Globalization;

namespace Laura.Core.Abstractions;

/// <summary>
/// Provides the text Laura speaks and displays, in the active language.
///
/// Besides messages, stores the phrase lists each skill recognizes -
/// translating Laura means editing a language file, never recompiling skills.
/// </summary>
public interface ILocalizer
{
    /// <summary>
    /// Occurs after the active language changes.
    /// </summary>
    event EventHandler<CultureInfo>? CultureChanged;

    /// <summary>
    /// Gets the active culture.
    /// </summary>
    CultureInfo Culture { get; }

    /// <summary>
    /// Gets the cultures for which a language file exists.
    /// </summary>
    IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <summary>
    /// Changes the active language.
    ///
    /// Args:
    ///     culture: Desired culture. When there is no matching file, the
    ///     closest available culture is used.
    /// </summary>
    void SetCulture(CultureInfo culture);

    /// <summary>
    /// Gets a formatted message.
    ///
    /// When the key defines several phrasings, one is selected at random - variation
    /// is what keeps Laura from sounding like a recording.
    ///
    /// Args:
    ///     key: Message key, such as <c>skill.time.response</c>.
    ///     arguments: Values applied to the message format.
    ///
    /// Returns:
    ///     The formatted message in the active culture, or the key itself in
    ///     brackets when it does not exist in the language file.
    /// </summary>
    string Get(string key, params object?[] arguments);

    /// <summary>
    /// Gets the phrase list associated with a key.
    ///
    /// Args:
    ///     key: List key, such as <c>phrases.time</c>.
    ///
    /// Returns:
    ///     The registered phrases, or an empty list when the key does not exist.
    /// </summary>
    IReadOnlyList<string> GetPhrases(string key);
}
