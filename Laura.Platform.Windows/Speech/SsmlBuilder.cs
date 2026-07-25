using System.Globalization;
using System.Security;
using System.Text;
using Laura.Core.Configuration;

namespace Laura.Platform.Windows.Speech;

/// <summary>
/// Builds the SSML document handed to SAPI.
///
/// Pitch is the one parameter SAPI does not expose as a synthesizer property; only
/// the <c>prosody</c> markup reaches it, which is why every utterance goes through
/// SSML. Pitch is expressed in semitones rather than a percentage because the
/// desktop voices honor semitone shifts far more audibly.
/// </summary>
internal static class SsmlBuilder
{
    // Each UI step is one semitone; the -10..10 range then spans roughly an octave
    // and a half, which is clearly audible on the Windows desktop voices.
    private const int SemitonesPerProsodyStep = 1;

    /// <summary>
    /// Builds the SSML for an utterance.
    ///
    /// Args:
    ///     text: Text to speak, in natural language.
    ///     voice: Voice profile the pitch is read from.
    ///     culture: Culture declared in the document.
    ///
    /// Returns:
    ///     A valid SSML document, with the text properly escaped.
    /// </summary>
    internal static string Build(string text, VoiceProfile voice, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(culture);

        string escapedText = SecurityElement.Escape(text) ?? string.Empty;
        int semitones = voice.Pitch * SemitonesPerProsodyStep;

        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"""
            <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{culture.Name}">
            """);

        builder.Append(CultureInfo.InvariantCulture, $"""<prosody pitch="{semitones:+0;-0;+0}st">""");
        builder.Append(escapedText);
        builder.Append("</prosody></speak>");

        return builder.ToString();
    }
}
