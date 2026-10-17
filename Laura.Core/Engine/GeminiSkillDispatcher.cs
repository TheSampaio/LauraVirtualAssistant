using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Skills;
using Laura.Core.Localization;

namespace Laura.Core.Engine;

/// <summary>Routes natural-language requests through the selected provider and its shared native tools.</summary>
public sealed class GeminiSkillDispatcher(IConversationEngine conversation, ISettingsService settings, IUserContext user,
    IConversationMemory memory, ILocalizer localizer, SkillDispatcher? localDispatcher = null, CognitiveTasks? tasks = null, IAssistantTools? tools = null,
    ConversationProviderRouter? routing = null) : ISkillDispatcher
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
        Guid taskId = request.ContinuingTaskTurnId != Guid.Empty ? request.ContinuingTaskTurnId : request.TurnId;
        bool tracked = tasks is not null && tools is not null && taskId != Guid.Empty;
        string objective = request.ContinuingTaskTurnId != Guid.Empty ? memory.WorkingContext.UserText : request.RawText;
        if (tracked && !await tasks!.BeginCycleAsync(new(taskId, objective), tools!.RegisteredToolNames, settings.Current.CognitiveTasks, cancellationToken).ConfigureAwait(false))
        { return SkillResponse.Speak(new ConversationFailure(ConversationFailureKind.OperationLimit).Describe(localizer)); }
        using var operation = tracked ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, tasks!.CycleCancellation(taskId))
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (tracked && tasks!.Current(taskId)?.Rounds is { } admitted)
        {
            operation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, admitted.MaximumActiveMilliseconds - admitted.ActiveMilliseconds)));
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp(); string? answer = null; bool canceled = false;
        try
        {
            if (tracked && !await tasks!.AdmitInferenceAsync(taskId, operation.Token).ConfigureAwait(false))
            { return SkillResponse.Speak(new ConversationFailure(ConversationFailureKind.OperationLimit).Describe(localizer)); }
            var checkpoint = tracked ? tasks!.Current(taskId) : null;
            CognitiveTaskRouting decision = CognitiveTaskRoutingPolicy.Evaluate(checkpoint);
            answer = await conversation.AskAsync(new ConversationTurn(request.RawText, request.Culture, user.DisplayName)
            { TurnId = request.TurnId, ContinuingTaskTurnId = request.ContinuingTaskTurnId, TaskDemand = decision.Demand, TaskRouting = decision,
                History = history.Where(message => request.TurnId == Guid.Empty || message.TurnId != request.TurnId).TakeLast(8).ToArray(), Image = request.Image }, operation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { canceled = true; throw; }
        finally
        {
            if (tracked)
            {
                string provider = routing?.ActiveProvider ?? "gemini";
                string model = tasks!.Current(taskId)?.Rounds is { } active && active.Provider == provider && active.Model.Length > 0
                    ? active.Model : provider == "chatgpt" ? settings.Current.ChatGpt.Model : settings.Current.GenerativeAi.Model;
                await tasks!.EndCycleAsync(taskId, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, provider,
                    model, conversation.LastFailure, canceled).ConfigureAwait(false);
            }
        }
        return SkillResponse.Speak(string.IsNullOrWhiteSpace(answer) ? (conversation.LastFailure ?? new(ConversationFailureKind.Unknown)).Describe(localizer) : answer);
    }
}
