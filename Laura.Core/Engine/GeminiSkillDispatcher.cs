using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Skills;

namespace Laura.Core.Engine;

/// <summary>Routes natural-language requests through Gemini, whose structured tools perform local actions.</summary>
public sealed class GeminiSkillDispatcher(IConversationEngine conversation, ISettingsService settings, IUserContext user,
    IConversationMemory memory, ILocalizer localizer) : ISkillDispatcher
{
    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request) => conversation.IsAvailable;
    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        if (!conversation.IsAvailable) { return SkillResponse.Speak(localizer.Get("ui.maui.configureGoogle")); }
        IReadOnlyList<ConversationMessage> history = [];
        if (settings.Current.RememberConversations)
        {
            try { history = memory.ReadRecent(8); }
            catch (Exception error) when (error is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException) { }
        }
        string? answer = await conversation.AskAsync(new ConversationTurn(request.RawText, request.Culture, user.DisplayName) { History = history }, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(string.IsNullOrWhiteSpace(answer) ? localizer.Get("ui.maui.liveFailed") : answer);
    }
}
