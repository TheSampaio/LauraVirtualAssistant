using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Diagnostics;

namespace Laura.Core.Conversation;

/// <summary>Routes native task demand under owner consent and drains work before changing providers or clearing dialogue.</summary>
public sealed class ConversationProviderRouter(ISettingsService settings, IConversationEngine? chatGpt = null,
    Engine.CognitiveTasks? tasks = null, IConversationEngine? local = null, IConversationEngine? gemini = null) : IConversationEngine, IDisposable
{
    private readonly SemaphoreSlim _requests = new(1, 1), _resets = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private bool _resetting, _disposed;
    private string _activeProvider = "";
    private static readonly string[] ProviderOrder = ["chatgpt", "gemini", "local"];
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.Ordinal);
    private IConversationEngine? Selected => settings.Current.ProviderSelection switch
    {
        ConversationProviderSelection.ChatGpt => chatGpt,
        ConversationProviderSelection.Local => settings.Current.LocalModel.Enabled ? local : null,
        ConversationProviderSelection.Gemini => GeminiAuthorized ? gemini : null,
        _ => null
    };
    private bool AutomaticAuthorized => settings.Current.ChatGpt is { Enabled: true, AllowAutomatic: true, ShareContext: true, IncludedPlanOnly: true };
    private bool GeminiAuthorized => settings.Current.GenerativeAi is { Enabled: true, Provider: "gemini" };
    /// <inheritdoc />
    public bool IsAvailable { get { lock (_gate) { return !_resetting && !_disposed && (Selected?.IsAvailable == true
        || settings.Current.ProviderSelection == ConversationProviderSelection.Automatic && (AutomaticAuthorized && chatGpt?.IsAvailable == true
            || GeminiAuthorized && gemini?.IsAvailable == true || settings.Current.LocalModel.Enabled && local?.IsAvailable == true)); } } }
    /// <inheritdoc />
    public bool SupportsImages => IsAvailable && Selected?.SupportsImages == true;
    /// <inheritdoc />
    public ConversationFailure? LastFailure { get; private set; }
    /// <summary>Gets the sole configured conversation provider without enabling cloud access.</summary>
    public string ActiveProvider => settings.Current.ProviderSelection switch { ConversationProviderSelection.ChatGpt => "chatgpt", ConversationProviderSelection.Local => "local",
        ConversationProviderSelection.Gemini => "gemini", _ => _activeProvider.Length > 0 ? _activeProvider
            : settings.Current.GenerativeAi.LightProvider == LightProviderSelection.Gemini ? "gemini" : "local" };
    /// <inheritdoc />
    public string? PendingRound(Guid taskId) => Engine(_activeProvider)?.PendingRound(taskId);
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
                { await tasks.RecordProviderAsync(taskId, provider, Model(provider, turn.Difficulty), decision, operation.Token, turn.Difficulty).ConfigureAwait(false); }
                LauraDiagnostics.Record("provider.selected", outcome: provider, taskId: taskId);
                LauraDiagnostics.Record("provider.routing.reason", outcome: decision.Reason.ToString(), taskId: taskId);
                LauraDiagnostics.Record("task.difficulty", outcome: turn.Difficulty.ToString(), taskId: taskId);
                if (turn.Image is not null && selected?.SupportsImages != true)
                {
                    LastFailure = new(ConversationFailureKind.UnsupportedInput); return null;
                }
                if (selected?.IsAvailable != true) { LastFailure = new(provider == "max_unavailable" ? ConversationFailureKind.MaxAuthorizationRequired : ConversationFailureKind.Unavailable); return null; }
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
                    var attempted = new HashSet<string>(StringComparer.Ordinal) { provider };
                    string? answer;
                    try { answer = await selected.AskAsync(turn, operation.Token).ConfigureAwait(false); }
                    catch (ConversationProviderException error) { LastFailure = error.Failure; answer = null; }
                    if (LastFailure is null)
                    { LastFailure = string.IsNullOrWhiteSpace(answer) && selected.PendingRound(taskId) is null
                        ? selected.LastFailure ?? new(ConversationFailureKind.Unknown) : null; }
                    while (LastFailure is { EffectsMayHaveRun: false } failure && CanRetryWithAlternate(turn, failure, selected))
                    {
                        _cooldowns[provider] = DateTimeOffset.UtcNow.AddSeconds(failure.Kind is ConversationFailureKind.Authentication or ConversationFailureKind.UsageLimit ? 120 : 30);
                        string alternateName = ProviderOrder.FirstOrDefault(name => !attempted.Contains(name) && Authorized(name) && Healthy(name)) ?? "";
                        var alternate = Engine(alternateName);
                        if (alternate?.IsAvailable == true)
                        {
                            LauraDiagnostics.Record("provider.fallback", outcome: provider + "_to_" + alternateName, taskId: taskId);
                            _activeProvider = alternateName;
                            attempted.Add(alternateName);
                            if (tasks is not null)
                            { await tasks.RecordProviderAsync(taskId, alternateName, Model(alternateName, turn.Difficulty),
                                decision with { Reason = CognitiveRoutingReason.ProviderCondition }, operation.Token, turn.Difficulty).ConfigureAwait(false); }
                            try { answer = await alternate.AskAsync(turn, operation.Token).ConfigureAwait(false); LastFailure = null; }
                            catch (ConversationProviderException error) { LastFailure = error.Failure; answer = null; }
                            LastFailure ??= string.IsNullOrWhiteSpace(answer) && alternate.PendingRound(taskId) is null
                                ? alternate.LastFailure ?? new(ConversationFailureKind.Unknown) : null;
                            provider = alternateName; selected = alternate;
                        }
                        else { break; }
                    }
                    return answer;
                }
                catch (ConversationProviderException error) { LastFailure = error.Failure; return null; }
            }
            finally { _requests.Release(); }
        }
    }
    /// <summary>Retries only an initial automatic text round whose first provider confirmed no native effects.</summary>
    private bool CanRetryWithAlternate(ConversationTurn turn, ConversationFailure failure, IConversationEngine selected)
        => settings.Current.ProviderSelection == ConversationProviderSelection.Automatic
            && turn.Difficulty != Models.TaskDifficulty.Light
            && turn.ContinuingTaskTurnId == Guid.Empty && turn.Image is null && !failure.EffectsMayHaveRun && !selected.HasProvisionalOutput
            && failure.Kind is ConversationFailureKind.Authentication or ConversationFailureKind.UsageLimit
                or ConversationFailureKind.CapacityLimit or ConversationFailureKind.ModelUnavailable
                or ConversationFailureKind.Timeout or ConversationFailureKind.Network or ConversationFailureKind.Unavailable or ConversationFailureKind.InvalidResponse
            && ProviderOrder.Any(name => name != _activeProvider && Authorized(name) && Healthy(name));
    /// <summary>Selects the initial provider from native signals while preserving manual owner choices.</summary>
    private (IConversationEngine? Engine, string Provider, CognitiveTaskRouting Decision) Select(ConversationTurn turn)
    {
        var decision = turn.TaskRouting ?? new(turn.TaskDemand, turn.TaskDemand == CognitiveTaskDemand.Routine ? CognitiveRoutingReason.Routine : CognitiveRoutingReason.ComplexPlan);
        if (turn.IsStartupBriefing && settings.Current.ProviderSelection == ConversationProviderSelection.Automatic)
        { return SelectLight(decision); }
        if (turn.Difficulty == Models.TaskDifficulty.Max)
        {
            bool selectedGpt = settings.Current.ProviderSelection is ConversationProviderSelection.Automatic or ConversationProviderSelection.ChatGpt;
            bool permitted = settings.Current.ChatGpt.AllowMax && settings.Current.ChatGpt.MaxModel.Length > 0 && selectedGpt
                && (settings.Current.ProviderSelection == ConversationProviderSelection.ChatGpt || AutomaticAuthorized);
            if (permitted && Healthy("chatgpt")) { return (chatGpt, "chatgpt", decision); }
            return (null, "max_unavailable", decision);
        }
        if (settings.Current.ProviderSelection != ConversationProviderSelection.Automatic)
        { return (Selected, settings.Current.ProviderSelection switch { ConversationProviderSelection.ChatGpt => "chatgpt", ConversationProviderSelection.Gemini => "gemini", _ => "local" },
            decision with { Reason = CognitiveRoutingReason.Manual }); }
        if (turn.Difficulty == Models.TaskDifficulty.Light) { return SelectLight(decision); }
        if (decision.Recovery != CognitiveRecoveryAction.None && decision.ContinuityProvider is "chatgpt" or "local" or "gemini")
        {
            return (Authorized(decision.ContinuityProvider) ? Engine(decision.ContinuityProvider) : null, decision.ContinuityProvider, decision);
        }
        bool complex = decision.Demand is CognitiveTaskDemand.Planning or CognitiveTaskDemand.Analysis or CognitiveTaskDemand.Synthesis;
        if (AutomaticAuthorized && Healthy("chatgpt") && (turn.Image is not null || complex || turn.Difficulty >= Models.TaskDifficulty.Standard || !Healthy("local")))
        { return (chatGpt, "chatgpt", decision); }
        if (GeminiAuthorized && Healthy("gemini")) { return (gemini, "gemini", decision); }
        if (turn.Image is null && turn.Difficulty <= settings.Current.LocalModel.MaximumDifficulty && Authorized("local") && Healthy("local"))
        { return (local, "local", decision); }
        if (complex) { decision = decision with { Reason = AutomaticAuthorized || GeminiAuthorized ? CognitiveRoutingReason.SecondaryUnavailable : CognitiveRoutingReason.SecondaryNotAuthorized }; }
        if (turn.Image is null && Authorized("local") && Healthy("local")) { return (local, "local", decision); }
        return (null, GeminiAuthorized ? "gemini" : "local", decision);
    }
    /// <summary>Respects the owner's Light choice without starting the other local or cloud model.</summary>
    private (IConversationEngine? Engine, string Provider, CognitiveTaskRouting Decision) SelectLight(CognitiveTaskRouting decision)
        => settings.Current.GenerativeAi.LightProvider == LightProviderSelection.Gemini
            ? (GeminiAuthorized ? gemini : null, "gemini", decision)
            : (settings.Current.LocalModel.Enabled ? local : null, "local", decision);
    /// <summary>Resolves only registered provider adapters.</summary>
    private IConversationEngine? Engine(string provider) => provider switch { "chatgpt" => chatGpt, "gemini" => gemini, "local" => local, _ => null };
    /// <summary>Checks native owner consent independently of provider availability.</summary>
    private bool Authorized(string provider) => provider switch { "chatgpt" => AutomaticAuthorized, "gemini" => GeminiAuthorized,
        "local" => settings.Current.LocalModel.Enabled, _ => false };
    /// <summary>Applies a bounded cooldown only to automatic routing.</summary>
    private bool Healthy(string provider) => Engine(provider)?.IsAvailable == true && (!_cooldowns.TryGetValue(provider, out var until) || until <= DateTimeOffset.UtcNow);
    /// <summary>Reads the actual configured model without inventing a local or account model identity.</summary>
    private string Model(string provider, Models.TaskDifficulty difficulty) => provider switch
    { "chatgpt" when difficulty == Models.TaskDifficulty.Max => settings.Current.ChatGpt.MaxModel,
      "chatgpt" => settings.Current.ChatGpt.Model, "gemini" => settings.Current.GenerativeAi.Model.Length == 0 ? GeminiLiveOptions.Model : settings.Current.GenerativeAi.Model,
      "local" => Path.GetFileNameWithoutExtension(settings.Current.LocalModel.ModelPath), _ => "" };
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
                if (gemini is not null) { await gemini.ResetSessionAsync().ConfigureAwait(false); }
                if (local is not null) { await local.ResetSessionAsync().ConfigureAwait(false); }
                await clearHistory().ConfigureAwait(false);
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
