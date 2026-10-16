using System.Text;
using System.Text.Json;
using Laura.App.Views;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Engine;
using Laura.Core.Brain;
using Laura.Core.Permissions;
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
        if (Environment.GetCommandLineArgs().Contains("--repository-study-smoke", StringComparer.Ordinal))
        { await VerifyRepositoryStudyAsync(services, directory).ConfigureAwait(true); }
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

    /// <summary>Acquires synthetic local sources through actual declarations, one exact review and resumable bounded cursors.</summary>
    private static async Task VerifyRepositoryStudyAsync(IServiceProvider services, string directory)
    {
        string root = Path.Combine(services.GetRequiredService<AssistantStorage>().Directory, "Workspace", "StudyFixture");
        Directory.CreateDirectory(root);
        for (int file = 0; file < 75; file++) { await File.WriteAllTextAsync(Path.Combine(root, "Source" + file.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".cs"), "// Synthetic repository evidence; never execute.\n").ConfigureAwait(true); }
        var memory = services.GetRequiredService<IConversationMemory>(); Guid taskId = Guid.NewGuid();
        memory.SetWorkingContext(new ConversationWorkingContext(taskId, "Study the synthetic repository incrementally without executing its code."));
        var tools = services.GetRequiredService<IAssistantTools>(); var permissions = services.GetRequiredService<PermissionManager>();
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new { source = root, topic = "Synthetic repository study" }));
        Task<AssistantToolResult> pending = tools.ExecuteAsync("start_repository_study", arguments.RootElement, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (permissions.PendingRequests.Count == 0 && !pending.IsCompleted) { await Task.Delay(10, timeout.Token).ConfigureAwait(true); }
        var review = permissions.PendingRequests.Single();
        if (review.Action.Operation != PermissionOperation.RepositoryStudyAuthorization || review.Action.ScopeIdentity != root || review.Action.TaskId != taskId || review.Action.Destination.Length != 0)
        { throw new InvalidOperationException("Synthetic study did not produce its exact native local-only review."); }
        permissions.Decide(review.Id, ApprovalChoice.Once); var result = await pending.ConfigureAwait(true);
        using var inventory = JsonDocument.Parse(result.Text); Guid id = inventory.RootElement.GetProperty("Id").GetGuid(); string cursor = inventory.RootElement.GetProperty("Cursor").GetString()!;
        long previous = System.Diagnostics.Stopwatch.GetTimestamp(); double maximumGap = 0; int ticks = 0;
        var timer = Application.Current!.Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(20);
        timer.Tick += (_, _) => { long now = System.Diagnostics.Stopwatch.GetTimestamp(); maximumGap = Math.Max(maximumGap, System.Diagnostics.Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now; ticks++; }; timer.Start();
        try
        {
            foreach (int expected in new[] { 64, 75 })
            {
                using var next = JsonDocument.Parse(JsonSerializer.Serialize(new { id, cursor }));
                result = await tools.ExecuteAsync("continue_repository_study", next.RootElement, timeout.Token).ConfigureAwait(true);
                using var batch = JsonDocument.Parse(result.Text);
                if (result.Failed || batch.RootElement.GetProperty("AcquiredFiles").GetInt32() != expected || permissions.PendingRequests.Count != 0)
                { throw new InvalidOperationException("Native repository batching, coverage or scope reuse failed."); }
                cursor = batch.RootElement.GetProperty("Cursor").GetString()!;
            }
        }
        finally { timer.Stop(); }
        var progress = await services.GetRequiredService<RepositoryStudy>().ReadPrivateProgressAsync(id).ConfigureAwait(true);
        if (progress.PendingFiles != 0 || progress.AcquiredFiles != 75 || !services.GetRequiredService<CognitiveTasks>().Current(taskId)!.RepositoryStudies!.Contains(id))
        { throw new InvalidOperationException("Repository progress or its task checkpoint identity was not preserved."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "repository-study.json"), JsonSerializer.Serialize(new { progress.Id, progress.InventoryFiles, progress.AcquiredFiles, progress.PendingFiles, progress.State,
            dispatcherTicks = ticks, maximumDispatcherGapMs = maximumGap, cloud = false, microphone = false, sourceExecution = false })).ConfigureAwait(true);
        if (ticks < 1 || maximumGap > 2000) { throw new InvalidOperationException("Native study did not preserve a responsive dispatcher in the isolated fixture."); }
    }
}
