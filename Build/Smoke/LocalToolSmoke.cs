using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Laura.Core.Models;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>Exercises real local tool selection with synthetic read-only adapters, never desktop, microphone or cloud effects.</summary>
internal static class LocalToolSmoke
{
    /// <summary>Loads pinned weights and validates EN/PT structured selection, actual observations and private argument isolation.</summary>
    internal static async Task RunAsync(string directory, string runtimeDirectory, CancellationToken token)
    {
        using var http = new HttpClient();
        using var models = new LocalModelManager(Path.Combine(directory, "Models"), http, SupportedModels.All);
        using var settings = new SettingsService(new IniSettingsStore(Path.Combine(directory, "local-tool-settings.ini"), NullLogger<IniSettingsStore>.Instance), NullLogger<SettingsService>.Instance);
        await settings.LoadAsync(token);
        await settings.UpdateAsync(settings.Current with { GenerativeAi = settings.Current.GenerativeAi with { LocalGpuLayers = -1, LocalContextTokens = 4096 } }, token);
        var memory = new SqliteConversationMemory(Path.Combine(directory, "local-tool-memory.db")); memory.Initialize();
        var tools = new SyntheticTools();
        var backend = new WindowsInferenceBackend(true, runtimeDirectory, allowCpuFallback: false);
        await using var inference = new LlamaLocalInferenceEngine(models, settings, memory, backend: backend);
        var observer = new ToolInferenceObserver(inference);
        var agent = new LocalToolConversationEngine(observer, tools, memory, settings);
        int leaked = 0; inference.TokenReceived += (_, _) => leaked++; inference.StreamUpdated += (_, _) => leaked++;
        await inference.LoadAsync(SupportedModels.Intelligence.Id, token);
        if (!inference.HasReviewedToolTemplate) { throw new InvalidOperationException("Pinned GGUF does not contain the reviewed Qwen function/parameter template."); }
        var measurements = new List<object>();
        string literal = "List<int> values = [];\n<div>literal</div>";
        foreach (var (culture, prompt, name, arguments) in new[]
        {
            ("en-US", "Use read_sensor to read sensor alpha and tell me its actual value.", "read_sensor", JsonSerializer.SerializeToElement(new { name = "alpha" })),
            ("pt-BR", "Use read_sensor para ler o sensor beta e me diga o valor real.", "read_sensor", JsonSerializer.SerializeToElement(new { name = "beta" })),
            ("pt-BR", "Use inspect_payload com payload exatamente igual a {\"count\":3,\"enabled\":true,\"items\":[1,2]} e diga o valor real devolvido pela ferramenta.",
                "inspect_payload", JsonSerializer.SerializeToElement(new { payload = new { count = 3, enabled = true, items = new[] { 1, 2 } } })),
            ("en-US", "Use inspect_literal with text exactly equal to this JSON string: " + JsonSerializer.Serialize(literal) + ". Report the actual value returned by the tool.",
                "inspect_literal", JsonSerializer.SerializeToElement(new { text = literal })),
        })
        {
            tools.ExpectedName = name; tools.ExpectedArguments = arguments;
            int before = tools.Calls; observer.Shapes.Clear(); var watch = System.Diagnostics.Stopwatch.StartNew();
            string? answer = await agent.AskAsync(new(prompt, CultureInfo.GetCultureInfo(culture), "Synthetic tester") { TurnId = Guid.NewGuid() }, token);
            measurements.Add(new { culture, nativeTool = name, elapsedMs = watch.Elapsed.TotalMilliseconds, nativeCalls = tools.Calls - before,
                failure = agent.LastFailure?.Kind.ToString(), valueVerified = answer?.Contains("731", StringComparison.Ordinal) == true, inference.LastMetrics, protocolShapes = observer.Shapes.ToArray() });
            await File.WriteAllTextAsync(Path.Combine(directory, "local-tool-attempt.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }), token);
            if (answer?.Contains("731", StringComparison.Ordinal) != true || tools.Calls != before + 1 || leaked != 0)
            { throw new InvalidOperationException("Real local tool selection, observation or private-stream isolation failed."); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "local-tool-verified.json"), JsonSerializer.Serialize(new
        { inference.Backend, inference.OffloadedLayers, embeddedToolTemplateVerified = true, privateStreamIsolationVerified = leaked == 0, syntheticOnly = true, measurements }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine("Pinned local EN/PT tool selection and actual synthetic observations verified without cloud, capture or desktop effects.");
    }

    /// <summary>Records only closed structural properties of synthetic generations, excluding response and argument bodies.</summary>
    private sealed class ToolInferenceObserver(LlamaLocalInferenceEngine inner) : ILocalToolInferenceEngine
    {
        internal List<object> Shapes { get; } = [];
        /// <inheritdoc />
        public bool IsAvailable => inner.IsAvailable;
        /// <inheritdoc />
        public string Status => inner.Status;
        /// <inheritdoc />
        public string? LoadedModel => inner.LoadedModel;
        /// <inheritdoc />
        public ConversationFailure? LastFailure => inner.LastFailure;
        /// <inheritdoc />
        public event EventHandler<string>? TokenReceived { add => inner.TokenReceived += value; remove => inner.TokenReceived -= value; }
        /// <inheritdoc />
        public async Task<string?> AskWithToolsAsync(ConversationTurn turn, LocalToolPrompt instructions, IReadOnlyList<ConversationMessage> exchange, CancellationToken cancellationToken)
        {
            string? output = await inner.AskWithToolsAsync(turn, instructions, exchange, cancellationToken);
            Shapes.Add(new { startsCall = output?.TrimStart().StartsWith("<tool_call>", StringComparison.Ordinal),
                endsCall = output?.TrimEnd().EndsWith("</tool_call>", StringComparison.Ordinal),
                discovery = output?.Contains("<function=describe_tool>", StringComparison.Ordinal),
                sensor = output?.Contains("<function=read_sensor>", StringComparison.Ordinal),
                parameterNewline = output?.Contains("<parameter=name>\n", StringComparison.Ordinal),
                closingNewline = output?.Contains("\n</parameter>", StringComparison.Ordinal),
                jsonCall = output?.Contains("\"arguments\"", StringComparison.Ordinal),
                codeFence = output?.Contains("```", StringComparison.Ordinal), length = output?.Length,
                syntheticFunctionHeader = FunctionHeader(output) });
            return output;
        }
        /// <summary>Captures only the bounded synthetic fixture function header to diagnose protocol drift, never argument or response bodies.</summary>
        private static string? FunctionHeader(string? output)
        {
            int start = output?.IndexOf("<function=", StringComparison.Ordinal) ?? -1;
            if (start < 0) { return null; } int end = output!.IndexOf('>', start);
            return end > start && end - start <= 80 ? output[start..(end + 1)] : "invalid_header";
        }
        /// <inheritdoc />
        public Task<string?> AskAsync(ConversationTurn turn, CancellationToken cancellationToken = default) => inner.AskAsync(turn, cancellationToken);
        /// <inheritdoc />
        public Task LoadAsync(string id, CancellationToken cancellationToken) => inner.LoadAsync(id, cancellationToken);
        /// <inheritdoc />
        public Task UnloadAsync(CancellationToken cancellationToken) => inner.UnloadAsync(cancellationToken);
        /// <inheritdoc />
        public void Cancel() => inner.Cancel();
        /// <inheritdoc />
        public Task ResetSessionAsync() => inner.ResetSessionAsync();
    }

    /// <summary>Publishes one synthetic read-only function so the fixture cannot affect user data or applications.</summary>
    private sealed class SyntheticTools : IAssistantTools
    {
        internal int Calls;
        internal string ExpectedName { get; set; } = "";
        internal JsonElement ExpectedArguments { get; set; }
        /// <inheritdoc />
        public object[] Declarations { get; } = [new { functionDeclarations = new object[] {
            new { name = "read_sensor", description = "Read the actual synthetic sensor value by name.",
                parameters = new { type = "OBJECT", properties = new { name = new { type = "STRING", @enum = new[] { "alpha", "beta" } } }, required = new[] { "name" } } },
            new { name = "inspect_payload", description = "Inspect a synthetic structured payload and return its actual fixture result.",
                parameters = new { type = "OBJECT", properties = new { payload = new { type = "OBJECT", properties = new {
                    count = new { type = "INTEGER", minimum = 1, maximum = 4 }, enabled = new { type = "BOOLEAN" },
                    items = new { type = "ARRAY", items = new { type = "INTEGER" } } }, required = new[] { "count", "enabled", "items" } } }, required = new[] { "payload" } } },
            new { name = "inspect_literal", description = "Inspect exact literal code or markup as inert text and return its actual fixture result.",
                parameters = new { type = "OBJECT", properties = new { text = new { type = "STRING" } }, required = new[] { "text" } } },
        } }];
        /// <inheritdoc />
        public bool IsRegisteredTool(string name) => name is "read_sensor" or "inspect_payload" or "inspect_literal";
        /// <inheritdoc />
        public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (name != ExpectedName || !JsonElement.DeepEquals(arguments, ExpectedArguments) || ProviderExecutionScope.Destination != "")
            { throw new InvalidOperationException("Unexpected synthetic adapter call or disclosure recipient."); }
            Calls++; return Task.FromResult(new AssistantToolResult("Observed sensor value: 731."));
        }
    }
}
