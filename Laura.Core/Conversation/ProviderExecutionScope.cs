namespace Laura.Core.Conversation;

/// <summary>Freezes the trusted runtime's disclosure recipient across asynchronous tool calls and nested adapters.</summary>
public sealed class ProviderExecutionScope : IDisposable
{
    private static readonly AsyncLocal<string?> ActiveProvider = new();
    private readonly string? _previous;
    private bool _disposed;

    /// <summary>Enters a known provider scope. Args: provider: Native runtime identifier. Raises: ArgumentException for unknown providers.</summary>
    public ProviderExecutionScope(string provider)
    {
        if (provider == "auto") { provider = "local"; }
        if (provider is not ("gemini" or "openai" or "local")) { throw new ArgumentException("Unknown execution provider.", nameof(provider)); }
        _previous = ActiveProvider.Value; ActiveProvider.Value = provider;
    }

    /// <summary>Gets the active runtime identity, or null outside provider execution.</summary>
    public static string? Provider => ActiveProvider.Value;

    /// <summary>Gets the fixed actual disclosure host. Raises: InvalidOperationException when no runtime is bound.</summary>
    public static string Destination => Provider switch
    {
        "gemini" => "generativelanguage.googleapis.com",
        "openai" => "api.openai.com",
        "local" => "",
        _ => throw new InvalidOperationException("Disclosure requires a trusted execution provider."),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true; ActiveProvider.Value = _previous;
    }
}
