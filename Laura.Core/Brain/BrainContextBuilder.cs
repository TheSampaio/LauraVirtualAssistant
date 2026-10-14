using Laura.Core.Abstractions;
using Laura.Core.Diagnostics;
using Laura.Core.Text;

namespace Laura.Core.Brain;

/// <summary>Adapts proactive retrieval to the interaction and caches only bounded factual context from the same coherent snapshot and settings.</summary>
public sealed class BrainContextBuilder(BrainService brain, ISettingsService settings, BrainSkills? skills = null)
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private BrainSnapshot? _snapshot;
    private BrainOptions? _options;
    /// <summary>Skips only a closed list of trivial messages; unfamiliar short requests still receive retrieval.</summary>
    public static bool NeedsKnowledge(string input) => TextNormalizer.Normalize(input) is not
        ("oi" or "ola" or "bom dia" or "boa tarde" or "boa noite" or "obrigado" or "obrigada" or "tchau"
        or "hi" or "hello" or "good morning" or "good afternoon" or "good evening" or "thanks" or "thank you" or "bye"
        or "que horas sao" or "what time is it" or "qual a data de hoje" or "whats the date today");
    /// <summary>Prepares factual context and current skill metadata under the existing context budget; never conveys tool authorization.</summary>
    public async Task<BrainContext> BuildAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        using var measurement = LauraDiagnostics.Measure("cognition.context");
        if (!NeedsKnowledge(input)) { LauraDiagnostics.Record("cognition.retrieval.skipped"); return new("", ""); }
        string query = input.Length <= 4000 ? input : input[..2000] + "\n" + input[^1999..];
        await brain.RefreshIfDirtyAsync(cancellationToken).ConfigureAwait(false);
        BrainOptions options = settings.Current.Brain;
        string catalogue = skills is null ? "" : await skills.DiscoverAsync(query, Math.Min(2000, options.ContextCharacters / 2), cancellationToken).ConfigureAwait(false);
        BrainSnapshot snapshot = brain.Snapshot;
        string? context = null;
        if (settings.Current.RememberConversations)
        {
            lock (_cacheGate)
            {
                if (!ReferenceEquals(snapshot, _snapshot) || options != _options) { _cache.Clear(); _snapshot = snapshot; _options = options; }
                _cache.TryGetValue(query, out context);
            }
            if (context is null)
            {
                context = await brain.RecallAsync(query, cancellationToken).ConfigureAwait(false);
                lock (_cacheGate)
                {
                    if (ReferenceEquals(snapshot, brain.Snapshot) && ReferenceEquals(snapshot, _snapshot))
                    { if (_cache.Count >= 32) { _cache.Clear(); } _cache[query] = context; }
                }
            }
            else { LauraDiagnostics.Record("cognition.retrieval.cache-hit"); }
        }
        context ??= "";
        int remaining = Math.Max(0, options.ContextCharacters - catalogue.Length);
        return new(context.Length > remaining ? context[..remaining] : context, catalogue);
    }
}

/// <summary>Separates untrusted recalled facts from skill discovery metadata; approved guidance still requires the existing read gate.</summary>
public sealed record BrainContext(string Memories, string SkillCatalogue);
