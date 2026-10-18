using System.Text;
using Laura.Core.ChatGpt;

namespace Laura.Core.Tests.Conversation;

/// <summary>Checks actual SSE terminal semantics using synthetic streams, never a real account.</summary>
public sealed class ChatGptResponseTests
{
    /// <summary>Accepts only completed status and preserves actual nonnegative provider usage.</summary>
    [Fact]
    public async Task WaitsForCompletedEventAndReadsOfficialUsage()
    {
        using var stream = Stream(": heartbeat\n\nevent: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"hello\"}\n\n"
            + "data: {\"type\":\"response.completed\",\n" + "data: \"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":12,\"output_tokens\":4}}}\n\n");
        Assert.Equal(new ChatGptProbeResult("synthetic-model", true, 12, 4), await ChatGptResponseParser.ReadProbeAsync(stream, "synthetic-model", CancellationToken.None));
    }
    /// <summary>Never invents missing consumption metrics.</summary>
    [Fact]
    public async Task MissingUsageIsUnavailable()
    {
        using var stream = Stream("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n");
        var result = await ChatGptResponseParser.ReadProbeAsync(stream, "synthetic", CancellationToken.None);
        Assert.Null(result.InputTokens); Assert.Null(result.OutputTokens);
    }
    /// <summary>Collects authoritative completed output items when the final event omits the output array.</summary>
    [Fact]
    public async Task RetainsCompletedToolItemsAcrossStreamingEvents()
    {
        using var stream = Stream("data: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"name\":\"probe\",\"call_id\":\"synthetic\",\"arguments\":\"{}\"}}\n\n"
            + "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n");
        var result = await ChatGptResponseParser.ReadAsync(stream, null, CancellationToken.None);
        Assert.Equal("probe", Assert.Single(result.Output).GetProperty("name").GetString());
    }
    /// <summary>Rejects partial output, premature EOF, error terminals and inconsistent completion status.</summary>
    [Theory]
    [InlineData("data: {\"type\":\"response.output_text.delta\",\"delta\":\"hello\"}\n\n")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {\"type\":\"response.failed\"}\n\n")]
    [InlineData("data: {\"type\":\"response.incomplete\"}\n\n")]
    [InlineData("data: {\"type\":\"error\"}\n\n")]
    [InlineData("data: {\"type\":\"response.completed\",\"response\":{\"status\":\"incomplete\"}}\n\n")]
    public async Task PartialOutputNeverVerifiesInference(string sse)
    {
        using var stream = Stream(sse);
        await Assert.ThrowsAsync<InvalidDataException>(() => ChatGptResponseParser.ReadProbeAsync(stream, "synthetic", CancellationToken.None));
    }
    /// <summary>Cancellation stops a probe instead of accepting an incomplete answer.</summary>
    [Fact]
    public async Task HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); using var stream = Stream("");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChatGptResponseParser.ReadProbeAsync(stream, "synthetic", cancellation.Token));
    }
    /// <summary>Encodes synthetic SSE with normal UTF-8 framing.</summary>
    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
