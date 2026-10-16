using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Laura.Core.Conversation;
using Laura.Core.Execution;
using Laura.Core.Permissions;
using Laura.Platform.Windows.System;

/// <summary>Runs the existing Core build and tests through the real reviewed background-command boundary, without restore or package installation.</summary>
internal static class CommandJobsSdkSmoke
{
    /// <summary>Verifies actual SDK exit and independent test-result counters. Args: directory: Owned diagnostics; cancellationToken: Harness deadline.</summary>
    internal static async Task RunAsync(string directory, CancellationToken cancellationToken)
    {
        using var provider = new ProviderExecutionScope("local");
        using var permissions = new PermissionManager(new PermissionStore(Path.Combine(directory, "Security", "permissions.json")), () => new());
        permissions.Changed += (_, _) => { foreach (var pending in permissions.PendingRequests) { permissions.Decide(pending.Id, ApprovalChoice.Once); } };
        await using var jobs = new CommandJobs(new WindowsCommandExecutor(permissions));
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        if (!File.Exists(Path.Combine(root, "Laura.sln"))) { throw new InvalidOperationException("SDK diagnostics require the repository smoke output directory."); }
        string results = Path.Combine(directory, "Results"); Directory.CreateDirectory(results);
        var request = new CommandExecutionRequest(Environment.ProcessPath!, ["build", Path.Combine(root, "Laura.Core", "Laura.Core.csproj"), "--no-restore", "-v", "minimal"], root)
        { TaskId = Guid.NewGuid(), TimeoutSeconds = 120 };
        var build = await jobs.StartAsync(request, "", cancellationToken); var built = await WaitAsync(jobs, build, cancellationToken);
        if (!built.Succeeded) { throw new InvalidOperationException("The actual background Core build did not exit successfully."); }
        var test = await jobs.StartAsync(request with { TaskId = Guid.NewGuid(), Arguments = ["test", Path.Combine(root, "Laura.Core.Tests", "Laura.Core.Tests.csproj"),
            "--no-build", "--no-restore", "--logger", "trx;LogFileName=background-core-tests.trx", "--results-directory", results, "-v", "minimal"] }, "", cancellationToken);
        var tested = await WaitAsync(jobs, test, cancellationToken);
        if (!tested.Succeeded) { throw new InvalidOperationException("The actual background Core tests did not exit successfully."); }
        var document = XDocument.Load(Path.Combine(results, "background-core-tests.trx"));
        var counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
        int total = int.Parse(counters.Attribute("total")!.Value, global::System.Globalization.CultureInfo.InvariantCulture);
        int passed = int.Parse(counters.Attribute("passed")!.Value, global::System.Globalization.CultureInfo.InvariantCulture);
        if (total < 665 || passed != total || counters.Attribute("failed")!.Value != "0") { throw new InvalidOperationException("Background SDK test evidence is incomplete or failed."); }
        await File.WriteAllTextAsync(Path.Combine(directory, "command-jobs-sdk.json"), JsonSerializer.Serialize(new { ActualCoreBuild = built.Succeeded, ActualCoreTests = tested.Succeeded,
            BuildProcessId = build.ProcessId, TestProcessId = test.ProcessId, TotalTests = total, PassedTests = passed, RestorePerformed = false, PackageInstallation = false }), cancellationToken);
        Console.WriteLine("Actual background SDK build and tests passed: " + passed + " independently observed TRX passes; no restore or package installation.");
    }

    /// <summary>Polls only bounded native metadata in this diagnostic, leaving the calling thread available. Args: jobs: Real manager; job: Observed creation; cancellationToken: Deadline. Returns: Actual terminal state.</summary>
    private static async Task<CommandJobSnapshot> WaitAsync(CommandJobs jobs, CommandJobSnapshot job, CancellationToken cancellationToken)
    {
        long began = Stopwatch.GetTimestamp(); CommandJobSnapshot current;
        while (!(current = jobs.Read(job.TaskId, job.Id, "")).Finished)
        { if (Stopwatch.GetElapsedTime(began) > TimeSpan.FromSeconds(135)) { throw new TimeoutException("Background SDK diagnostic exceeded its deadline."); } await Task.Delay(100, cancellationToken); }
        return current;
    }
}
