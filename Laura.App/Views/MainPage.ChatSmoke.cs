using System.Diagnostics;
using Laura.App.Platforms.Windows;
using Laura.App.Infrastructure;
using Laura.Core.Abstractions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Documents;
using NativeClipboard = global::Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Laura.App.Views;

public sealed partial class MainPage
{
    /// <summary>Checks that a delayed coalesced stream cannot recreate a provisional bubble after the final native reply.</summary>
    internal async Task VerifyStreamCompletionForSmokeAsync()
    {
        EnsureSmoke(); Guid turn = Guid.NewGuid();
        ReceiveStream(null, new(turn, "Synthetic provisional reply", false));
        await WaitNativeAsync(() => _provisional is not null).ConfigureAwait(true);
        const string final = "Synthetic finalized reply";
        ReceiveStream(null, new(turn, final, true));
        AddMessage(new(ConversationMessageSource.Assistant, final, DateTimeOffset.Now));
        await Task.Delay(180).ConfigureAwait(true);
        if (_provisional is not null || _messages.Count(message => message.Text == final) != 1)
        { throw new InvalidOperationException("A queued stream recreated a finalized reply or produced duplicate native bubbles."); }
    }
    internal async Task VerifyChatUxForSmokeAsync(string directory)
    {
        EnsureSmoke(); _messages.Clear(); _conversationScroll.Follow(); AttachConversationScroll();
        string fixture = "abc Ω exemplo\n\n```csharp\nConsole.WriteLine(\"Olá\");\n```\nhttps://example.invalid/路径";
        AddMessage(new(ConversationMessageSource.Assistant, fixture, DateTimeOffset.Now));
        await WaitNativeAsync(() => SelectableText(fixture) is not null).ConfigureAwait(true);
        TextBlock body = SelectableText(fixture)!;
        // Preserve all available native clipboard formats in memory; never write its former contents to artifacts.
        var original = NativeClipboard.GetContent(); var restore = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        foreach (string format in original.AvailableFormats) { restore.SetData(format, await original.GetDataAsync(format)); }
        try
        {
            var menu = (Microsoft.UI.Xaml.Controls.MenuFlyout)body.ContextFlyout;
            Invoke((Microsoft.UI.Xaml.Controls.MenuFlyoutItem)menu.Items[1]);
            await WaitClipboardAsync(fixture).ConfigureAwait(true);
            Invoke((Microsoft.UI.Xaml.Controls.MenuFlyoutItem)menu.Items[2]);
            if (body.SelectedText != fixture) { throw new InvalidOperationException("Native Select all changed the visible message."); }
            var end = body.ContentStart.GetPositionAtOffset(5, LogicalDirection.Forward);
            body.Select(body.ContentStart, end); string selected = body.SelectedText;
            if (selected.Length == 0 || selected == fixture) { throw new InvalidOperationException("Native partial text selection is unavailable."); }
            Invoke((Microsoft.UI.Xaml.Controls.MenuFlyoutItem)menu.Items[0]);
            await WaitClipboardAsync(selected).ConfigureAwait(true);
            await Clipboard.Default.SetTextAsync(fixture).ConfigureAwait(true);
            var input = (TextBox)_draft.Handler!.PlatformView!; input.Text = ""; input.Focus(FocusState.Programmatic); input.PasteFromClipboard();
            await WaitNativeAsync(() => _draft.Text?.ReplaceLineEndings("\n") == fixture).ConfigureAwait(true);
            if (_command is not null || !input.AcceptsReturn) { throw new InvalidOperationException("Paste submitted the draft or lost multiline support."); }
            string longPaste = string.Concat(Enumerable.Repeat("Código Ω com múltiplas linhas e Unicode.\n", 1500));
            if (!await ConversationClipboard.CopyAsync(longPaste).ConfigureAwait(true)) { throw new InvalidOperationException("Native long-text fixture could not reach the clipboard."); }
            input.Text = ""; long pasteStart = Stopwatch.GetTimestamp(); input.PasteFromClipboard();
            await WaitNativeAsync(() => _draft.Text?.ReplaceLineEndings("\n") == longPaste).ConfigureAwait(true);
            if (_command is not null) { throw new InvalidOperationException("Long paste submitted itself."); }
            await File.WriteAllTextAsync(Path.Combine(directory, "paste-performance.json"), System.Text.Json.JsonSerializer.Serialize(new { Characters = longPaste.Length, ElapsedMs = Stopwatch.GetElapsedTime(pasteStart).TotalMilliseconds })).ConfigureAwait(true);
        }
        finally
        {
            for (int attempt = 0; ; attempt++)
            {
                try { NativeClipboard.SetContent(restore); break; }
                catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 6) { await Task.Delay(25 << attempt).ConfigureAwait(true); }
            }
            _draft.Text = "";
        }
        for (int index = 0; index < 30; index++) { AddMessage(new(ConversationMessageSource.Assistant, fixture + "\nRow " + index, DateTimeOffset.Now)); }
        var nativeChat = (FrameworkElement)_chat.Handler!.PlatformView!;
        ScrollViewer? viewer = null;
        await WaitNativeAsync(() => (viewer = WindowsConversationScroll.Descendant<ScrollViewer>(nativeChat)) is not null && viewer.ScrollableHeight > 100).ConfigureAwait(true);
        await WaitNativeAsync(() => viewer!.ScrollableHeight - viewer.VerticalOffset <= 4).ConfigureAwait(true);
        _conversationScroll.ObserveManualForSmoke(viewer!.ScrollableHeight); viewer.ChangeView(null, 0, null, true);
        await WaitNativeAsync(() => viewer.VerticalOffset < 4).ConfigureAwait(true);
        AddMessage(new(ConversationMessageSource.Assistant, "New message while reading older content", DateTimeOffset.Now));
        await NextLayoutAsync(nativeChat).ConfigureAwait(true);
        if (viewer.VerticalOffset > 4 || !_returnLatest.IsVisible || _conversationScroll.Following || _unreadCount.Text != "1") { throw new InvalidOperationException("New message interrupted manual reading or lost its unread indicator."); }
        await FollowLatestAsync().ConfigureAwait(true);
        await WaitNativeAsync(() => viewer.ScrollableHeight - viewer.VerticalOffset <= 4).ConfigureAwait(true);
        Guid stream = Guid.NewGuid();
        ReceiveStream(null, new(stream, "Synthetic streaming start", false));
        await WaitNativeAsync(() => _provisional is not null).ConfigureAwait(true);
        ReceiveStream(null, new(stream, string.Join('\n', Enumerable.Repeat("Synthetic streaming growth", 60)), false));
        await WaitNativeAsync(() => _provisional?.Text.Length > 1000 && viewer.ScrollableHeight - viewer.VerticalOffset <= 4).ConfigureAwait(true);
        var memory = _services.GetRequiredService<IConversationMemory>();
        var brain = _services.GetRequiredService<BrainService>();
        var note = await brain.SaveAsync(new BrainNote { Path = "Knowledge/Chat-reset-smoke.md", Title = "Synthetic reset fixture", Body = "Durable synthetic reset fixture." }).ConfigureAwait(true);
        memory.AppendHistory(new(ConversationMessageSource.Assistant, "Old stored conversation", DateTimeOffset.Now)); memory.SetWorkingContext("Old synthetic context");
        var repeated = new ConversationMessage(ConversationMessageSource.Assistant, "Legitimate repeated message with identical timestamp", DateTimeOffset.Now);
        memory.AppendHistory(repeated); memory.AppendHistory(repeated);
        if ((await _conversations.SnapshotAsync().ConfigureAwait(true)).Count(message => message == repeated) != 2) { throw new InvalidOperationException("Snapshot merged away legitimate duplicate messages."); }
        ReceiveStream(null, new(stream, "Queued stale stream before reset", false));
        await ClearConfirmedChatAsync().ConfigureAwait(true);
        await NextLayoutAsync(nativeChat).ConfigureAwait(true);
        if (_messages.Count != 0 || memory.ReadAllHistory().Count != 0 || memory.WorkingUserText.Length != 0 || _provisional is not null || _live.IsListening)
        { throw new InvalidOperationException("Reset retained conversation context or stale streaming."); }
        if (!brain.Snapshot.Notes.Any(item => item.Id == note.Id && item.Hash == note.Hash)) { throw new InvalidOperationException("Transcript reset changed durable Brain knowledge."); }
        PreviewConversationForSmoke();
        await File.WriteAllTextAsync(Path.Combine(directory, "chat-contracts.txt"), "Native TextBlock partial selection and full-message context actions copied exact Unicode/code/newlines. Native TextBox pasted multiline text without submitting. Real ScrollViewer followed layout growth, kept an emulated manual position and resumed on return-to-latest. Queued synthetic stream was invalidated by actual provider teardown/engine reset; persisted history/working context cleared and Markdown preserved. Physical mouse/keyboard input and the OS save picker remain manual checks.").ConfigureAwait(true);
    }
    private TextBlock? SelectableText(string text) => _chat.Handler?.PlatformView is DependencyObject native ? TextBlocks(native).FirstOrDefault(block => block.IsTextSelectionEnabled && block.Text == text) : null;
    private static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
    {
        if (root is TextBlock text) { yield return text; }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { foreach (TextBlock child in TextBlocks(VisualTreeHelper.GetChild(root, index))) { yield return child; } }
    }
    private static void Invoke(Microsoft.UI.Xaml.Controls.MenuFlyoutItem item)
    {
        var peer = new MenuFlyoutItemAutomationPeer(item);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static async Task WaitNativeAsync(Func<bool> condition)
    {
        long start = Stopwatch.GetTimestamp();
        while (!condition())
        { if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(8)) { throw new TimeoutException("Native chat condition did not settle."); } await Task.Delay(25).ConfigureAwait(true); }
    }
    private static async Task WaitClipboardAsync(string expected)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(8))
        {
            try { if (await Clipboard.Default.GetTextAsync().ConfigureAwait(true) == expected) { return; } }
            catch (System.Runtime.InteropServices.COMException error) when (error.HResult == unchecked((int)0x800401D0)) { }
            await Task.Delay(25).ConfigureAwait(true);
        }
        throw new TimeoutException("Native clipboard action did not settle.");
    }
    private static async Task NextLayoutAsync(FrameworkElement element)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Layout(object? sender, object args) { ready.TrySetResult(); }
        element.LayoutUpdated += Layout;
        try { element.InvalidateArrange(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true); }
        finally { element.LayoutUpdated -= Layout; }
    }
}
