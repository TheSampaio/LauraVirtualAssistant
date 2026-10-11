using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Skills;
using Laura.Platform.Windows.System;
using IShellController = Laura.Core.Abstractions.IShellController;

namespace Laura.App.Infrastructure;

/// <summary>Implements structured Windows, browser, file, vision, memory and development capabilities for Gemini.</summary>
public sealed class MauiAssistantTools(WindowsApplicationCatalog applications, IProcessLauncher launcher,
    WindowsResourceMonitor resources, ISystemController system, IConversationMemory memory, ISettingsService settings,
    IUiLocalizer ui, IDesktopTools desktop, IEnumerable<ISkill> skills,
    AssistantStorage storage, IShellController shell, ReminderScheduler reminders) : IAssistantTools, IDisposable
{
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly Stack<Func<Task>> _undo = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _startedApplications = new();
    /// <summary>Gets observed successful application launches for diagnostics without exposing tool payloads.</summary>
    public IReadOnlyList<string> StartedApplications => _startedApplications.ToArray();
    /// <inheritdoc />
    public object[] Declarations { get; } = [new { googleSearch = new { } }, new { functionDeclarations = new object[] {
        Function("open_app", "Open ANY installed or portable application by name. Discovers shortcuts and registry entries; supports Steam, PCSX2 and other applications. Use this tool rather than claiming that you cannot open apps.", ["name"], ("name", "STRING", "Requested application name")),
        Function("list_apps", "Find actual installed applications and launch targets; refresh discovery when needed.", [], ("query", "STRING", "Optional app name")),
        Function("system_status", "Read real CPU, RAM, disk and network traffic measurements.", []),
        Function("screen_process", "Capture a real screen or camera image for visual analysis. The image is attached to your live context after native user confirmation. Do not claim vision before using this tool.", [], ("source", "STRING", "screen or camera")),
        Function("computer_control", "Control actual Windows applications. Actions: list_windows, focus, type, key, click, double_click, right_click, scroll, move, drag. Focus the intended window first. For visual actions first capture screen, then use actual pixel coordinates. Use confirm_action before sending communications, purchases or submitting destructive commands.", ["action"], ("action", "STRING", "Action"), ("window", "STRING", "Exact or unique window title"), ("text", "STRING", "Literal text or key chord such as CTRL+S"), ("x", "INTEGER", "Primary-screen x coordinate"), ("y", "INTEGER", "Primary-screen y coordinate"), ("x2", "INTEGER", "Drag destination x"), ("y2", "INTEGER", "Drag destination y"), ("steps", "INTEGER", "Scroll wheel steps; positive is up")),
        Function("browser_control", "Open a URL or search in the default browser. Also supports new_tab, close_tab, next_tab, previous_tab, back, forward, reload, find, zoom_in, zoom_out and zoom_reset on a focused browser window. For current facts use Google Search grounding. For page inspection use screen_process and computer_control.", ["action"], ("action", "STRING", "Browser action"), ("url", "STRING", "HTTP/HTTPS URL"), ("query", "STRING", "Search or find text"), ("window", "STRING", "Browser window title")),
        Function("computer_settings", "Windows settings and media controls. Actions: volume_up, volume_down, volume_mute, play_pause, lock, settings, task_manager, show_desktop, minimize, maximize, dark_mode, light_mode, wallpaper, shutdown, restart. Shutdown/restart require native confirmation.", ["action"], ("action", "STRING", "Setting action"), ("value", "INTEGER", "Optional volume steps"), ("path", "STRING", "Existing wallpaper image"), ("window", "STRING", "Target window when needed")),
        Function("file_control", "Operate on real user files. Actions: list, search, read, write, mkdir, copy, move, delete, open, info, undo. Aliases: desktop, downloads, documents, pictures, workspace. Writes and destructive actions require native confirmation; deletions move to reversible Laura trash. Read text/code files to help develop projects; use write to save generated code.", ["action"], ("action", "STRING", "File action"), ("path", "STRING", "File or folder"), ("destination", "STRING", "Copy/move destination"), ("content", "STRING", "Literal file content"), ("query", "STRING", "File name search")),
        Function("run_program", "Run a specific installed executable with structured arguments for development tasks or existing projects, after native user confirmation. Captures actual exit code and output; no shell interpolation. Do not invent successful builds.", ["executable"], ("executable", "STRING", "Executable name or existing executable path"), ("arguments", "STRING", "JSON array of literal command-line arguments"), ("directory", "STRING", "Working directory"), ("timeout", "INTEGER", "Timeout seconds, up to 120")),
        Function("save_memory", "Save a fact or preference the user asks you to remember.", ["key", "value"], ("key", "STRING", "Stable fact key"), ("value", "STRING", "Fact text")),
        Function("recall_memory", "Search local user memories before saying you do not remember.", [], ("query", "STRING", "Optional fact query")),
        Function("forget_memory", "Forget a specific saved fact requested by the user.", ["key"], ("key", "STRING", "Fact key")),
        Function("clipboard", "Read or write the clipboard; reading needs native confirmation.", ["action"], ("action", "STRING", "read or write"), ("text", "STRING", "Literal text to copy")),
        Function("reminder", "Add, list or cancel an explicitly requested persistent reminder. Notifications use Gemini while Laura is running. Do not create schedules unless the user requested them.", ["action"], ("action", "STRING", "add, list or cancel"), ("text", "STRING", "Reminder in the user's language"), ("due", "STRING", "ISO 8601 date/time including timezone offset"), ("seconds", "INTEGER", "Alternative relative delay in seconds"), ("id", "STRING", "Identifier to cancel")),
        Function("confirm_action", "Ask native user approval before communicating externally, submitting purchases or other consequential actions. A denial must stop the action.", ["description"], ("description", "STRING", "Concrete action for approval in the user's language")),
        Function("run_local_command", "Execute existing Laura commands such as time, weather, web/YouTube search, notes, undo, volume and settings. For general app launch use open_app.", ["command"], ("command", "STRING", "Plain local command")),
        Function("shutdown_assistant", "Exit Laura only when the user asks to close the assistant.", [])
    } }];

    /// <summary>Builds a public function schema from supported fields.</summary>
    private static object Function(string name, string description, string[] required, params (string Name, string Type, string Description)[] fields) => new
    { name, description, parameters = new { type = "OBJECT", properties = fields.ToDictionary(field => field.Name, field => (object)new { type = field.Type, description = field.Description }), required } };

    /// <inheritdoc />
    public async Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        await _actions.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string action = Value(arguments, "action");
            switch (name)
            {
                case "open_app":
                    if (applications.Applications.Count == 0) { await applications.RefreshAsync(cancellationToken).ConfigureAwait(false); }
                    string requested = Value(arguments, "name");
                    IReadOnlyList<InstalledApplication> matches = applications.Find(requested);
                    if (matches.Count == 0) { await applications.RefreshAsync(cancellationToken).ConfigureAwait(false); matches = applications.Find(requested); }
                    if (matches.Count == 0) { return new("No matching application was found. Ask the user for the installed program name or path. Do not claim success."); }
                    var ranked = matches.GroupBy(app => ApplicationNameMatcher.Score(app.Name, requested)).OrderByDescending(group => group.Key).First().ToArray();
                    if (ranked.Length > 1 && ranked.Select(app => app.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    { return new("Choose an application: " + JsonSerializer.Serialize(ranked)); }
                    InstalledApplication target = ranked[0];
                    bool started = launcher.TryStart(target.Target);
                    if (started) { _startedApplications.Enqueue(target.Name); while (_startedApplications.Count > 100) { _startedApplications.TryDequeue(out _); } }
                    return new(started ? "Started application: " + target.Name : "Windows refused to start: " + target.Name);
                case "list_apps":
                    await applications.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    string query = Value(arguments, "query");
                    return new(JsonSerializer.Serialize((query.Length == 0 ? applications.Applications : applications.Find(query)).Take(100)));
                case "system_status": return new(JsonSerializer.Serialize(resources.Sample(storage.Directory)));
                case "screen_process":
                    bool camera = Value(arguments, "source") == "camera";
                    if (!await ConfirmAsync(ui.Get(camera ? "ui.maui.confirmCamera" : "ui.tools.confirm.CaptureScreen"), cancellationToken).ConfigureAwait(false)) { return new("User declined capture."); }
                    byte[] image = camera ? await MainThread.InvokeOnMainThreadAsync(() => WindowsCameraCapture.CaptureAsync(cancellationToken)).ConfigureAwait(false)
                        : await WindowsScreenCapture.CaptureAsync(cancellationToken, jpeg: true).ConfigureAwait(false);
                    return new("Actual " + (camera ? "camera" : "primary desktop") + " frame attached. Analyze it before acting. Camera is released after this single frame.", image);
                case "computer_control": return new(await ComputerActionAsync(action, arguments, cancellationToken).ConfigureAwait(false));
                case "browser_control": return new(BrowserAction(action, arguments));
                case "computer_settings": return new(await SystemActionAsync(action, arguments, cancellationToken).ConfigureAwait(false));
                case "file_control": return new(await FileActionAsync(action, arguments, cancellationToken).ConfigureAwait(false));
                case "run_program": return new(await RunProgramAsync(arguments, cancellationToken).ConfigureAwait(false));
                case "save_memory":
                    if (!settings.Current.RememberConversations) { return new("User memory is disabled."); }
                    memory.SetFact(Value(arguments, "key"), Value(arguments, "value")); return new("Fact saved.");
                case "recall_memory": return new(settings.Current.RememberConversations ? JsonSerializer.Serialize(memory.Search(Value(arguments, "query"))) : "User memory is disabled.");
                case "forget_memory": memory.DeleteFact(Value(arguments, "key")); return new("Fact removed.");
                case "clipboard":
                    if (action == "read") { return new(await desktop.ExecuteAsync(DesktopAction.ReadClipboard, null, cancellationToken).ConfigureAwait(false)); }
                    if (action != "write") { return new("Unknown clipboard action."); }
                    await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(Value(arguments, "text"))).ConfigureAwait(false); return new("Clipboard updated.");
                case "confirm_action": return new(await ConfirmAsync(Value(arguments, "description"), cancellationToken).ConfigureAwait(false) ? "Approved for the described action only." : "Denied. Stop this action.");
                case "reminder":
                    if (action == "add")
                    {
                        DateTimeOffset due = DateTimeOffset.TryParse(Value(arguments, "due"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : DateTimeOffset.UtcNow.AddSeconds(Number(arguments, "seconds", 0));
                        return new(JsonSerializer.Serialize(await reminders.AddAsync(Value(arguments, "text"), due, cancellationToken).ConfigureAwait(false)));
                    }
                    if (action is not ("list" or "cancel")) { return new("Unknown reminder action."); }
                    return new(JsonSerializer.Serialize(await reminders.ListAsync(action == "cancel" ? Value(arguments, "id") : null, cancellationToken).ConfigureAwait(false)));
                case "run_local_command":
                    SkillRequest request = SkillRequest.Create(Value(arguments, "command"), settings.Current.ResolveCulture(), DateTimeOffset.Now, SkillRequestSource.Text);
                    ISkill? skill = skills.OrderBy(item => item.Priority).FirstOrDefault(item => item.CanHandle(request));
                    if (skill is null) { return new("No local command matches. Use the structured action tools instead."); }
                    SkillResponse response = await skill.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                    return new(response.SpokenText ?? "Command completed.");
                case "shutdown_assistant": MainThread.BeginInvokeOnMainThread(shell.Shutdown); return new("Laura is closing.");
                default: return new("Unknown capability.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new("Action failed: " + error.GetType().Name + ". Do not claim that it succeeded."); }
        finally { _actions.Release(); }
    }
    /// <summary>Performs finite native input actions without parsing scripts.</summary>
    private static async Task<string> ComputerActionAsync(string action, JsonElement args, CancellationToken token)
    {
        if (action == "list_windows") { return JsonSerializer.Serialize(WindowsComputerController.ListWindows()); }
        string window = Value(args, "window");
        if (window.Length > 0 && !WindowsComputerController.Focus(window)) { return "Window not found or ambiguous. List windows and choose the exact title."; }
        switch (action)
        {
            case "focus": return window.Length > 0 ? "Window focused." : "Provide a window title.";
            case "type": WindowsComputerController.Type(Value(args, "text")); break;
            case "key": WindowsComputerController.Hotkey(Value(args, "text").Split('+', StringSplitOptions.TrimEntries)); break;
            case "click": case "double_click": case "right_click": WindowsComputerController.Click(Number(args, "x", -1), Number(args, "y", -1), action == "right_click", action == "double_click"); break;
            case "scroll": WindowsComputerController.Scroll(Number(args, "steps", -3)); break;
            case "move": WindowsComputerController.Move(Number(args, "x", -1), Number(args, "y", -1)); break;
            case "drag": await WindowsComputerController.DragAsync(Number(args, "x", -1), Number(args, "y", -1), Number(args, "x2", -1), Number(args, "y2", -1), token).ConfigureAwait(false); break;
            default: return "Unknown computer action.";
        }
        return "Input delivered. Capture the screen to verify the resulting application state.";
    }
    /// <summary>Uses native browser launch and focused-browser keyboard controls.</summary>
    private string BrowserAction(string action, JsonElement args)
    {
        if (action is "open" or "search")
        {
            string address = action == "search" ? "https://www.google.com/search?q=" + Uri.EscapeDataString(Value(args, "query")) : Value(args, "url");
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) { return "Only HTTP and HTTPS browser URLs are supported."; }
            return launcher.TryOpen(uri) ? "Browser launch requested for " + uri : "Browser launch failed.";
        }
        string window = Value(args, "window");
        if (window.Length == 0 || !WindowsComputerController.Focus(window)) { return "Choose a unique browser window title first."; }
        string[]? keys = action switch { "new_tab" => ["CTRL", "T"], "close_tab" => ["CTRL", "W"], "next_tab" => ["CTRL", "TAB"], "previous_tab" => ["CTRL", "SHIFT", "TAB"], "back" => ["ALT", "LEFT"], "forward" => ["ALT", "RIGHT"], "reload" => ["CTRL", "R"], "find" => ["CTRL", "F"], "zoom_reset" => ["CTRL", "0"], "zoom_in" => ["CTRL", "PLUS"], "zoom_out" => ["CTRL", "MINUS"], _ => null };
        if (keys is null) { return "Unsupported browser action. Use computer_control with an explicit key or screen coordinates."; }
        WindowsComputerController.Hotkey(keys);
        if (action == "find") { WindowsComputerController.Type(Value(args, "query")); }
        return "Browser input delivered; inspect the screen to verify.";
    }
    /// <summary>Applies native system settings and confirms session-ending actions.</summary>
    private async Task<string> SystemActionAsync(string action, JsonElement args, CancellationToken token)
    {
        switch (action)
        {
            case "volume_up": system.AdjustVolume(Math.Clamp(Number(args, "value", 5), 1, 50)); break;
            case "volume_down": system.AdjustVolume(-Math.Clamp(Number(args, "value", 5), 1, 50)); break;
            case "volume_mute": system.ToggleMute(); break;
            case "lock": system.LockWorkstation(); break;
            case "settings": return launcher.TryOpen(new Uri("ms-settings:")) ? "Windows settings opened." : "Could not open settings.";
            case "task_manager": return launcher.TryStart("taskmgr.exe") ? "Task Manager started." : "Task Manager could not start.";
            case "show_desktop": WindowsComputerController.Hotkey(["WIN", "D"]); break;
            case "minimize": case "maximize":
                string window = Value(args, "window");
                if (window.Length == 0 || !WindowsComputerController.Focus(window)) { return "Choose the target window first."; }
                WindowsComputerController.Hotkey(["WIN", action == "minimize" ? "DOWN" : "UP"]); break;
            case "play_pause": WindowsComputerController.Hotkey(["PLAYPAUSE"]); break;
            case "wallpaper":
                string wallpaper = ResolvePath(Value(args, "path"));
                if (!await ConfirmAsync(ui.Get("ui.maui.confirmFile", "wallpaper", wallpaper), token).ConfigureAwait(false)) { return "User declined wallpaper change."; }
                return WindowsComputerController.SetWallpaper(wallpaper) ? "Wallpaper changed." : "Windows did not apply the wallpaper.";
            case "dark_mode": case "light_mode":
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                { key.SetValue("AppsUseLightTheme", action == "light_mode" ? 1 : 0); key.SetValue("SystemUsesLightTheme", action == "light_mode" ? 1 : 0); }
                break;
            case "shutdown": case "restart":
                if (!await ConfirmAsync(ui.Get("ui.maui.confirmSystem", action), token).ConfigureAwait(false)) { return "User declined."; }
                using (Process? process = Process.Start(new ProcessStartInfo("shutdown.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { action == "restart" ? "/r" : "/s", "/t", "30" } })) { }
                return "Scheduled in 30 seconds. shutdown.exe /a can cancel it.";
            default: return "This system setting has no native adapter yet.";
        }
        return "System action applied.";
    }
    /// <summary>Resolves file aliases and refuses links and protected system roots for mutation.</summary>
    private string ResolvePath(string value)
    {
        string alias = value.Trim().ToLowerInvariant();
        string path = alias switch { "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "workspace" or "" => Path.Combine(storage.Directory, "Workspace"), _ => Environment.ExpandEnvironmentVariables(value) };
        path = Path.GetFullPath(path);
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        { if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new ArgumentException("Linked file locations are not supported.", nameof(value)); } }
        return path;
    }
    /// <summary>Requires approval for filesystem mutations and keeps reversible copies in assistant storage.</summary>
    private async Task<string> FileActionAsync(string action, JsonElement args, CancellationToken token)
    {
        if (action == "undo") { if (_undo.Count == 0) { return "Nothing to undo."; } if (!await ConfirmAsync(ui.Get("ui.tools.confirm.Undo"), token).ConfigureAwait(false)) { return "User declined undo."; } await _undo.Peek()().ConfigureAwait(false); _undo.Pop(); return "Last file action reversed."; }
        string path = ResolvePath(Value(args, "path"));
        if (action is "list" or "search")
        {
            if (!Directory.Exists(path)) { return "Folder not found."; }
            var entries = Directory.EnumerateFileSystemEntries(path).Where(entry => (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0);
            if (action == "search") { entries = entries.Where(entry => Path.GetFileName(entry).Contains(Value(args, "query"), StringComparison.OrdinalIgnoreCase)); }
            return JsonSerializer.Serialize(entries.Take(150));
        }
        if (action == "read")
        {
            var file = new FileInfo(path); if (!file.Exists) { return "File not found."; }
            if (file.Length > 2 * 1024 * 1024 || !new[] { ".txt", ".md", ".cs", ".py", ".js", ".ts", ".tsx", ".html", ".css", ".json", ".xml", ".csv", ".ini", ".log", ".yml", ".yaml", ".csproj", ".sln", ".bat", ".ps1" }.Contains(file.Extension.ToLowerInvariant(), StringComparer.Ordinal)) { return "Use an image capture for visual files. This reader supports text and code files up to 2 MiB."; }
            if (!await ConfirmAsync(ui.Get("ui.maui.confirmRead", path), token).ConfigureAwait(false)) { return "User declined file reading."; }
            string content = await File.ReadAllTextAsync(path, token).ConfigureAwait(false); return content.Length > 32000 ? content[..32000] + "\n[truncated]" : content;
        }
        if (action == "info") { return File.Exists(path) ? JsonSerializer.Serialize(new { path, bytes = new FileInfo(path).Length, modified = File.GetLastWriteTimeUtc(path) }) : "File not found."; }
        if (action == "open") { return launcher.TryStart(path) ? "File opened." : "File could not open."; }
        string? destinationPath = action is "copy" or "move" ? ResolvePath(Value(args, "destination")) : null;
        if (ProtectedPath(path) || (destinationPath is not null && ProtectedPath(destinationPath))) { return "System locations cannot be changed by this file tool."; }
        string detail = destinationPath is null ? path : path + "\n→ " + destinationPath;
        if (action == "write") { string preview = Value(args, "content"); detail += "\n\n" + preview[..Math.Min(preview.Length, 600)]; }
        if (!await ConfirmAsync(ui.Get("ui.maui.confirmFile", action, detail), token).ConfigureAwait(false)) { return "User declined file change."; }
        token.ThrowIfCancellationRequested();
        string backupDirectory = Path.Combine(storage.Directory, "Undo"); Directory.CreateDirectory(backupDirectory);
        switch (action)
        {
            case "write":
                string content = Value(args, "content"); if (content.Length > 100000) { return "Content exceeds the file-write limit."; }
                string? backup = null;
                if (File.Exists(path)) { if (new FileInfo(path).Length > 32 * 1024 * 1024) { return "The existing file exceeds the reversible-edit limit."; } backup = Path.Combine(backupDirectory, Guid.NewGuid().ToString("N")); File.Copy(path, backup); }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), token).ConfigureAwait(false);
                byte[] expectedHash = SHA256.HashData(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
                _undo.Push(() => { GuardUnchanged(path, expectedHash); if (backup is not null) { File.Copy(backup, path, true); } else { File.Delete(path); } return Task.CompletedTask; }); return "File written: " + path;
            case "mkdir": Directory.CreateDirectory(path); return "Folder created: " + path;
            case "copy": case "move":
                string destination = destinationPath!; if (File.Exists(destination) || Directory.Exists(destination)) { return "Destination already exists; choose a new path."; }
                if (action == "copy") { File.Copy(path, destination); using var copied = File.OpenRead(destination); byte[] copiedHash = SHA256.HashData(copied); _undo.Push(() => { GuardUnchanged(destination, copiedHash); File.Delete(destination); return Task.CompletedTask; }); }
                else { if (Directory.Exists(path)) { Directory.Move(path, destination); _undo.Push(() => { Directory.Move(destination, path); return Task.CompletedTask; }); } else { File.Move(path, destination); _undo.Push(() => { File.Move(destination, path); return Task.CompletedTask; }); } }
                return "File action completed: " + destination;
            case "delete":
                string trash = Path.Combine(backupDirectory, Guid.NewGuid().ToString("N"));
                if (Directory.Exists(path)) { Directory.Move(path, trash); _undo.Push(() => { Directory.Move(trash, path); return Task.CompletedTask; }); }
                else { File.Move(path, trash); _undo.Push(() => { File.Move(trash, path); return Task.CompletedTask; }); }
                return "Moved to reversible Laura trash.";
            default: return "Unknown file action.";
        }
    }
    /// <summary>Rejects filesystem roots and Windows installation locations at either end of a mutation.</summary>
    private static bool ProtectedPath(string path) => path == Path.GetPathRoot(path) || new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
        .Select(Environment.GetFolderPath).Where(root => root.Length > 0).Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    /// <summary>Preserves files modified outside Laura after a reversible action.</summary>
    private static void GuardUnchanged(string path, byte[] expectedHash)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new InvalidOperationException("Undo target was moved or linked."); }
        using var file = File.OpenRead(path);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), expectedHash)) { throw new InvalidOperationException("Undo refused because the file changed outside Laura."); }
    }
    /// <summary>Runs an approved executable with separate arguments and a bounded output/timeout.</summary>
    private async Task<string> RunProgramAsync(JsonElement args, CancellationToken token)
    {
        string executable = Value(args, "executable"), directory = ResolvePath(Value(args, "directory"));
        string[] arguments = JsonSerializer.Deserialize<string[]>(Value(args, "arguments", "[]")) ?? [];
        if (arguments.Length > 30 || arguments.Any(argument => argument.Length > 4000)) { return "Invalid process arguments."; }
        if (!await ConfirmAsync(ui.Get("ui.maui.confirmProgram", executable, string.Join(' ', arguments), directory), token).ConfigureAwait(false)) { return "User declined execution."; }
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        void Capture(object sender, DataReceivedEventArgs data) { lock (output) { if (data.Data is { } line && output.Length < 16000) { output.AppendLine(line); } } }
        process.OutputDataReceived += Capture; process.ErrorDataReceived += Capture;
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Number(args, "timeout", 60), 1, 120)));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (!process.HasExited) { process.Kill(entireProcessTree: true); } throw; }
        lock (output) { return "Exit code: " + process.ExitCode + "\n" + output; }
    }
    /// <summary>Shows a native confirmation; model text cannot self-approve.</summary>
    private Task<bool> ConfirmAsync(string description, CancellationToken token) => WindowsConfirmation.AskAsync(description, ui.Get("ui.yes"), ui.Get("ui.no"), token);
    /// <summary>Reads one bounded string parameter without coercing other JSON types.</summary>
    private static string Value(JsonElement args, string name, string fallback = "") => args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? fallback) : fallback;
    /// <summary>Reads an integer parameter with a predictable fallback.</summary>
    private static int Number(JsonElement args, string name, int fallback) => args.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : fallback;
    /// <inheritdoc />
    public void Dispose() => _actions.Dispose();
}
