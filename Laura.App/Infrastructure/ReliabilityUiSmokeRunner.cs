using System.Text.Json;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Configuration;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Exercises real native provider controls and guarded file tools in isolated storage with synthetic data.</summary>
internal static class ReliabilityUiSmokeRunner
{
    /// <summary>Validates saved credentials, ten provider switches, two approval modes and real file effects without cloud inference or microphone use.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        var settings = services.GetRequiredService<ISettingsService>();
        var keys = services.GetRequiredService<IApiKeyStore>();
        keys.Save("synthetic-key-not-valid-for-inference");
        await settings.UpdateAsync(current => current with { GenerativeAi = current.GenerativeAi with { Enabled = true, LiveVoice = "Aoede" },
            MicrophoneMuted = true, Permissions = current.Permissions with { Mode = ApprovalMode.Ask } }).ConfigureAwait(true);
        var main = services.GetRequiredService<MainPage>();
        using var capture = LauraDiagnostics.Capture();
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            await settings.UpdateAsync(current => current with { UiCulture = culture }).ConfigureAwait(true);
            services.GetRequiredService<IUiLocalizer>().SetCulture(settings.Current.ResolveUiCulture());
            for (int index = 0; index < 10; index++)
            {
                await settings.UpdateAsync(current => current with { ProviderSelection = index % 2 == 0 ? ConversationProviderSelection.Gemini : ConversationProviderSelection.ChatGpt }).ConfigureAwait(true);
                main.VerifyProviderFeedbackForSmoke();
            }
            foreach (int width in new[] { 1440, 720 })
            {
                window.Width = width; window.Height = width == 720 ? 750 : 1000;
                await Task.Delay(350).ConfigureAwait(true);
                await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, $"providers-{culture}-{width}.png")).ConfigureAwait(true);
            }
            await settings.UpdateAsync(current => current with { ProviderSelection = ConversationProviderSelection.Gemini }).ConfigureAwait(true);
            var navigation = (NavigationPage)window.Page!;
            var setup = services.GetRequiredService<SetupPage>(); await navigation.PushAsync(setup, false).ConfigureAwait(true);
            await setup.VerifyProviderIsolationForSmokeAsync().ConfigureAwait(true);
            setup.SelectTab(4); await Task.Delay(350).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, $"permissions-{culture}.png")).ConfigureAwait(true);
            await navigation.PopAsync(false).ConfigureAwait(true);
        }
        if (LauraDiagnostics.Snapshot().Any(sample => sample.Operation == "gemini.session.reset"))
        { throw new InvalidOperationException("A provider-only settings save reset the Gemini session."); }
        var memory = services.GetRequiredService<IConversationMemory>();
        memory.SetWorkingContext(new ConversationWorkingContext(Guid.NewGuid(), "Verify one synthetic scoped HTML correction and durable rollback."));
        var tools = services.GetRequiredService<IAssistantTools>();
        var permissions = services.GetRequiredService<PermissionManager>();
        string path = Path.Combine(directory, "Fixture", "site.html"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string source = "<style>.layout{display:grid}</style><h1>Configuracao</h1><footer>Preserve layout</footer>";
        await File.WriteAllTextAsync(path, source).ConfigureAwait(true);
        using var local = new ProviderExecutionScope("local");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        AssistantToolResult read = await ExecuteReviewedAsync(tools, permissions, new { action = "read", path }, timeout.Token).ConfigureAwait(true);
        using var snapshot = JsonDocument.Parse(read.Text);
        string hash = snapshot.RootElement.GetProperty("Hash").GetString()!;
        AssistantToolResult edit = await ExecuteReviewedAsync(tools, permissions, new { action = "patch", path, expected_hash = hash, old_text = "Configuracao", new_text = "Configuração" }, timeout.Token).ConfigureAwait(true);
        if (edit.Failed || await File.ReadAllTextAsync(path).ConfigureAwait(true) != source.Replace("Configuracao", "Configuração", StringComparison.Ordinal))
        { throw new InvalidOperationException("The native file tool did not preserve the exact scoped edit."); }
        string receipt = edit.Text[..edit.Text.IndexOf('\n')]; using var parsed = JsonDocument.Parse(receipt);
        Guid id = parsed.RootElement.GetProperty("Id").GetGuid();
        AssistantToolResult rollback = await ExecuteReviewedAsync(tools, permissions, new { action = "rollback", path, receipt = id }, timeout.Token).ConfigureAwait(true);
        if (rollback.Failed || await File.ReadAllTextAsync(path).ConfigureAwait(true) != source) { throw new InvalidOperationException("Native guarded rollback failed."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "PASS: actual EN/PT HUD and settings, twenty provider switches, protected Gemini credential preserved, no unrelated session reset, two approval modes, native reviewed patch and durable rollback. Synthetic data; no cloud or microphone. Rendered HTML browser acceptance is separate.").ConfigureAwait(true);
        services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown();
    }

    /// <summary>Supplies an explicit synthetic native decision only for this isolated diagnostic operation.</summary>
    private static async Task<AssistantToolResult> ExecuteReviewedAsync(IAssistantTools tools, PermissionManager permissions, object arguments, CancellationToken token)
    {
        Task<AssistantToolResult> result = tools.ExecuteAsync("file_control", JsonSerializer.SerializeToElement(arguments), token);
        while (!result.IsCompleted)
        {
            foreach (ApprovalRequest request in permissions.PendingRequests) { permissions.Decide(request.Id, ApprovalChoice.Once); }
            await Task.Delay(15, token).ConfigureAwait(true);
        }
        AssistantToolResult completed = await result.ConfigureAwait(true);
        if (completed.Failed) { throw new InvalidOperationException("Synthetic reviewed file action failed: " + completed.Status + "; " + completed.Text); }
        return completed;
    }
}
