using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>Regressions for provider-independent drafts and serialized persistence/publication.</summary>
public sealed class SettingsConcurrencyTests
{
    /// <summary>Concurrent field edits must transform the last committed state, including while a write is pending.</summary>
    [Fact]
    public async Task ConcurrentEdits_PreserveBothProvidersAndPublishPersistedOrder()
    {
        var store = new BlockingStore();
        using var service = new SettingsService(store, NullLogger<SettingsService>.Instance);
        var published = new List<LauraSettings>();
        service.Changed += (_, value) => { Assert.Equal(store.Saved, value); published.Add(value); };
        Task<LauraSettings> first = service.UpdateAsync(current => current with { GenerativeAi = current.GenerativeAi with { Enabled = true, LiveVoice = "Aoede" } });
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task<LauraSettings> second = service.UpdateAsync(current => current with { ChatGpt = current.ChatGpt with { Enabled = true }, ProviderSelection = ConversationProviderSelection.ChatGpt });
        store.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.True(service.Current.GenerativeAi.Enabled);
        Assert.True(service.Current.ChatGpt.Enabled);
        Assert.Equal("Aoede", service.Current.GenerativeAi.LiveVoice);
        Assert.Equal(2, published.Count);
        Assert.Equal(store.Saved, service.Current);
    }

    /// <summary>A failed persisted write must leave the prior in-memory value and event stream intact.</summary>
    [Fact]
    public async Task FailedWrite_DoesNotPublishCandidate()
    {
        var store = new BlockingStore { Fail = true }; store.Release.TrySetResult();
        using var service = new SettingsService(store, NullLogger<SettingsService>.Instance);
        int events = 0; service.Changed += (_, _) => events++;
        await Assert.ThrowsAsync<IOException>(() => service.UpdateAsync(current => current with { UserNickname = "Unsaved" }));
        Assert.Equal(LauraSettings.Default, service.Current);
        Assert.Equal(0, events);
    }

    /// <summary>A stale page changing only provider choice must retain external Gemini and Brain changes.</summary>
    [Fact]
    public void StaleDraft_ProviderSwitchesPreserveUneditedSettings()
    {
        LauraSettings baseline = LauraSettings.Default;
        LauraSettings current = baseline with { GenerativeAi = baseline.GenerativeAi with { Enabled = true, Model = "gemini-owner-choice", LiveVoice = "Aoede" },
            Brain = baseline.Brain with { Directory = "OwnerBrain" }, MicrophoneMuted = false };
        foreach (int index in Enumerable.Range(0, 10))
        {
            var edited = baseline with { ProviderSelection = index % 2 == 0 ? ConversationProviderSelection.ChatGpt : ConversationProviderSelection.Automatic };
            var merged = SettingsDraft.Merge(baseline, edited, current);
            Assert.Equal(current.GenerativeAi, merged.GenerativeAi);
            Assert.Equal(current.Brain, merged.Brain);
            Assert.False(merged.MicrophoneMuted);
            Assert.Equal(edited.ProviderSelection, merged.ProviderSelection);
        }
    }

    /// <summary>Controllable storage creates real contention and persistence failures without a mocking framework.</summary>
    private sealed class BlockingStore : ISettingsStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LauraSettings Saved { get; private set; } = LauraSettings.Default;
        public bool Fail { get; init; }
        /// <inheritdoc />
        public Task<LauraSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        /// <inheritdoc />
        public async Task SaveAsync(LauraSettings settings, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            if (Fail) { throw new IOException("Synthetic persistence failure."); }
            Saved = settings;
        }
    }
}
