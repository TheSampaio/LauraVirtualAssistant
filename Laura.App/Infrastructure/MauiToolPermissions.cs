using System.Text.Json;
using Laura.Core.Permissions;
using Laura.Core.Conversation;

namespace Laura.App.Infrastructure;

/// <summary>Closed tool/effect registry. Model supplied risk, origin, policy and permission fields are never consumed.</summary>
internal static class MauiToolPermissions
{
    internal static string CloudDestination => ProviderExecutionScope.Destination;
    internal static PermissionAction Describe(string name, JsonElement args, Guid taskId = default)
    {
        string action = Text(args, "action");
        PermissionOperation operation = name switch
        {
            "record_repository_analysis" or "repository_learning_status" or "brain_stats" => PermissionOperation.Internal,
            "clone_repository" or "authorize_workspace" or "inspect_brain_maintenance" or "study_github_repository" or "start_repository_study" or "continue_repository_study" or "repository_study_status" or "list_capabilities" or "plan_task" or "task_status" or "recent_task_context" or "assess_source_learning" => PermissionOperation.Internal, // specialized adapters own effect approvals
            "list_mcp_servers" or "connect_mcp_server" or "list_mcp_tools" or "call_mcp_tool" or "disconnect_mcp_server" => PermissionOperation.Internal, // the native MCP runtime gates the exact external effect
            // These dispatch through separately guarded adapters or are internal metadata operations.
            "start_program" or "program_status" or "stop_program" or "run_program" or "open_app" or "run_local_command" or "list_apps" or "system_status" or "stop_listening" or "recall_memory" or "read_brain_passage" or "learn_memory" or "reflect_experience" or "study_source" or "list_brain_skills" or "read_brain_skill" or "read_brain_skill_usage" or "propose_brain_skill" => PermissionOperation.Internal,
            "save_memory" or "forget_memory" or "shutdown_assistant" => PermissionOperation.SystemSettings,
            "reminder" when action == "list" => PermissionOperation.Internal,
            "reminder" when action is "add" or "cancel" => PermissionOperation.SystemSettings,
            "screen_process" when Text(args, "source") is "" or "screen" => PermissionOperation.ScreenCloud,
            "screen_process" when Text(args, "source") == "camera" => PermissionOperation.CameraCloud,
            "screenshot_control" when action is "region" or "window" or "fullscreen" or "copy" or "save" or "analyze" or "brain" or "cancel" => PermissionOperation.Internal,
            "read_brain_image" => PermissionOperation.Internal, // local read; cloud transmission gates the exact image separately
            "computer_control" when action == "list_windows" => PermissionOperation.Internal,
            "computer_control" when action == "focus" => PermissionOperation.WindowManagement,
            "close_app" => PermissionOperation.ApplicationClose,
            "computer_control" when action is "focus" or "type" or "key" or "click" or "double_click" or "right_click" or "scroll" or "move" or "drag" => PermissionOperation.DesktopInput,
            "browser_control" when action is "open" or "search" => PermissionOperation.Internal,
            "browser_control" when action is "new_tab" or "close_tab" or "next_tab" or "previous_tab" or "back" or "forward" or "reload" or "find" or "zoom_reset" or "zoom_in" or "zoom_out" => PermissionOperation.DesktopInput,
            "computer_settings" when action is "volume_up" or "volume_down" or "volume_mute" or "play_pause" => PermissionOperation.Internal,
            "computer_settings" when action is "settings" or "task_manager" => PermissionOperation.Internal,
            "computer_settings" when action is "lock" or "show_desktop" or "minimize" or "maximize" or "wallpaper" or "dark_mode" or "light_mode" or "shutdown" or "restart" => PermissionOperation.SystemSettings,
            "file_control" => action switch
            {
                "open" => PermissionOperation.Internal,
                "list" or "search" or "info" => ProviderExecutionScope.Destination.Length == 0 ? PermissionOperation.FileReadLocal : PermissionOperation.FileReadCloud,
                "read" => ProviderExecutionScope.Destination.Length == 0 ? PermissionOperation.FileReadLocal : PermissionOperation.FileReadCloud, "write" or "patch" => PermissionOperation.FileWrite,
                "mkdir" => PermissionOperation.FileCreateDirectory, "copy" => PermissionOperation.FileCopy,
                "move" => PermissionOperation.FileMove, "delete" => PermissionOperation.FileTrash,
                "undo" or "rollback" => PermissionOperation.Undo, _ => PermissionOperation.Unknown
            },
            "clipboard" when action == "read" => PermissionOperation.Internal, // guarded by IDesktopTools
            "clipboard" when action == "write" => PermissionOperation.ClipboardWrite,
            _ => PermissionOperation.Unknown
        };
        string[] resources = name == "file_control" && action == "undo" && args.TryGetProperty("lauraUndoTargets", out var undo) ? undo.Deserialize<string[]>() ?? []
            : name == "file_control" && action != "undo" ? action is "copy" or "move" ? [Text(args, "path"), Text(args, "destination")] : [Text(args, "path")]
            : name == "run_program" ? [Text(args, "executable"), Text(args, "directory")]
            : name == "computer_settings" && action == "wallpaper" ? [Text(args, "path")] : [];
        string destination = operation is PermissionOperation.FileReadCloud or PermissionOperation.ScreenCloud or PermissionOperation.CameraCloud or PermissionOperation.ImageCloud || name == "run_program" ? CloudDestination : "";
        // Literal argument data is shown for processes/input only; it is never persisted or read aloud.
        string detail = operation switch
        {
            PermissionOperation.Process => Text(args, "executable") + "\n" + JsonSerializer.Serialize(Laura.Core.Execution.StructuredProcessParameters.Arguments(args)) + "\n" + Text(args, "directory"),
            PermissionOperation.DesktopInput => Text(args, "window") + "\n" + args.GetRawText(),
            PermissionOperation.FileWrite when action == "patch" => "Scoped patch\nExpected hash: " + Text(args, "expected_hash") + "\nRemove:\n" + Text(args, "old_text") + "\nInsert:\n" + Text(args, "new_text"),
            PermissionOperation.FileWrite => Text(args, "content"),
            _ when name is "save_memory" or "forget_memory" => Text(args, "key") + "\n" + Text(args, "value"),
            _ when name == "reminder" => action + "\n" + Text(args, "text") + "\n" + Text(args, "due") + "\n" + Text(args, "id"),
            _ when name == "read_brain_image" => Text(args, "path"),
            _ => action
        };
        return new(operation, "Native tool: " + name, detail, resources, CanonicalParameters(args), destination,
            automaticEligible: operation == PermissionOperation.WindowManagement && args.TryGetProperty("lauraVerifiedTarget", out _), taskId: taskId);
    }
    private static string CanonicalParameters(JsonElement arguments)
    {
        using var bytes = new MemoryStream(); using (var writer = new Utf8JsonWriter(bytes)) { Write(arguments, writer); }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            { writer.WriteStartObject(); foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Write(property.Value, writer); } writer.WriteEndObject(); }
            else if (element.ValueKind == JsonValueKind.Array)
            { writer.WriteStartArray(); foreach (var value in element.EnumerateArray()) { Write(value, writer); } writer.WriteEndArray(); }
            else { element.WriteTo(writer); }
        }
    }
    private static string Text(JsonElement args, string key) => args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
