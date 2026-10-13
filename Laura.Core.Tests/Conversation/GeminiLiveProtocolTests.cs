using System.Globalization;
using System.Text.Json;
using Laura.Core.Configuration;
using Laura.Core.Conversation;

namespace Laura.Core.Tests.Conversation;

public sealed class GeminiLiveProtocolTests
{
    [Fact]
    public void Setup_UsesNativeAudioVoiceAndExplicitMicrophoneActivity()
    {
        using JsonDocument document = JsonDocument.Parse(GeminiLiveProtocol.Setup(GeminiLiveOptions.Model, "Kore", "You are Laura", "pt-BR", [], true));
        JsonElement setup = document.RootElement.GetProperty("setup");
        Assert.Equal("models/" + GeminiLiveOptions.Model, setup.GetProperty("model").GetString());
        Assert.Equal("AUDIO", setup.GetProperty("generationConfig").GetProperty("responseModalities")[0].GetString());
        Assert.Equal("Kore", setup.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString());
        Assert.True(setup.GetProperty("realtimeInputConfig").GetProperty("automaticActivityDetection").GetProperty("disabled").GetBoolean());
        Assert.True(setup.TryGetProperty("outputAudioTranscription", out _));
        Assert.True(setup.TryGetProperty("inputAudioTranscription", out _));
    }
    [Fact]
    public void Audio_PreservesPcmBytesAndUsesCurrentRealtimeField()
    {
        byte[] pcm = [0, 255, 16, 32];
        using JsonDocument document = JsonDocument.Parse(GeminiLiveProtocol.Audio(pcm));
        JsonElement audio = document.RootElement.GetProperty("realtimeInput").GetProperty("audio");
        Assert.Equal("audio/pcm;rate=16000", audio.GetProperty("mimeType").GetString());
        Assert.Equal(pcm, Convert.FromBase64String(audio.GetProperty("data").GetString()!));
        Assert.False(document.RootElement.GetProperty("realtimeInput").TryGetProperty("mediaChunks", out _));
    }
    [Fact]
    public void Text_ReplaysBoundedHistoryOnlyAfterReconnectAndRetainsRoles()
    {
        var history = Enumerable.Range(0, 12).Select(index => new ConversationMessage(index % 2 == 0 ? ConversationMessageSource.UserTyped : ConversationMessageSource.Assistant, index.ToString(CultureInfo.InvariantCulture), DateTimeOffset.Now)).ToArray();
        var turn = new ConversationTurn("next", CultureInfo.GetCultureInfo("en-US"), "Tester") { History = history };
        using JsonDocument reconnected = JsonDocument.Parse(GeminiLiveProtocol.Text(turn, true));
        JsonElement turns = reconnected.RootElement.GetProperty("clientContent").GetProperty("turns");
        Assert.Equal(9, turns.GetArrayLength()); Assert.Equal("user", turns[0].GetProperty("role").GetString()); Assert.Equal("model", turns[1].GetProperty("role").GetString());
        Assert.Equal("next", turns[8].GetProperty("parts")[0].GetProperty("text").GetString());
        using JsonDocument active = JsonDocument.Parse(GeminiLiveProtocol.Text(turn, false));
        Assert.Single(active.RootElement.GetProperty("clientContent").GetProperty("turns").EnumerateArray());
    }
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void VisualFrameAndQuestionShareOneUserTurnAndHistoryCanAvoidTriggeringGeneration(string mimeType)
    {
        byte[] frame = [1, 2, 3, 4]; var turn = new ConversationTurn("Read the image", CultureInfo.GetCultureInfo("en-US"), "Synthetic") { Image = frame, ImageMimeType = mimeType };
        using var packet = JsonDocument.Parse(GeminiLiveProtocol.Text(turn, false)); var input = packet.RootElement.GetProperty("clientContent").GetProperty("turns")[0].GetProperty("parts");
        Assert.Equal(frame, Convert.FromBase64String(input[1].GetProperty("inlineData").GetProperty("data").GetString()!));
        Assert.Equal(mimeType, input[1].GetProperty("inlineData").GetProperty("mimeType").GetString()); Assert.Contains("Read the image", input[0].GetProperty("text").GetString());
        using var context = JsonDocument.Parse(GeminiLiveProtocol.Text(turn, false, false)); Assert.False(context.RootElement.GetProperty("clientContent").GetProperty("turnComplete").GetBoolean());
    }
    [Theory]
    [InlineData("Charon")]
    [InlineData("Puck")]
    [InlineData("Kore")]
    [InlineData("Fenrir")]
    [InlineData("Aoede")]
    public void Sanitization_PreservesGeminiAndReferenceVoices(string voice)
    {
        GenerativeAiOptions options = new GenerativeAiOptions { Provider = " Gemini ", Model = GeminiLiveOptions.Model, LiveVoice = voice }.Sanitized();
        Assert.Equal("gemini", options.Provider); Assert.Equal(voice, options.LiveVoice); Assert.Equal(GeminiLiveOptions.Model, options.Model);
    }
    [Fact]
    public void ToolOutcomeIsBoundToItsFunctionIdentityWithoutAnUnrelatedRealtimePacket()
    {
        using var packet = JsonDocument.Parse(GeminiLiveProtocol.ToolResult("call-123", "read_brain_image", "Actual image"));
        var result = packet.RootElement.GetProperty("toolResponse").GetProperty("functionResponses")[0];
        Assert.Equal("call-123", result.GetProperty("id").GetString()); Assert.Equal("read_brain_image", result.GetProperty("name").GetString());
        Assert.Equal("Actual image", result.GetProperty("response").GetProperty("result").GetString());
        Assert.False(packet.RootElement.TryGetProperty("realtimeInput", out _));
        using var text = JsonDocument.Parse(GeminiLiveProtocol.ToolResult("call-124", "file_control", "Denied"));
        Assert.False(text.RootElement.GetProperty("toolResponse").GetProperty("functionResponses")[0].TryGetProperty("parts", out _));
    }
    [Fact]
    public void Sanitization_RejectsUnknownLiveVoiceAndUnsafeModel()
    {
        GenerativeAiOptions options = new GenerativeAiOptions { Provider = "gemini", Model = "../key", LiveVoice = "unknown" }.Sanitized();
        Assert.Equal("Aoede", options.LiveVoice); Assert.Empty(options.Model);
    }

    [Fact]
    public void Sanitization_MigratesLocalProvidersWithoutReusingLocalModelNames()
    {
        var options = new GenerativeAiOptions { Provider = "ollama", Model = "qwen2.5:0.5b", ContinuousListening = false }.Sanitized();
        Assert.Equal("gemini", options.Provider); Assert.Empty(options.Model); Assert.True(options.ContinuousListening);
    }
}
