using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Skills;
using Laura.Core.Localization;

namespace Laura.Core.Engine;

/// <summary>Routes natural-language requests through the selected provider and its shared native tools.</summary>
public sealed class GeminiSkillDispatcher(IConversationEngine conversation, ISettingsService settings, IUserContext user,
    IConversationMemory memory, ILocalizer localizer, SkillDispatcher? localDispatcher = null) : ISkillDispatcher
{
    private bool UseLocalDispatcher => localDispatcher is not null && !conversation.IsAvailable;
    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request) => UseLocalDispatcher ? localDispatcher!.CanUseGenerativeFallback(request) : conversation.IsAvailable;
    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        if (UseLocalDispatcher)
        {
            SkillResponse native = await localDispatcher!.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            return !native.Handled && !conversation.IsAvailable
                ? SkillResponse.Speak(localizer.Get(LocalizationKeys.GenerativeAi.NoProvider)) : native;
        }
        if (!conversation.IsAvailable) { return SkillResponse.Speak(localizer.Get(LocalizationKeys.GenerativeAi.ConfigureProvider)); }
        IReadOnlyList<ConversationMessage> history = [];
        if (settings.Current.RememberConversations)
        {
            try { history = memory.ReadRecent(9); }
            catch (Exception error) when (error is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException) { }
        }
        string? answer = await conversation.AskAsync(new ConversationTurn(request.RawText, request.Culture, user.DisplayName)
        { TurnId = request.TurnId, ContinuingTaskTurnId = request.ContinuingTaskTurnId, History = history.Where(message => request.TurnId == Guid.Empty || message.TurnId != request.TurnId).TakeLast(8).ToArray(), Image = request.Image }, cancellationToken).ConfigureAwait(false);
        return SkillResponse.Speak(string.IsNullOrWhiteSpace(answer) ? (conversation.LastFailure ?? new(ConversationFailureKind.Unknown)).Describe(localizer) : answer);
    }
}
