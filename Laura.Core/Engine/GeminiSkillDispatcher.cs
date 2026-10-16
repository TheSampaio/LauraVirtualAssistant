using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Skills;

namespace Laura.Core.Engine;

/// <summary>Routes natural-language requests through the selected provider and its shared native tools.</summary>
public sealed class GeminiSkillDispatcher(IConversationEngine conversation, ISettingsService settings, IUserContext user,
    IConversationMemory memory, ILocalizer localizer) : ISkillDispatcher
{
    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request) => conversation.IsAvailable;
    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        if (!conversation.IsAvailable) { return SkillResponse.Speak(localizer.Get(settings.Current.GenerativeAi.Provider is "local" or "auto" ? "ui.models.noProvider" : "ui.ai.configureProvider")); }
        IReadOnlyList<ConversationMessage> history = [];
        if (settings.Current.RememberConversations)
        {
            try { history = memory.ReadRecent(9); }
            catch (Exception error) when (error is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException) { }
        }
        string? answer = await conversation.AskAsync(new ConversationTurn(request.RawText, request.Culture, user.DisplayName)
        { TurnId = request.TurnId, History = history.Where(message => request.TurnId == Guid.Empty || message.TurnId != request.TurnId).TakeLast(8).ToArray(), Image = request.Image }, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(string.IsNullOrWhiteSpace(answer) ? localizer.Get("ui.ai.providerFailed") : answer);
    }
}
