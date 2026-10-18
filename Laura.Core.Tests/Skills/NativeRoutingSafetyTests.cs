using System.Globalization;
using Laura.Core.Abstractions;
using Laura.Core.Engine;
using Laura.Core.Localization;
using Laura.Core.Permissions;
using Laura.Core.Skills;
using Laura.Core.Skills.Builtin;
using Laura.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laura.Core.Tests.Skills;

/// <summary>Checks command intent, permission enforcement and prevention of sequence replay through a model.</summary>
public sealed class NativeRoutingSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "laura-native-safety-" + Guid.NewGuid().ToString("N"));

    /// <summary>Provides the actual English command catalog for capability matching.</summary>
    private static JsonLocalizer Localizer() => new(Path.Combine(AppContext.BaseDirectory, "Localization", "Locales"), NullLogger<JsonLocalizer>.Instance);

    /// <summary>Preserves a trusted interaction identity through the native path.</summary>
    private static SkillRequest Request(string text) => SkillRequest.Create(text, CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Text) with { TurnId = Guid.NewGuid() };

    /// <summary>Descriptions, quotations and negations must not become direct native effects.</summary>
    [Theory]
    [InlineData("Do not lock the computer")]
    [InlineData("Explain how to lock the computer")]
    [InlineData("The command is lock the computer")]
    public void EmbeddedLockTextDoesNotMatch(string text)
    {
        using var manager = new PermissionManager(new(Path.Combine(_directory, "permissions.json")), () => new());
        var system = new RecordingSystemController();
        Assert.False(new LockWorkstationSkill(Localizer(), system, manager).CanHandle(Request(text)));
        Assert.Equal(0, system.LockRequests);
    }

    /// <summary>Native classification cannot grant system permissions, and OS refusal cannot be reported as success.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LockRequiresOwnerDecisionAndReportsActualAcceptance(bool approve, bool osAccepts)
    {
        using var manager = new PermissionManager(new(Path.Combine(_directory, "permissions.json")), () => new());
        var system = new RecordingSystemController { AcceptLock = osAccepts };
        var skill = new LockWorkstationSkill(Localizer(), system, manager);
        var request = Request("lock the computer");
        Assert.True(skill.CanHandle(request));
        var pending = skill.ExecuteAsync(request);
        Assert.Equal(0, system.LockRequests);
        var review = Assert.Single(manager.PendingRequests);
        Assert.Equal(request.TurnId, review.Action.TaskId);
        Assert.Equal(PermissionOperation.SystemSettings, review.Action.Operation);
        Assert.True(manager.Decide(review.Id, approve ? ApprovalChoice.Once : ApprovalChoice.Deny));
        if (!approve)
        {
            await Assert.ThrowsAsync<PermissionDeniedException>(() => pending);
            Assert.Equal(0, system.LockRequests);
        }
        else
        {
            var response = await pending;
            Assert.Equal(Localizer().Get(osAccepts ? LocalizationKeys.Skills.LockResponse : LocalizationKeys.Assistant.SkillFailed), response.SpokenText);
            Assert.Equal(1, system.LockRequests);
        }
    }

    /// <summary>A native step declining after a previous effect cannot resend the sequence to a cloud executor.</summary>
    [Fact]
    public async Task DeclinedSequenceStepDoesNotReplayEarlierEffects()
    {
        var first = new FakeSkill("first", 1, "first", SkillResponse.Speak("Confirmed"));
        var second = new FakeSkill("second", 2, "second", SkillResponse.NotHandled);
        var cloud = new RecordingConversationEngine(true, "Must not replay");
        var dispatcher = new SkillDispatcher([first, second], Localizer(), cloud, new StubSettingsService(Laura.Core.Configuration.LauraSettings.Default), new StubUserContext(), NullLogger<SkillDispatcher>.Instance);
        var request = Request("run steps: first; second");
        Assert.False(dispatcher.CanUseGenerativeFallback(request));
        Assert.True((await dispatcher.DispatchAsync(request)).Handled);
        Assert.Equal(1, first.ExecutionCount); Assert.Equal(1, second.ExecutionCount); Assert.Equal(0, cloud.CallCount);
    }

    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
}
