using Laura.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Configuration;

/// <summary>Checks independent provider opt-ins, task limits and secret-free INI persistence.</summary>
public sealed class ChatGptSettingsTests
{
    /// <summary>A fresh installation selects local-first routing and grants no cloud disclosure or spending permission.</summary>
    [Fact]
    public void DefaultsDoNotGrantChatGptAccess()
    {
        var settings = LauraSettings.Default.Sanitized(); Assert.False(settings.ChatGpt.Enabled); Assert.False(settings.ChatGpt.ShareContext);
        Assert.False(settings.ChatGpt.AllowAutomatic); Assert.False(settings.ChatGpt.IncludedPlanOnly); Assert.Empty(settings.ChatGpt.Model); Assert.Empty(settings.ChatGpt.MaxModel);
        Assert.Equal(ConversationProviderSelection.Automatic, settings.ProviderSelection);
    }
    /// <summary>Invalid disk values cannot remove cumulative limits or fabricate a valid model identity.</summary>
    [Fact]
    public void SanitizesIndependentTaskBudgets()
    {
        var settings = (LauraSettings.Default with { ChatGpt = new() { Model = "../../private", Enabled = true }, ProviderSelection = (ConversationProviderSelection)123,
            CognitiveTasks = new() { MaximumCycles = int.MaxValue, MaximumProviderCalls = -1, MaximumRecoveries = -1, ActiveMinutes = -3, IdleMinutes = int.MaxValue } }).Sanitized();
        Assert.Empty(settings.ChatGpt.Model); Assert.Equal(ConversationProviderSelection.Automatic, settings.ProviderSelection);
        Assert.Equal(128, settings.CognitiveTasks.MaximumCycles); Assert.Equal(1, settings.CognitiveTasks.MaximumProviderCalls);
        Assert.Equal(0, settings.CognitiveTasks.MaximumRecoveries); Assert.Equal(1, settings.CognitiveTasks.ActiveMinutes); Assert.Equal(10080, settings.CognitiveTasks.IdleMinutes);
    }
    /// <summary>Round-trips only explicit configuration in BOM UTF-8; OAuth credentials have no INI representation.</summary>
    [Fact]
    public async Task PersistsProviderAndTaskOptionsIndependently()
    {
        string directory = Path.Combine(Path.GetTempPath(), "laura-chatgpt-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "settings.ini"); var store = new IniSettingsStore(path, NullLogger<IniSettingsStore>.Instance);
            var expected = LauraSettings.Default with { GenerativeAi = new() { Enabled = true }, ProviderSelection = ConversationProviderSelection.Automatic,
                ChatGpt = new() { Enabled = true, Model = "account-discovered-model", AllowMax = true, MaxModel = "verified-max-model", ShareContext = true, AllowAutomatic = true, IncludedPlanOnly = true },
                CognitiveTasks = new() { MaximumCycles = 7, MaximumProviderCalls = 12, MaximumRecoveries = 3, MaximumToolCalls = 90, ActiveMinutes = 15, IdleMinutes = 200, MaximumResultMegabytes = 4 } };
            await store.SaveAsync(expected); var actual = await store.LoadAsync();
            Assert.Equal(expected.ChatGpt, actual.ChatGpt); Assert.Equal(expected.CognitiveTasks, actual.CognitiveTasks); Assert.True(actual.GenerativeAi.Enabled);
            Assert.Equal(expected.ProviderSelection, actual.ProviderSelection);
            byte[] bytes = await File.ReadAllBytesAsync(path); Assert.Equal(new byte[] { 239, 187, 191 }, bytes[..3]);
            string text = await File.ReadAllTextAsync(path); Assert.DoesNotContain("access_token", text, StringComparison.Ordinal); Assert.DoesNotContain("refresh_token", text, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
