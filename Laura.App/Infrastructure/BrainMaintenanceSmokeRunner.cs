using System.Text.Json;
using Laura.App.Platforms.Windows;
using Laura.App.Shell;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Verifies actual MAUI review actions, native confirmations and subsequent Brain recovery using only isolated synthetic knowledge.</summary>
internal static class BrainMaintenanceSmokeRunner
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IServiceProvider services, string root)
    {
        var brain = services.GetRequiredService<BrainService>(); var maintenance = services.GetRequiredService<BrainMaintenance>(); var settings = services.GetRequiredService<ISettingsService>();
        var ui = services.GetRequiredService<IUiLocalizer>(); var tools = services.GetRequiredService<IAssistantTools>(); var permissions = services.GetRequiredService<PermissionManager>();
        const string body = "Synthetic shared knowledge sentinel_brain_maintenance_849. Source identity, references and history must remain recoverable.";
        BrainNote canonical = await brain.SaveAsync(new() { Path = "Knowledge/Canonical.md", Title = "Synthetic canonical source", Body = body }).ConfigureAwait(true);
        BrainNote duplicate = await brain.SaveAsync(new() { Path = "Knowledge/Duplicate.md", Title = "Synthetic repeated source", Body = body }).ConfigureAwait(true);
        BrainNote referring = await brain.SaveAsync(new() { Path = "Knowledge/Referrer.md", Title = "Synthetic referring source", Body = "Read [[id:" + duplicate.Id + "]] as the original authored reference." }).ConfigureAwait(true);
        var relation = new BrainRelation(canonical.Id, "supports", .6, .8, "Synthetic explicit supporting evidence", "manual");
        BrainNote repeatedRelations = await brain.SaveAsync(new() { Path = "Knowledge/Relations.md", Title = "Synthetic relation review", Body = "Preserve differently attributed relations while cleaning exact duplicates.", Relations = [relation, relation, relation with { Confidence = .7 }] }).ConfigureAwait(true);
        using var analyze = JsonDocument.Parse("{\"action\":\"analyze\"}");
        AssistantToolResult metadata = await tools.ExecuteAsync("inspect_brain_maintenance", analyze.RootElement, CancellationToken.None).ConfigureAwait(true);
        if (!metadata.Text.Contains("duplicate_content", StringComparison.Ordinal) || metadata.Text.Contains(body, StringComparison.Ordinal) || permissions.PendingRequests.Count != 0) { throw new InvalidOperationException("Maintenance adapter did not preserve bounded metadata-only analysis."); }
        using var forged = JsonDocument.Parse("{\"action\":\"apply\"}"); await tools.ExecuteAsync("inspect_brain_maintenance", forged.RootElement, CancellationToken.None).ConfigureAwait(true);
        if (brain.Snapshot.Notes.Single(note => note.Id == repeatedRelations.Id).Hash != repeatedRelations.Hash) { throw new InvalidOperationException("Model inspection could apply a maintenance edit."); }
        var window = Application.Current!.Windows[0]; var navigation = (NavigationPage)window.Page!; services.GetRequiredService<MauiShellController>().RevealForConfirmation();
        var page = new BrainMaintenancePage(brain, maintenance, settings, ui, _ => Task.CompletedTask);
        await NavigationMotion.PushAsync(navigation.Navigation, page).ConfigureAwait(true); await page.AnalyzeForSmokeAsync().ConfigureAwait(true); await Task.Delay(200).ConfigureAwait(true);
        window.Width = 1440; window.Height = 900; await Task.Delay(150).ConfigureAwait(true); await MauiSmokeRunner.CaptureAsync(window, Path.Combine(root, "maintenance-normal.png")).ConfigureAwait(true);
        window.Width = 720; window.Height = 600; await Task.Delay(150).ConfigureAwait(true); await MauiSmokeRunner.CaptureAsync(window, Path.Combine(root, "maintenance-compact.png")).ConfigureAwait(true);
        Invoke(Button("brain-maintenance-clean-")); await DecideDialogAsync(false).ConfigureAwait(true); await Task.Delay(100).ConfigureAwait(true);
        if (brain.Snapshot.Notes.Single(note => note.Id == repeatedRelations.Id).Hash != repeatedRelations.Hash) { throw new InvalidOperationException("Denied native relation cleanup changed knowledge."); }
        Invoke(Button("brain-maintenance-clean-")); await DecideDialogAsync(true).ConfigureAwait(true);
        await UntilAsync(() => brain.Snapshot.Notes.Single(note => note.Id == repeatedRelations.Id).Relations.Count == 2).ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true);
        Invoke(Button("brain-maintenance-duplicate-")); var choice = await WaitDialogAsync().ConfigureAwait(true);
        var form = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)choice.Content).Content;
        var picker = form.Children.OfType<Microsoft.UI.Xaml.Controls.ComboBox>().Single();
        picker.SelectedIndex = ((IEnumerable<string>)picker.ItemsSource).ToList().FindIndex(item => item.EndsWith(canonical.Path, StringComparison.Ordinal));
        Invoke(form.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().Last().Children.OfType<Microsoft.UI.Xaml.Controls.Button>().Last());
        await UntilAsync(() => WindowsDialogs.DialogForSmoke is { } current && current != choice).ConfigureAwait(true); await DecideDialogAsync(true).ConfigureAwait(true);
        await UntilAsync(() => brain.Snapshot.Notes.Single(note => note.Id == duplicate.Id).Status == "archived").ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true);
        if (brain.Snapshot.Notes.Single(note => note.Id == canonical.Id).Hash != canonical.Hash || brain.Snapshot.Notes.Single(note => note.Id == referring.Id).Hash != referring.Hash
            || BrainService.ResolveLink("id:" + duplicate.Id, referring.Path, brain.Snapshot.Notes) != duplicate.Id)
        { throw new InvalidOperationException("Native duplicate consolidation changed canonical data or broke authored identity references."); }
        using var recall = JsonDocument.Parse("{\"query\":\"sentinel_brain_maintenance_849\"}");
        string recovered = (await tools.ExecuteAsync("recall_memory", recall.RootElement, CancellationToken.None).ConfigureAwait(true)).Text;
        if (!recovered.Contains(canonical.Id, StringComparison.Ordinal) || !recovered.Contains("sentinel_brain_maintenance_849", StringComparison.Ordinal)) { throw new InvalidOperationException("Consolidated knowledge could not be recovered by the actual adapter."); }
        BrainNote third = await brain.SaveAsync(new() { Path = "Knowledge/Third.md", Title = "Synthetic cancellation source", Body = body }).ConfigureAwait(true);
        await page.AnalyzeForSmokeAsync().ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true);
        Invoke(Button("brain-maintenance-duplicate-")); await DecideDialogAsync(true).ConfigureAwait(true); await WaitDialogAsync().ConfigureAwait(true);
        await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true); await Task.Delay(200).ConfigureAwait(true);
        if (WindowsDialogs.DialogForSmoke is not null || brain.Snapshot.Notes.Single(note => note.Id == third.Id).Hash != third.Hash || !brain.Snapshot.Notes.Single(note => note.Id == canonical.Id).IsKnowledge)
        { throw new InvalidOperationException("Closing native maintenance did not cancel its outstanding confirmation."); }
        await settings.UpdateAsync(settings.Current with { RememberConversations = false }).ConfigureAwait(true);
        if ((await tools.ExecuteAsync("inspect_brain_maintenance", analyze.RootElement, CancellationToken.None).ConfigureAwait(true)).Text.Contains(canonical.Id, StringComparison.Ordinal)) { throw new InvalidOperationException("Disabled Brain recall disclosed maintenance metadata."); }
        if (permissions.PendingRequests.Count != 0 || permissions.Grants.Count != 0) { throw new InvalidOperationException("Maintenance granted execution access."); }
        return ["Actual maintenance adapter analyzed bounded metadata without full bodies; an unsupported model apply action did not edit anything.",
            "Actual MAUI/WinUI confirmation denied then accepted exact relation cleanup, preserving different evidence and authored history. Native duplicate review chose the canonical note and archived only the matching copy while retaining IDs, paths and references.",
            "Actual recall_memory recovered the consolidated canonical source. Closing the maintenance page cancelled its pending confirmation without archiving another note; disabling Brain recall withheld metadata. No execution grants were created."];

        Microsoft.UI.Xaml.Controls.Button Button(string prefix) => (Microsoft.UI.Xaml.Controls.Button)(Descendants(page).OfType<Microsoft.Maui.Controls.Button>().First(button => button.AutomationId?.StartsWith(prefix, StringComparison.Ordinal) == true).Handler?.PlatformView ?? throw new InvalidOperationException("Native maintenance action not realized."));
        static IEnumerable<IVisualTreeElement> Descendants(IVisualTreeElement element) { foreach (IVisualTreeElement child in element.GetVisualChildren()) { yield return child; foreach (var nested in Descendants(child)) { yield return nested; } } }
        static void Invoke(Microsoft.UI.Xaml.Controls.Button button) { var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button); ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke(); }
        static async Task UntilAsync(Func<bool> condition) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!condition()) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); } }
        static async Task<Microsoft.UI.Xaml.Window> WaitDialogAsync() { await UntilAsync(() => WindowsDialogs.DialogForSmoke is not null).ConfigureAwait(true); return WindowsDialogs.DialogForSmoke!; }
        static async Task DecideDialogAsync(bool accept)
        {
            var dialog = await WaitDialogAsync().ConfigureAwait(true); await Task.Delay(100).ConfigureAwait(true);
            var body = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)dialog.Content).Content;
            var buttons = body.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().Last().Children.OfType<Microsoft.UI.Xaml.Controls.Button>().ToArray(); Invoke(accept ? buttons.Last() : buttons.First());
        }
    }
}
