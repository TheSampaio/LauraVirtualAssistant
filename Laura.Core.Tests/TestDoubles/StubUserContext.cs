using Laura.Core.Abstractions;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>
/// Test user context with a fixed name.
/// </summary>
public sealed class StubUserContext : IUserContext
{
    /// <inheritdoc />
    public string DisplayName => "Kellvyn";

    /// <inheritdoc />
    public string AccountName => "Kellvyn";
}
