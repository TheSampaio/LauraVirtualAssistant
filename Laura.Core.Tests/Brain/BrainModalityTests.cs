using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Theory]
    [InlineData(ConversationMessageSource.UserTyped)]
    [InlineData(ConversationMessageSource.UserVoice)]
    public async Task BothUserModalitiesShareDurableLearningAndApplicationResolution(ConversationMessageSource source)
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "modality.db")));
        var launch = await SyntheticLaunchAsync("NeuralScreen");
        await Task.Run(() => memory.Append(new(source, "O Neural Screen está instalado em " + launch.Target, DateTimeOffset.UtcNow)));
        await learning.FlushAsync();
        using var apps = new BrainApplications(brain, _settings, new(_root), NullLogger<BrainApplications>.Instance);
        Assert.Equal(launch.Target, (await apps.ResolveAsync("Neural Screen")).Reference!.Target);
        var builder = new BrainContextBuilder(brain, _settings);
        Assert.Contains("Neural Screen", (await builder.BuildAsync("Onde está o Neural Screen?")).Memories);
        Assert.Equal(source, memory.ReadRecent(1).Single().Source);
    }
    [Fact]
    public async Task TranscriptFragmentsPreserveTheRealTurnAndANewUserTurnSupersedesIt()
    {
        BrainService brain = Create(); await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "partial.db")));
        memory.SetWorkingContext("O Neural"); Guid turn = memory.WorkingContext.TurnId;
        memory.UpdateWorkingContext("O Neural Screen está instalado");
        Assert.Equal(turn, memory.WorkingContext.TurnId); Assert.Contains("Screen", memory.WorkingUserText);
        memory.SetWorkingContext("Another actual request"); Assert.NotEqual(turn, memory.WorkingContext.TurnId);
        Assert.Empty(brain.Snapshot.Notes);
    }
}
