using Laura.Core.Abstractions;
using Laura.Core.Localization;
using Laura.Core.Text;

namespace Laura.Core.Conversation;

/// <summary>Recognizes complete standby requests without matching quoted or negated phrases.</summary>
public sealed class ListeningControl(ILocalizer localizer)
{
    /// <summary>Checks localized whole-command variants, including punctuation and accent differences.</summary>
    public bool IsRestRequest(string text)
    {
        string normalized = TextNormalizer.Normalize(text);
        return localizer.GetPhrases(LocalizationKeys.Listening.RestPhrases)
            .Any(phrase => TextNormalizer.Normalize(phrase) == normalized);
    }
    /// <summary>Gets the localized confirmation for the conversation history.</summary>
    public string RestConfirmation => localizer.Get(LocalizationKeys.Listening.RestConfirmation);
}
