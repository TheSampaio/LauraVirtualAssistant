using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Localization;

namespace Laura.Core.Skills.Builtin;

/// <summary>Applies an explicit model-provider change through the native settings service.</summary>
public sealed class SwitchProviderSkill(ILocalizer localizer, ISettingsService settings,
    Func<bool> chatGptReady, Func<bool> localReady, Func<bool>? geminiReady = null) : ISkill
{
    /// <inheritdoc />
    public string Id => "builtin.switchProvider";
    /// <inheritdoc />
    public int Priority => 5;
    /// <inheritdoc />
    public bool ModelCallable => false;
    /// <inheritdoc />
    public bool CanHandle(SkillRequest request) => Selection(request) is not null;

    /// <inheritdoc />
    public async Task<SkillResponse> ExecuteAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        ConversationProviderSelection? selection = Selection(request);
        if (selection is null) { return SkillResponse.NotHandled; }
        bool ready = selection switch { ConversationProviderSelection.ChatGpt => chatGptReady(), ConversationProviderSelection.Local => localReady(),
            ConversationProviderSelection.Gemini => geminiReady?.Invoke() == true,
            ConversationProviderSelection.Automatic => chatGptReady() || localReady() || geminiReady?.Invoke() == true, _ => false };
        if (!ready) { return SkillResponse.Speak(localizer.Get(LocalizationKeys.Skills.SwitchProviderUnavailable)); }
        await settings.UpdateAsync(current => current with { ProviderSelection = selection.Value }, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(localizer.Get(selection switch { ConversationProviderSelection.ChatGpt => LocalizationKeys.Skills.SwitchToChatGptResponse,
            ConversationProviderSelection.Gemini => LocalizationKeys.Skills.SwitchToGeminiResponse,
            ConversationProviderSelection.Local => LocalizationKeys.Skills.SwitchToLocalResponse, _ => LocalizationKeys.Skills.SwitchToAutomaticResponse }));
    }

    /// <summary>Matches complete localized commands without interpreting arbitrary model output as a setting.</summary>
    private ConversationProviderSelection? Selection(SkillRequest request)
    {
        if (PhraseMatcher.MatchesWhole(request.NormalizedText, localizer.GetPhrases(LocalizationKeys.Skills.SwitchToLocalPhrases))) { return ConversationProviderSelection.Local; }
        if (PhraseMatcher.MatchesWhole(request.NormalizedText, localizer.GetPhrases(LocalizationKeys.Skills.SwitchToAutomaticPhrases))) { return ConversationProviderSelection.Automatic; }
        if (PhraseMatcher.MatchesWhole(request.NormalizedText, localizer.GetPhrases(LocalizationKeys.Skills.SwitchToChatGptPhrases)))
        { return ConversationProviderSelection.ChatGpt; }
        if (PhraseMatcher.MatchesWhole(request.NormalizedText, localizer.GetPhrases(LocalizationKeys.Skills.SwitchToGeminiPhrases)))
        { return ConversationProviderSelection.Gemini; }
        return null;
    }
}
