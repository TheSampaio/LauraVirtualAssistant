using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.TestDoubles;

/// <summary>Records native local dispatch identity and provides scripted actual adapter outcomes.</summary>
internal sealed class LocalAgentTools : IAssistantTools
{
    internal int Calls;
    internal string? ActiveProvider;
    internal AssistantToolResult Result { get; set; } = new("Observed fixture value: 731.");
    internal Func<CancellationToken, Task<AssistantToolResult>>? Execute { get; set; }
    /// <inheritdoc />
    public object[] Declarations { get; } = [new { functionDeclarations = new[] { new { name = "probe", description = "Read one synthetic fixture value.",
        parameters = new { type = "OBJECT", properties = new { text = new { type = "STRING" } }, required = new[] { "text" } } } } }];
    /// <inheritdoc />
    public bool IsRegisteredTool(string name) => name == "probe";
    /// <inheritdoc />
    public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        Calls++; ActiveProvider = ProviderExecutionScope.Provider;
        return Execute?.Invoke(cancellationToken) ?? Task.FromResult(Result);
    }
}
