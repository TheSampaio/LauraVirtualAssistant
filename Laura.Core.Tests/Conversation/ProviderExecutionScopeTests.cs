using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks trusted recipient binding independently of model arguments and mutable settings.</summary>
public sealed class ProviderExecutionScopeTests
{
    [Fact]
    public async Task ConcurrentProvidersAndNestedScopesRetainTheirActualRecipient()
    {
        Assert.Null(ProviderExecutionScope.Provider);
        await Task.WhenAll(CheckAsync("gemini", "generativelanguage.googleapis.com"), CheckAsync("openai", "api.openai.com"));
        Assert.Null(ProviderExecutionScope.Provider);
        Assert.Throws<InvalidOperationException>(() => ProviderExecutionScope.Destination);
    }

    /// <summary>Checks asynchronous flow, nested local execution and restoration. Args: provider: Runtime identity; recipient: Expected destination.</summary>
    private static async Task CheckAsync(string provider, string recipient)
    {
        using var scope = new ProviderExecutionScope(provider);
        await Task.Yield(); Assert.Equal(recipient, ProviderExecutionScope.Destination);
        using (var local = new ProviderExecutionScope("local")) { await Task.Yield(); Assert.Equal("", ProviderExecutionScope.Destination); }
        Assert.Equal(recipient, ProviderExecutionScope.Destination);
    }
}
