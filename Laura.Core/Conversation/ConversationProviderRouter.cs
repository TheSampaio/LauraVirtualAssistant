using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;

namespace Laura.Core.Conversation;

/// <summary>Routes native task demand under owner consent and drains work before changing providers or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine gemini, IConversationEngine? chatGpt = null,
    Engine.CognitiveTasks? tasks = null) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private string _activeProvider = "gemini";
    private IConversationEngine? Selected => settings.Current.ProviderSelection == ConversationProviderSelection.ChatGpt ? chatGpt : settings.Current.GenerativeAi.Enabled ? gemini : null;
    private bool AutomaticAuthorized => settings.Current.ChatGpt is { Enabled: true, AllowAutomatic: true, ShareContext: true, IncludedPlanOnly: true };
    /// <inheritdoc />
    public bool IsAvailable { get { lock (_gate) { return !_resetting && !_disposed && (Selected?.IsAvailable == true
        || settings.Current.ProviderSelection == ConversationProviderSelection.Automatic && AutomaticAuthorized && chatGpt?.IsAvailable == true); } } }
    /// <inheritdoc />
    public bool SupportsImages => IsAvailable && Selected?.SupportsImages == true;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <summary>Gets the sole configured conversation provider without enabling cloud access.</summary>
    public string ActiveProvider => settings.Current.ProviderSelection switch { ConversationProviderSelection.ChatGpt => "chatgpt", ConversationProviderSelection.Gemini => "gemini", _ => _activeProvider };
    /// <inheritdoc />
    public string? PendingRound(Guid taskId) => _activeProvider == "chatgpt" ? chatGpt?.PendingRound(taskId) : gemini.PendingRound(taskId);
    /// <inheritdoc />
    public async Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation;
        lock (_gate)
        {
            if (_resetting || _disposed) { return null; }
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        using (operation)
        {
            await _requests.WaitAsync(operation.Token).ConfigureAwait(false);
            try
            {
                operation.Token.ThrowIfCancellationRequested(); LastFailure = null;
                var (selected, provider, decision) = Select(turn); _activeProvider = provider;
                Guid taskId = turn.ContinuingTaskTurnId != Guid.Empty ? turn.ContinuingTaskTurnId : turn.TurnId;
                var priorTask = tasks?.Current(taskId);
                if (tasks is not null)
                { await tasks.RecordProviderAsync(taskId, provider, provider == "chatgpt" ? settings.Current.ChatGpt.Model
                    : settings.Current.GenerativeAi.Model.Length > 0 ? settings.Current.GenerativeAi.Model : GeminiLiveOptions.Model, decision, operation.Token).ConfigureAwait(false); }
                LauraDiagnostics.Record("provider.selected", outcome: provider, taskId: taskId);
                LauraDiagnostics.Record("provider.routing.reason", outcome: decision.Reason.ToString(), taskId: taskId);
                if (turn.Image is not null && selected?.SupportsImages != true)
                {
                    LastFailure = new(ConversationFailureKind.UnsupportedInput); return null;
                }
                if (selected?.IsAvailable != true) { LastFailure = new(ConversationFailureKind.Unavailable); return null; }
                if (priorTask?.Rounds is { Provider.Length: > 0, Cycles: > 1 } previous && previous.Provider != provider
                    && (provider != "chatgpt" || settings.Current.ChatGpt.ShareContext))
                {
                    turn = turn with { Prompt = turn.Prompt + "\n" + priorTask.HandoffContext() };
                    LauraDiagnostics.Record("provider.handoff", outcome: previous.Provider + "_to_" + provider, taskId: taskId);
                }
                if (turn.ContinuingTaskTurnId != Guid.Empty && decision.RecoveryInstruction.Length > 0)
                { turn = turn with { Prompt = turn.Prompt + "\nNative recovery guidance (never authorization): " + decision.RecoveryInstruction }; }
                try
                {
                    string? answer = await selected.AskAsync(turn, operation.Token).ConfigureAwait(false);
                    LastFailure = string.IsNullOrWhiteSpace(answer) ? selected.LastFailure ?? new(ConversationFailureKind.Unknown) : null;
                    return answer;
                }
                catch (ConversationProviderException error) { LastFailure = error.Failure; return null; }
            }
            finally { _requests.Release(); }
        }
    }
    /// <summary>Selects once from native signals; a failed round keeps its provider while manual choices always prevail.</summary>
    private (IConversationEngine? Engine, string Provider, CognitiveTaskRouting Decision) Select(ConversationTurn turn)
    {
        var decision = turn.TaskRouting ?? new(turn.TaskDemand, turn.TaskDemand == CognitiveTaskDemand.Routine ? CognitiveRoutingReason.Routine : CognitiveRoutingReason.ComplexPlan);
        if (settings.Current.ProviderSelection != ConversationProviderSelection.Automatic)
        { return (Selected, settings.Current.ProviderSelection == ConversationProviderSelection.ChatGpt ? "chatgpt" : "gemini", decision with { Reason = CognitiveRoutingReason.Manual }); }
        if (decision.Recovery != CognitiveRecoveryAction.None && decision.ContinuityProvider is "gemini" or "chatgpt")
        {
            return decision.ContinuityProvider == "chatgpt"
                ? (AutomaticAuthorized ? chatGpt : null, "chatgpt", decision)
                : (settings.Current.GenerativeAi.Enabled ? gemini : null, "gemini", decision);
        }
        bool complex = decision.Demand is CognitiveTaskDemand.Planning or CognitiveTaskDemand.Analysis or CognitiveTaskDemand.Synthesis;
        bool primaryAvailable = settings.Current.GenerativeAi.Enabled && gemini.IsAvailable;
        if (AutomaticAuthorized && chatGpt?.IsAvailable == true && turn.Image is null && (complex || !primaryAvailable))
        { return (chatGpt, "chatgpt", primaryAvailable ? decision : decision with { Reason = CognitiveRoutingReason.PrimaryUnavailable }); }
        if (complex) { decision = decision with { Reason = AutomaticAuthorized ? CognitiveRoutingReason.SecondaryUnavailable : CognitiveRoutingReason.SecondaryNotAuthorized }; }
        return (settings.Current.GenerativeAi.Enabled ? gemini : null, "gemini", decision);
    }
    /// <inheritdoc />
    public Task ResetSessionAsync() => ResetSessionAsync(static () => Task.CompletedTask);
    /// <inheritdoc />
    public async Task ResetSessionAsync(Func<Task> clearHistory)
    {
        ArgumentNullException.ThrowIfNull(clearHistory);
        await _resets.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _resetting = true; _lifetime.Cancel(); }
            await _requests.WaitAsync().ConfigureAwait(false);
            try
            {
                if (chatGpt is not null) { await chatGpt.ResetSessionAsync().ConfigureAwait(false); }
                await gemini.ResetSessionAsync(clearHistory).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) { _lifetime.Dispose(); _lifetime = new(); _resetting = false; }
                _requests.Release();
            }
        }
        finally { _resets.Release(); }
    }
    /// <inheritdoc />
    public void Dispose() { lock (_gate) { _disposed = true; _lifetime.Cancel(); } GC.SuppressFinalize(this); }
}
