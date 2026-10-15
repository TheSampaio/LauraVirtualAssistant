using System.Text;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;
using Laura.App.Infrastructure;
using Windows.Storage.Pickers;

namespace Laura.App.Platforms.Windows;

/// <summary>Explicit local save destinations for dialogue and payload-free diagnostics; no network upload.</summary>
public static class WindowsDocumentExport
{
    private static readonly string[] Markdown = [".md"], Text = [".txt"], Json = [".json"], Zip = [".zip"];
    /// <summary>Exports the whole retained transcript from a stable snapshot, never the virtualized UI collection.</summary>
    public static async Task<bool> ConversationAsync(nint owner, ConversationActions actions, IApiKeyStore keys, IApiKeyStore openAiKeys, CancellationToken token = default)
    {
        var picker = Picker(owner, "Laura-conversa", new Dictionary<string, IList<string>> { ["Markdown"] = Markdown, ["Texto"] = Text, ["JSON"] = Json });
        var selected = await picker.PickSaveFileAsync().AsTask(token).ConfigureAwait(true);
        if (selected is null) { return false; }
        var snapshot = await actions.SnapshotAsync(token).ConfigureAwait(false);
        string[] secrets = [keys.Read(), openAiKeys.Read()];
        await Task.Run(async () =>
        {
            string text = ConversationExport.Render(snapshot, Path.GetExtension(selected.Name).TrimStart('.').ToLowerInvariant(), secrets);
            await WriteAtomicallyAsync(selected.Path, stream => stream.WriteAsync(Encoding.UTF8.GetBytes(text), token).AsTask(), token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        LauraDiagnostics.Record("conversation.exported", amount: snapshot.Count); return true;
    }
    /// <summary>Creates a ZIP with a technical analysis guide, counters and sanitized JSONL events.</summary>
    public static async Task<bool> DiagnosticsAsync(nint owner, RuntimeJournal journal, CancellationToken token = default)
    {
        var picker = Picker(owner, "Laura-diagnostico", new Dictionary<string, IList<string>> { ["Diagnóstico ZIP"] = Zip });
        var selected = await picker.PickSaveFileAsync().AsTask(token).ConfigureAwait(true);
        if (selected is null) { return false; }
        RuntimeLogSnapshot snapshot = await journal.SnapshotAsync(token).ConfigureAwait(false);
        string version = typeof(WindowsDocumentExport).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
        await Task.Run(() => WriteAtomicallyAsync(selected.Path, stream => DiagnosticExport.WriteAsync(stream, snapshot, version, token), token), token).ConfigureAwait(false);
        LauraDiagnostics.Record("diagnostics.exported", amount: snapshot.Events.Count); return true;
    }
    private static FileSavePicker Picker(nint owner, string name, Dictionary<string, IList<string>> formats)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var format in formats) { picker.FileTypeChoices.Add(format.Key, format.Value); }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner); return picker;
    }
    private static async Task WriteAtomicallyAsync(string destination, Func<Stream, Task> write, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        string temporary = Path.Combine(directory, ".laura-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { await write(stream).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
