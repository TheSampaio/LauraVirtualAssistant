using System.Globalization;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Permissions;
using Laura.Core.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Exercises the real local provider and full native Brain/tool composition using explicit isolated storage, without speech or microphone capture.</summary>
internal static class LocalRuntimeUiSmokeRunner
{
    /// <summary>Requires a real successful brain_stats receipt and a matching numeric reply from the actual app dispatcher.</summary>
    internal static async Task RunAsync(IServiceProvider services, string directory)
    {
        var settings = services.GetRequiredService<ISettingsService>();
        if (!settings.Current.LocalModel.Enabled || settings.Current.GenerativeAi.Enabled || settings.Current.ChatGpt.Enabled
            || settings.Current.ProviderSelection != Laura.Core.Configuration.ConversationProviderSelection.Local)
        { throw new InvalidOperationException("Local acceptance requires installed local inference and disabled cloud in an isolated profile."); }
        await settings.UpdateAsync(settings.Current with { MicrophoneMuted = true, GenerativeAi = settings.Current.GenerativeAi with { Timeout = TimeSpan.FromMinutes(5) } }).ConfigureAwait(true);
        var permissions = services.GetRequiredService<PermissionManager>();
        void DenyEffects(object? sender, EventArgs args)
        { foreach (var request in permissions.PendingRequests) { permissions.Decide(request.Id, ApprovalChoice.Deny); } }
        permissions.Changed += DenyEffects;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(9));
            const string prompt = "Call brain_stats once to read the actual Brain node count, then answer in one short English sentence containing that count as a numeral. Do not plan, change files, use the Internet or run another native tool. This is an isolated offline acceptance check.";
            Guid id = Guid.NewGuid(); var request = SkillRequest.Create(prompt, CultureInfo.GetCultureInfo("en-US"), DateTimeOffset.UtcNow, SkillRequestSource.Text) with { TurnId = id };
            var dispatcher = services.GetRequiredService<ISkillDispatcher>();
            SkillResponse response = await dispatcher.DispatchAsync(request, timeout.Token).ConfigureAwait(true);
            var router = services.GetRequiredService<ConversationProviderRouter>();
            var checkpoint = services.GetRequiredService<CognitiveTasks>().Current(id);
            if (router.LastFailure is not null || router.ActiveProvider != "local" || string.IsNullOrWhiteSpace(response.SpokenText)
                || checkpoint?.Operations.Any(operation => operation.Tool == "brain_stats" && operation.Status == "success") != true)
            { throw new InvalidOperationException("The real application did not confirm local native Brain execution: " + router.LastFailure?.Kind); }
            int nodes = services.GetRequiredService<BrainService>().Snapshot.Notes.Count;
            if (!System.Text.RegularExpressions.Regex.IsMatch(response.SpokenText, @"(?<!\d)" + nodes.ToString(CultureInfo.InvariantCulture) + @"(?!\d)", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            { throw new InvalidOperationException("The local app answer did not match the measured synthetic Brain inventory: " + response.SpokenText); }
            await File.WriteAllTextAsync(Path.Combine(directory, "local-application.json"), JsonSerializer.Serialize(new { provider = router.ActiveProvider, nodes,
                answer = response.SpokenText, tools = checkpoint.Operations.Select(operation => new { operation.Tool, operation.Status }), cloudDisabled = true, microphoneOpened = false }), timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: actual app dispatcher, full native tool catalogue, local Qwen, task receipts and Brain inventory agreed. Cloud disabled; no microphone, TTS playback or personal storage.", timeout.Token).ConfigureAwait(true);
        }
        finally { permissions.Changed -= DenyEffects; services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
}
