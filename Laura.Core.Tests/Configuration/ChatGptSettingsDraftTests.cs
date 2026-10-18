using Laura.Core.ChatGpt;
using Laura.Core.Configuration;

namespace Laura.Core.Tests.Configuration;

/// <summary>Prevents transient connection reads or asynchronous UI loading from destroying account-scoped choices.</summary>
public sealed class ChatGptSettingsDraftTests
{
    /// <summary>A cold view and temporary broker outages retain the exact saved registration and consent.</summary>
    [Fact]
    public void MissingBrokerMetadataIsNotLogout()
    {
        var saved = new ChatGptOptions { Registration = Guid.NewGuid(), Enabled = true, IncludedPlanOnly = true, ShareContext = true, AllowAutomatic = true, Model = "discovered-owner-model" };
        Assert.Same(saved, ChatGptSettingsDraft.Reconcile(saved, new(null, [], "disconnected", "broker_required")));
        Assert.Same(saved, ChatGptSettingsDraft.Reconcile(saved, new(saved.Registration, [], "plan_authorized")));
    }

    /// <summary>Owner logout is explicit, and a newly selected account cannot inherit another account's disclosure or billing choices.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewAccountAndExplicitDisconnectClearConsent(bool disconnected)
    {
        var saved = new ChatGptOptions { Registration = Guid.NewGuid(), Enabled = true, IncludedPlanOnly = true, ShareContext = true, AllowAutomatic = true, Model = "discovered-owner-model" };
        Guid other = Guid.NewGuid();
        var result = ChatGptSettingsDraft.Reconcile(saved, new(other, [], "plan_authorized"), disconnected);
        Assert.Equal(disconnected ? null : (Guid?)other, result.Registration);
        Assert.False(result.Enabled); Assert.False(result.IncludedPlanOnly); Assert.False(result.ShareContext); Assert.False(result.AllowAutomatic);
        Assert.Equal(saved.Model, result.Model);
    }
}
