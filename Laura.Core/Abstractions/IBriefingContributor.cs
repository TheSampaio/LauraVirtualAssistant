using Laura.Core.Conversation;

namespace Laura.Core.Abstractions;

/// <summary>Supplies verified facts for wake briefings; future email plugins can provide additional sections.</summary>
public interface IBriefingContributor
{
    /// <summary>Collects actual data or returns null when the source is unavailable or has no relevant facts.</summary>
    Task<BriefingSection?> CollectAsync(CancellationToken cancellationToken);
}
