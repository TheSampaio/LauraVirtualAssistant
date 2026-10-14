using System.Globalization;
using System.Text.Json;
using Laura.App.Shell;
using Laura.App.Views;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Inspects actual native skill observations and reviewed sources, following UI links and historical versions without invoking a model or executing skill instructions.</summary>
internal static class BrainEvidenceSmokeRunner
{
    internal static async Task<IReadOnlyList<string>> RunAsync(IServiceProvider services, string root)
    {
        var brain = services.GetRequiredService<BrainService>(); var skills = services.GetRequiredService<BrainSkills>(); var usage = services.GetRequiredService<BrainSkillUsage>();
        var settings = services.GetRequiredService<ISettingsService>(); var ui = services.GetRequiredService<IUiLocalizer>(); var tools = services.GetRequiredService<IAssistantTools>();
        BrainNote native = skills.Find("Synthetic native diagnostic") ?? throw new InvalidOperationException("Native process procedure missing.");
        BrainNote blocked = skills.Find("Synthetic unavailable MCP review") ?? throw new InvalidOperationException("Blocked MCP procedure missing.");
        if (usage.Read(native.Id, native.Hash).Count != 2) { throw new InvalidOperationException("Inspector requires actual completed native observations."); }
        BrainNote source = brain.Snapshot.Notes.Single(note => note.Id == native.Procedure!.Sources[0].Id);
        var window = Application.Current!.Windows[0]; var navigation = (NavigationPage)window.Page!; services.GetRequiredService<MauiShellController>().RevealForConfirmation();
        string originalCulture = settings.Current.UiCulture;
        string checkpoints = Path.Combine(root, "evidence-checks.txt");
        await File.WriteAllTextAsync(checkpoints, "Actual native observations and source identities located.\n").ConfigureAwait(true);
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            await settings.UpdateAsync(settings.Current with { UiCulture = culture }).ConfigureAwait(true); ui.SetCulture(CultureInfo.GetCultureInfo(culture));
            MemoryPage page = services.GetRequiredService<MemoryPage>(); await NavigationMotion.PushAsync(navigation.Navigation, page).ConfigureAwait(true);
            window.Width = 1440; window.Height = 900; await page.PreviewEvidenceForSmokeAsync(native.Id).ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true);
            BrainEvidenceView inspector = Descendants(page).OfType<BrainEvidenceView>().Single();
            if (!Text(inspector, "brain-evidence-state").Contains(ui.Get("ui.evidence.available_instructions"), StringComparison.Ordinal)
                || !Text(inspector, "brain-evidence-outcomes").Contains(string.Format(ui.Culture, ui.Get("ui.evidence.outcomeSummary"), 2, 1, 1), StringComparison.Ordinal))
            { throw new InvalidOperationException("Inspector claimed the wrong readiness or native outcome count."); }
            Toggle(inspector, "brain-evidence-sources"); Toggle(inspector, "brain-evidence-outcomes");
            await Task.Delay(150).ConfigureAwait(true);
            Button sourceButton = Descendants(inspector).OfType<Button>().Single(button => button.Text == source.Title);
            await File.AppendAllTextAsync(checkpoints, culture + ": source action height=" + sourceButton.Height.ToString("F0", CultureInfo.InvariantCulture) + ".\n").ConfigureAwait(true);
            if (sourceButton.Height > 120 || sourceButton.Height < Theme.TargetSize) { throw new InvalidOperationException("Source action has an unusable native height."); }
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(root, "evidence-ready-" + culture + ".png")).ConfigureAwait(true);
            await File.AppendAllTextAsync(checkpoints, culture + ": ready inspector and progressive sections rendered.\n").ConfigureAwait(true);
            Invoke(sourceButton);
            await UntilAsync(() => page.SelectedIdentityForSmoke == source.Id).ConfigureAwait(true);
            await File.AppendAllTextAsync(checkpoints, culture + ": native source link opened the actual source.\n").ConfigureAwait(true);
            await page.PreviewEvidenceForSmokeAsync(native.Id).ConfigureAwait(true); inspector = Descendants(page).OfType<BrainEvidenceView>().Single(); Toggle(inspector, "brain-evidence-history");
            Button revision = Descendants(inspector).OfType<Button>().First(button => button.AutomationId?.StartsWith("brain-evidence-revision-", StringComparison.Ordinal) == true); Invoke(revision);
            await UntilAsync(() => page.SelectedIdentityForSmoke != native.Id).ConfigureAwait(true);
            if (brain.Snapshot.Notes.Single(note => note.Id == page.SelectedIdentityForSmoke).Status != "archived") { throw new InvalidOperationException("History action did not open an actual preserved revision."); }
            await File.AppendAllTextAsync(checkpoints, culture + ": native history link opened a preserved revision.\n").ConfigureAwait(true);
            window.Width = 720; window.Height = 600; await page.PreviewEvidenceForSmokeAsync(blocked.Id).ConfigureAwait(true); await Task.Delay(150).ConfigureAwait(true);
            inspector = Descendants(page).OfType<BrainEvidenceView>().Single(); Toggle(inspector, "brain-evidence-requirements");
            if (!Text(inspector, "brain-evidence-state").Contains(ui.Get("ui.evidence.blocked"), StringComparison.Ordinal)
                || !Descendants(inspector).OfType<Label>().Any(label => label.Text?.Contains("mcp.rea.inspect", StringComparison.Ordinal) == true)) { throw new InvalidOperationException("Unavailable MCP dependencies were not explained by the inspector."); }
            Button activation = Descendants(page).OfType<Button>().Single(button => button.Text == ui.Get("ui.brain.enableSkill"));
            if (activation.IsEnabled) { throw new InvalidOperationException("Unavailable procedure offered an activation that cannot succeed."); }
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(root, "evidence-blocked-compact-" + culture + ".png")).ConfigureAwait(true);
            await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true);
        }
        BrainNote failedEpisode = brain.Snapshot.Notes.Single(note => note.IsKnowledge && note.Type == "experience" && note.Body.Contains("Observed exit code: 1", StringComparison.Ordinal));
        var revisionProposal = new BrainSkillProposal(native.Title, native.Description, native.Procedure! with { Steps = ["Review the actual nonzero exit before proposing another approved attempt; do not invent its cause."],
            Sources = [new(failedEpisode.Id, failedEpisode.Hash, "Observed exit code: 1")] }, native.Id, native.Hash);
        JsonElement proposal = JsonSerializer.SerializeToElement(new { proposal = JsonSerializer.Serialize(revisionProposal) });
        await tools.ExecuteAsync("propose_brain_skill", proposal, CancellationToken.None).ConfigureAwait(true);
        BrainNote revised = skills.Find(native.Id)!; if (revised.Hash == native.Hash || revised.Status != "draft" || skills.IsEnabled(revised)) { throw new InvalidOperationException("Observed failure revision was self-activated."); }
        MemoryPage current = services.GetRequiredService<MemoryPage>(); await NavigationMotion.PushAsync(navigation.Navigation, current).ConfigureAwait(true); await current.PreviewEvidenceForSmokeAsync(revised.Id).ConfigureAwait(true);
        BrainEvidenceView changed = Descendants(current).OfType<BrainEvidenceView>().Single(); Toggle(changed, "brain-evidence-outcomes");
        if (!Text(changed, "brain-evidence-outcomes").Contains(ui.Get("ui.evidence.noOutcomes"), StringComparison.Ordinal)
            || !Text(changed, "brain-evidence-outcomes").Contains(ui.Get("ui.evidence.olderOutcomes"), StringComparison.Ordinal)) { throw new InvalidOperationException("Older native observations incorrectly validated a changed skill version."); }
        await MauiSmokeRunner.CaptureAsync(window, Path.Combine(root, "evidence-revision-unverified.png")).ConfigureAwait(true);
        await NavigationMotion.PopAsync(navigation.Navigation).ConfigureAwait(true);
        await settings.UpdateAsync(settings.Current with { UiCulture = originalCulture }).ConfigureAwait(true); ui.SetCulture(settings.Current.ResolveUiCulture());
        return ["Actual EN/PT inspector showed reviewed instructions, two real process outcomes (zero/nonzero), unavailable MCP dependencies and disabled impossible activation. Native source/history buttons opened actual source and archived revision identities.",
            "Actual propose_brain_skill revised the same identity using the real observed failure as literal evidence. The new version stayed draft/unapproved, and the UI separated its lack of observations from two retained earlier-version outcomes. Normal and compact native captures were produced without cloud or microphone access."];

        static string Text(IVisualTreeElement root, string id) => Descendants(root).OfType<Label>().Single(label => label.AutomationId == id).Text;
        static void Toggle(IVisualTreeElement root, string id) => Invoke(Descendants(root).OfType<Button>().Single(button => button.AutomationId == id));
        static void Invoke(Button button) { var nativeButton = (Microsoft.UI.Xaml.Controls.Button)button.Handler!.PlatformView!; var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(nativeButton); ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke(); }
        static IEnumerable<IVisualTreeElement> Descendants(IVisualTreeElement root) { foreach (var child in root.GetVisualChildren()) { yield return child; foreach (var nested in Descendants(child)) { yield return nested; } } }
        static async Task UntilAsync(Func<bool> ready) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!ready()) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); } }
    }
}
