using System.Text;
using System.Text.Json;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Verifies actual DPAPI task persistence and native task UI without credentials, remote requests or microphone capture.</summary>
internal static class TaskRecoverySmokeRunner
{
    /// <summary>Exercises existing tool adapters and a private checkpoint through the isolated application's composition root.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        var memory = services.GetRequiredService<IConversationMemory>(); var tasks = services.GetRequiredService<CognitiveTasks>();
        var journal = services.GetRequiredService<ICognitiveTaskJournal>(); var tools = services.GetRequiredService<IAssistantTools>();
        Guid id = Guid.NewGuid(); const string objective = "Inspect the synthetic task recovery fixture and preserve actual partial progress.";
        memory.SetWorkingContext(new ConversationWorkingContext(id, objective));
        using var plan = JsonDocument.Parse("{\"tools\":\"[\\\"system_status\\\",\\\"recall_memory\\\"]\"}");
        if ((await tools.ExecuteAsync("plan_task", plan.RootElement, CancellationToken.None).ConfigureAwait(true)).Failed) { throw new InvalidOperationException("Native plan checkpoint failed."); }
        using var empty = JsonDocument.Parse("{}");
        if ((await tools.ExecuteAsync("system_status", empty.RootElement, CancellationToken.None).ConfigureAwait(true)).Failed) { throw new InvalidOperationException("Native observed read failed."); }
        var checkpoint = await journal.ReadAsync(id).ConfigureAwait(true) ?? throw new InvalidOperationException("Native checkpoint missing.");
        if (checkpoint.Calls != 2 || checkpoint.Steps[0].State != "completed" || checkpoint.HasUncertainEffect) { throw new InvalidOperationException("Native task plan or outcomes were not preserved."); }
        string path = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Tasks", id.ToString("N") + ".task");
        if (Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path).ConfigureAwait(true)).Contains(objective, StringComparison.Ordinal)) { throw new InvalidOperationException("Task objective remained plaintext on disk."); }
        var restored = await tasks.PrepareResumeAsync(id).ConfigureAwait(true);
        if (restored.Id != id || restored.Objective != objective || restored.Calls != 2) { throw new InvalidOperationException("Native recovery reset the objective or budget."); }
        var navigation = (NavigationPage)window.Page!;
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            services.GetRequiredService<IUiLocalizer>().SetCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
            var page = services.GetRequiredService<TasksPage>(); await navigation.PushAsync(page, false).ConfigureAwait(true);
            foreach (double width in new[] { 1440d, 720d })
            {
                window.Width = width; window.Height = width == 720 ? 600 : 900;
                await page.RefreshAsync().ConfigureAwait(true); await Task.Delay(350).ConfigureAwait(true); page.VerifyForSmoke();
                await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "tasks-" + culture + "-" + width + ".png")).ConfigureAwait(true);
            }
            await navigation.PopAsync(false).ConfigureAwait(true);
        }
        await tasks.DeleteAsync(id).ConfigureAwait(true);
        if (await journal.ReadAsync(id).ConfigureAwait(true) is not null) { throw new InvalidOperationException("Removing a checkpoint did not remove its private state."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), "Native tools preserved plan/outcomes in DPAPI-protected checkpoints; explicit recovery preserved task identity and budgets; EN/PT task workspace at 1440/720 and selected checkpoint deletion verified. No cloud, microphone or user project effects.").ConfigureAwait(true);
    }
}
