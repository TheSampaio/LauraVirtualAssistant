using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Configuration;
using Laura.Core.Engine;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Laura.App.Infrastructure;

/// <summary>Investigates a synthetic binary through real native tools; fixture implementation and held-out cases never enter model context.</summary>
internal static class ChatGptApplicationSmokeRunner
{
    private static readonly JsonSerializerOptions EvidenceJson = new() { WriteIndented = true };
    internal static string Fixture => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "_Output", "Validation", "Laura2", "ChatGPTIntegration", "ApplicationFixture", "OpaqueProbe.dll"));
    internal static string Dotnet => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    /// <summary>Restricts this diagnostic to literal numeric invocations of the one reviewed synthetic binary.</summary>
    internal static bool AllowsInvocation(JsonElement arguments)
    {
        try
        {
            string executable = arguments.GetProperty("executable").GetString() ?? "";
            string[] values = StructuredProcessParameters.Arguments(arguments);
            return Path.GetFullPath(executable).Equals(Dotnet, StringComparison.OrdinalIgnoreCase) && values.Length == 2
                && Path.GetFullPath(values[0]).Equals(Fixture, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is >= 0 and <= 100
                && StructuredProcessParameters.Environment(arguments).Count == 0
                && (!arguments.TryGetProperty("directory", out var directory) || string.IsNullOrWhiteSpace(directory.GetString())
                    || PermissionPolicy.Within(Path.GetFullPath(directory.GetString()!), Path.GetDirectoryName(Fixture)!));
        }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException) { return false; }
    }
    /// <summary>Runs a bounded black-box investigation and validates its declared hypothesis against hidden native oracle cases.</summary>
    internal static async Task RunAsync(IServiceProvider services, Window window, string directory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--included-plan-only-confirmed", StringComparer.Ordinal) || !File.Exists(Fixture))
        { throw new InvalidOperationException("Explicit included-plan confirmation and the compiled synthetic fixture are required."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        string originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Fixture, timeout.Token).ConfigureAwait(true)));
        var connection = services.GetRequiredService<IChatGptConnection>(); var account = await connection.ReadAsync(timeout.Token).ConfigureAwait(true);
        if (account.Status != "plan_authorized" || !services.GetRequiredService<IChatGptInference>().IsAvailable)
        { throw new InvalidOperationException("The already verified ChatGPT plan connection is required."); }
        var settings = services.GetRequiredService<ISettingsService>();
        string publicVault = Path.Combine(Environment.CurrentDirectory, "_Output", "Validation", "Laura2", "AgentRework", "Acceptance", "Storage", "Brain");
        var brain = services.GetRequiredService<BrainService>(); await brain.ChangeLocationAsync(publicVault, false, true, timeout.Token).ConfigureAwait(true);
        var prior = brain.Snapshot.Notes.Where(note => note.Source == "repository_module_analysis").Select(note => new { note.Id, note.Hash, note.Title }).Take(4).ToArray();
        var publicReferences = brain.Snapshot.Notes.Where(note => prior.Any(reference => reference.Id == note.Id))
            .Select(note => Path.GetFullPath(Path.Combine(publicVault, note.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await settings.UpdateAsync(settings.Current with { ProviderSelection = ConversationProviderSelection.ChatGpt, MicrophoneMuted = true,
            Permissions = settings.Current.Permissions with { Mode = ApprovalMode.Ask },
            ChatGpt = new() { Enabled = true, Model = account.VerifiedModel, Registration = account.Active, ShareContext = true, IncludedPlanOnly = true },
            CognitiveTasks = new() { MaximumCycles = 3, MaximumToolCalls = 32, MaximumProviderCalls = 16, ActiveMinutes = 3 } }, timeout.Token).ConfigureAwait(true);
        var tools = (MauiAssistantTools)services.GetRequiredService<IAssistantTools>(); var observations = new List<object>(); var sampled = new HashSet<int>();
        void Observe(string name, JsonElement arguments, AssistantToolResult result)
        {
            if (name == "run_program" && !result.Failed) { sampled.Add(int.Parse(StructuredProcessParameters.Arguments(arguments)[1], CultureInfo.InvariantCulture)); }
            observations.Add(new { Tool = name, Arguments = name == "run_program" ? arguments.Clone() : JsonSerializer.SerializeToElement(new { }), result.Status, result.Failed, PublicFixtureOutput = name == "run_program" ? result.Text : "Reference content omitted." });
        }
        tools.IsolatedAgentToolObserved += Observe;
        var engine = services.GetRequiredService<IAssistantEngine>(); var tasks = services.GetRequiredService<CognitiveTasks>();
        var permissions = services.GetRequiredService<PermissionManager>(); int approvals = 0;
        try
        {
            Task investigation = engine.SubmitCommandAsync("Use the repository investigation knowledge you acquired to investigate an opaque synthetic program. "
                + "You may read relevant prior analysis with read_brain_passage; metadata: " + JsonSerializer.Serialize(prior)
                + ". Execute only this program using run_program with executable " + Dotnet + ", directory " + Path.GetDirectoryName(Fixture) + " and literal arguments [\"" + Fixture.Replace("\\", "\\\\", StringComparison.Ordinal) + "\", \"an integer 0..100\"]. "
                + "Its implementation is withheld. Sample inputs, form and test hypotheses, distinguish evidence from inference and acknowledge uncertainty. "
                + "Return a final JSON object with integer a,b,c,modulus for your best quadratic modular hypothesis y=(a*x*x+b*x+c)%modulus, and a short evidence summary. The hypothesis will be tested on undisclosed cases. Do not read source, execute other commands or modify files.", timeout.Token);
            int settled = 0; var elapsed = Stopwatch.StartNew();
            while (settled < 20)
            {
                Guid task = services.GetRequiredService<IConversationMemory>().WorkingContext.TurnId;
                var current = tasks.Current(task);
                bool active = !investigation.IsCompleted || engine.PendingMessageCount > 0 || current?.Rounds is { Outcome: "running" } or { NeedsContinuation: true, CanContinue: true };
                settled = active ? 0 : settled + 1;
                foreach (var review in permissions.PendingRequests)
                {
                    string[] detail = review.Action.Description.Split('\n'); bool allowed = false;
                    if (review.Action.TaskId == task && review.Action.Destination == "api.openai.com" && review.Action.Operation == PermissionOperation.FileReadCloud)
                    { allowed = review.Action.Resources.Length > 0 && review.Action.Resources.All(resource => publicReferences.Contains(Path.GetFullPath(resource))); }
                    if (review.Action.TaskId == task && review.Action.Destination == "api.openai.com" && review.Action.Operation == PermissionOperation.Process && detail.Length >= 2)
                    {
                        try { allowed = AllowsInvocation(JsonSerializer.SerializeToElement(new { executable = detail[0], arguments = JsonSerializer.Deserialize<string[]>(detail[1]), directory = detail.Length > 2 ? detail[2] : "" })); }
                        catch (JsonException) { }
                    }
                    permissions.Decide(review.Id, allowed ? ApprovalChoice.Once : ApprovalChoice.Deny); if (allowed) { approvals++; }
                }
                if (elapsed.Elapsed > TimeSpan.FromMinutes(3.5)) { await tasks.PauseAsync(task).ConfigureAwait(true); break; }
                await Task.Delay(100, timeout.Token).ConfigureAwait(true);
            }
            await investigation.ConfigureAwait(true);
            string answer = engine.ConversationHistory.LastOrDefault(message => message.Source == Laura.Core.Conversation.ConversationMessageSource.Assistant)?.Text ?? "";
            int[] heldOut = Enumerable.Range(0, 101).Reverse().Where(input => !sampled.Contains(input)).Take(3).ToArray();
            bool hypothesis = false;
            try
            {
                int start = answer.IndexOf('{'), end = answer.LastIndexOf('}');
                using var parsed = JsonDocument.Parse(answer[start..(end + 1)]); var values = parsed.RootElement;
                int a = values.GetProperty("a").GetInt32(), b = values.GetProperty("b").GetInt32(), c = values.GetProperty("c").GetInt32(), modulus = values.GetProperty("modulus").GetInt32();
                hypothesis = modulus > 0 && heldOut.Length == 3 && heldOut.All(input => ((long)a * input * input + (long)b * input + c) % modulus == (7 * input * input + 13 * input + 29) % 101);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException) { }
            bool unchanged = originalHash == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Fixture, timeout.Token).ConfigureAwait(true)));
            await File.WriteAllTextAsync(Path.Combine(directory, "investigation.md"), answer, timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "application.json"), JsonSerializer.Serialize(new { HypothesisPassedHiddenCases = hypothesis, FixtureUnchanged = unchanged,
                ExactProcessApprovals = approvals, NativeObservations = observations, HeldOutInputs = heldOut, DistinctObservedInputs = sampled.Count, PriorPublicAnalysisReferences = prior.Length, SourceDisclosed = false,
                Validated = hypothesis && unchanged && approvals >= 3, LearningCausalityProven = false, MicrophoneUsed = false }, EvidenceJson), timeout.Token).ConfigureAwait(true);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"), hypothesis && unchanged && approvals >= 3 ? "PASS: real model investigated the known synthetic binary through authorized native tools; hypothesis matched three hidden oracle cases. This does not prove learning causality or general REA proficiency." : "PARTIAL: operational hypothesis was not validated; inspect the retained synthetic evidence.", timeout.Token).ConfigureAwait(true);
            await MauiSmokeRunner.CaptureAsync(window, Path.Combine(directory, "application.png")).ConfigureAwait(true);
        }
        finally { tools.IsolatedAgentToolObserved -= Observe; services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
}
