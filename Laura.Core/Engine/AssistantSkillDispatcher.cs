using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Configuration;
using Laura.Core.Skills;
using Laura.Core.Localization;

namespace Laura.Core.Engine;

/// <summary>Routes natural-language requests through the selected provider and its shared native tools.</summary>
public sealed class AssistantSkillDispatcher(IConversationEngine conversation, ISettingsService settings, IUserContext user,
    IConversationMemory memory, ILocalizer localizer, SkillDispatcher? localDispatcher = null, CognitiveTasks? tasks = null, IAssistantTools? tools = null,
    ConversationProviderRouter? routing = null) : ISkillDispatcher
{
    /// <inheritdoc />
    public bool CanUseGenerativeFallback(SkillRequest request) => conversation.IsAvailable && localDispatcher?.CanHandleNatively(request) != true;
    /// <inheritdoc />
    public async Task<SkillResponse> DispatchAsync(SkillRequest request, CancellationToken cancellationToken = default)
    {
        if (localDispatcher is not null)
        {
            SkillResponse native = await localDispatcher.DispatchNativeAsync(request, cancellationToken).ConfigureAwait(false);
            if (native.Handled) { Diagnostics.LauraDiagnostics.Record("task.difficulty", outcome: Models.TaskDifficulty.Native.ToString(), taskId: request.TurnId); return native; }
            if (!conversation.IsAvailable) { return SkillResponse.Speak(localizer.Get(LocalizationKeys.GenerativeAi.NoProvider)); }
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
            { TurnId = request.TurnId, ContinuingTaskTurnId = request.ContinuingTaskTurnId, TaskDemand = decision.Demand, TaskRouting = decision, IsStartupBriefing = request.IsStartupBriefing,
                History = history.Where(message => request.TurnId == Guid.Empty || message.TurnId != request.TurnId).TakeLast(8).ToArray(), Image = request.Image }, operation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { canceled = true; throw; }
        finally
        {
            if (tracked)
            {
                string provider = routing?.ActiveProvider ?? "local";
                string model = tasks!.Current(taskId)?.Rounds is { } active && active.Provider == provider && active.Model.Length > 0
                    ? active.Model : provider switch
                    {
                        "chatgpt" => settings.Current.ChatGpt.Model,
                        "gemini" => settings.Current.GenerativeAi.Model.Length > 0 ? settings.Current.GenerativeAi.Model : GeminiLiveOptions.Model,
                        _ => Path.GetFileNameWithoutExtension(settings.Current.LocalModel.ModelPath)
                    };
                await tasks!.EndCycleAsync(taskId, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, provider,
                    model, conversation.LastFailure, canceled).ConfigureAwait(false);
            }
        }
        if (string.IsNullOrWhiteSpace(answer) && conversation.LastFailure is null && conversation.PendingRound(taskId) is not null)
        { return SkillResponse.Silent(); }
        return SkillResponse.Speak(string.IsNullOrWhiteSpace(answer) ? (conversation.LastFailure ?? new(ConversationFailureKind.Unknown)).Describe(localizer) : answer);
    }
}
