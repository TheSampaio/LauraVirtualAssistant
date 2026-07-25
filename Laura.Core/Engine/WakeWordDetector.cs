using Laura.Core.Configuration;
using Laura.Core.Text;

namespace Laura.Core.Engine;

/// <summary>
/// Recognizes the wake word and separates the rest of the command from it.
/// </summary>
public static class WakeWordDetector
{
    /// <summary>
    /// Looks for a wake phrase in the recognized text.
    ///
    /// Phrases are tested from longest to shortest so the extracted command does not
    /// carry leftovers of the wake word.
    ///
    /// Args:
    ///     normalizedText: The already-normalized transcript.
    ///     options: Listening options with the current wake phrases.
    ///     command: Receives the portion after the wake word, empty when the user
    ///     said only "Ok, Laura".
    ///
    /// Returns:
    ///     <see langword="true"/> when a wake phrase was found.
    /// </summary>
    public static bool TryDetect(string normalizedText, RecognitionOptions options, out string command)
    {
        ArgumentNullException.ThrowIfNull(options);

        command = string.Empty;

        if (string.IsNullOrEmpty(normalizedText))
        {
            return false;
        }

        IEnumerable<string> ordered = options.WakePhrases
            .Select(TextNormalizer.Normalize)
            .Where(static phrase => phrase.Length > 0)
            .OrderByDescending(static phrase => phrase.Length);

        foreach (string phrase in ordered)
        {
            if (!TextNormalizer.ContainsPhrase(normalizedText, phrase))
            {
                continue;
            }

            command = TextNormalizer.RemovePhrase(normalizedText, phrase);
            return true;
        }

        return false;
    }
}
