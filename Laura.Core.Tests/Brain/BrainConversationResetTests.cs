using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Brain;

public sealed partial class BrainTests
{
    [Fact]
    public async Task ClearingTranscriptPreservesMarkdownAndStartsWithEmptyWorkingContextAcrossRestart()
    {
        BrainService brain = Create();
        await using var learning = new BrainLearning(brain, _settings, _localizer, NullLogger<BrainLearning>.Instance);
        var memory = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "reset.db")));
        await Task.Run(memory.Initialize);
        BrainNote saved = await brain.SaveAsync(Note("Knowledge/Keep.md", "Preserved knowledge", "Synthetic persistent knowledge for reset test."));
        memory.Append(new(ConversationMessageSource.Assistant, "Old dialogue", DateTimeOffset.UtcNow)); memory.SetWorkingContext("Old working context");
        memory.ClearHistory(); Assert.Empty(memory.ReadAllHistory()); Assert.Equal("", memory.WorkingUserText); Assert.Equal(Guid.Empty, memory.WorkingTurnId);
        Assert.Equal(saved.Hash, brain.Snapshot.Notes.Single(note => note.Id == saved.Id).Hash);
        Assert.Equal(saved.Hash, FileBrainStorage.Hash(await File.ReadAllTextAsync(Path.Combine(brain.Location, saved.Path))));
        var restarted = new BrainConversationMemory(brain, learning, new SqliteConversationMemory(Path.Combine(_root, "reset.db")));
        Assert.Empty(restarted.ReadAllHistory()); Assert.Equal("", restarted.WorkingUserText);
    }
}
