using System.Globalization;
using System.Text.Json;

// Independent, synthetic stdio fixture: never accesses a network, executes other programs or reads personal data.
bool legacy = args.Contains("--legacy", StringComparer.Ordinal);
await File.AppendAllTextAsync("starts.txt", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
string[] names = ["echo", "fail", "delay", "crash", "oversize", "malformed", "check_env"];
while (await Console.In.ReadLineAsync() is { } line)
{
    using JsonDocument request = JsonDocument.Parse(line);
    JsonElement root = request.RootElement;
    if (!root.TryGetProperty("id", out JsonElement id)) { continue; }
    string method = root.GetProperty("method").GetString()!;
    if (method == "server/discover")
    {
        if (legacy) { await ErrorAsync(id, -32601); continue; }
        await ResultAsync(id, new { resultType = "complete", supportedVersions = new[] { "2026-07-28" }, capabilities = new { tools = new { } },
            _meta = new Dictionary<string, object> { ["io.modelcontextprotocol/serverInfo"] = new { name = "LAURA synthetic fixture", version = "1.0" } } }); continue;
    }
    if (method == "initialize") { await ResultAsync(id, new { protocolVersion = "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "LAURA synthetic fixture", version = "1.0" } }); continue; }
    if (method == "ping") { await ResultAsync(id, new { resultType = "complete" }); continue; }
    if (!legacy && (!root.TryGetProperty("params", out JsonElement parameters) || !parameters.TryGetProperty("_meta", out JsonElement meta)
        || !meta.TryGetProperty("io.modelcontextprotocol/protocolVersion", out _) || !meta.TryGetProperty("io.modelcontextprotocol/clientCapabilities", out _)))
    { await ErrorAsync(id, -32602); continue; }
    if (method == "tools/list")
    {
        await ResultAsync(id, new { resultType = "complete", tools = names.Select(name => new { name, description = "Synthetic " + name + (File.Exists("schema-changed.txt") ? " changed actual version" : " original actual version"),
            inputSchema = new { type = "object", properties = new { message = new { type = "string" } }, additionalProperties = false }, annotations = new { readOnlyHint = true } }).ToArray() }); continue;
    }
    if (method != "tools/call") { await ErrorAsync(id, -32601); continue; }
    string tool = root.GetProperty("params").GetProperty("name").GetString()!;
    await File.AppendAllTextAsync("calls.txt", tool + "\n");
    if (tool == "crash") { return 17; }
    if (tool == "delay") { await Task.Delay(TimeSpan.FromMinutes(2)); }
    if (tool == "malformed") { await Console.Out.WriteLineAsync("not a JSON-RPC message"); await Console.Out.FlushAsync(); continue; }
    string text = tool switch { "echo" => "Synthetic echo: " + root.GetProperty("params").GetProperty("arguments").GetProperty("message").GetString(),
        "fail" => "Synthetic tool reports an actual controlled failure.", "check_env" => "Synthetic credential inherited: " + (Environment.GetEnvironmentVariable("LAURA_MCP_TEST_SECRET") is not null),
        "oversize" => new string('x', 600000), _ => "Synthetic completed result." };
    await ResultAsync(id, new { resultType = "complete", isError = tool == "fail", content = new[] { new { type = "text", text } } });
}
return 0;

static Task ResultAsync(JsonElement id, object result) => Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
static Task ErrorAsync(JsonElement id, int code) => Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message = "Synthetic unsupported request." } }));
