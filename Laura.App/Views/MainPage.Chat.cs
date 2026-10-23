using System.Runtime.InteropServices;
using Laura.App.Infrastructure;
using Laura.App.Platforms.Windows;
using Laura.App.Theming;
using Laura.Core.Abstractions;
using Laura.Core.Conversation;
using Laura.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;

namespace Laura.App.Views;

public sealed partial class MainPage
{
    private readonly ConversationActions _conversations;
    private readonly WindowsConversationScroll _conversationScroll;
    private readonly ImageButton _returnLatest, _chatActionsButton;
    private readonly Microsoft.Maui.Controls.Grid _returnLatestHost = new() { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = 12, IsVisible = false };
    private readonly Label _unreadCount = Theme.Label("", 10, Colors.White);
    private readonly Label _chatNotice = Theme.Label("", 11, Theme.Muted);
    private bool _clearing;
    private readonly object _streamGate = new();
    private ConversationStreamUpdate? _pendingStream;
    private bool _streamQueued;
    private ConversationMessage? _provisional;
    private Guid _streamTurn;
    private void StreamInterrupted(object? sender, EventArgs args)
    {
        long version = _conversations.Version;
        lock (_streamGate) { _pendingStream = null; }
        MainThread.BeginInvokeOnMainThread(() => { if (_conversations.Version == version) { RemoveProvisional(); } });
    }
    private void ReceiveStream(object? sender, ConversationStreamUpdate update)
    {
        if (_conversations.IsResetting) { return; }
        long version = _conversations.Version;
        lock (_streamGate) { _pendingStream = update; if (_streamQueued) { return; } _streamQueued = true; }
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Limit native text/layout updates while retaining the newest provider delta.
            await Task.Delay(80).ConfigureAwait(true);
            ConversationStreamUpdate? latest;
            lock (_streamGate) { latest = _pendingStream; _pendingStream = null; _streamQueued = false; }
            if (latest is null || _conversations.IsResetting || _conversations.Version != version || _clearing) { return; }
            if (latest.Text.Length == 0) { RemoveProvisional(); return; }
            if (_streamTurn != latest.TurnId) { RemoveProvisional(); _streamTurn = latest.TurnId; }
            var next = new ConversationMessage(ConversationMessageSource.Assistant, latest.Text, _provisional?.Timestamp ?? DateTimeOffset.Now);
            int index = _provisional is null ? -1 : _messages.IndexOf(_provisional);
            if (index >= 0) { _messages[index] = next; } else { _messages.Add(next); if (_messages.Count > 200) { _messages.RemoveAt(0); } _conversationScroll.MessageArrived(); }
            _provisional = next; _emptyState.IsVisible = false; RequestConversationScroll();
        });
    }
    private void RemoveProvisional() { if (_provisional is not null) { _messages.Remove(_provisional); _provisional = null; } }
    private Task FollowLatestAsync() { _conversationScroll.Follow(); return Task.CompletedTask; }
    private void ReceiveMessage(object? sender, ConversationMessage message)
    {
        if (_conversations.IsResetting) { return; }
        long version = _conversations.Version; _conversations.Observe(message);
        MainThread.BeginInvokeOnMainThread(() => { if (!_conversations.IsResetting && _conversations.Version == version) { AddMessage(message); } });
    }
    private void AttachConversationScroll()
    { if (_chat.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native) { _conversationScroll.Attach(native); } }
    private void ConfigureComposer()
    {
        if (_draft.Handler?.PlatformView is not TextBox native) { return; }
        native.BorderThickness = new Microsoft.UI.Xaml.Thickness(0); native.AcceptsReturn = true; native.MaxLength = 1_000_000;
        native.Resources["TextControlBorderBrushFocused"] = WindowsDialogs.Brush(Theme.Accent);
        native.Resources["TextControlBorderBrushPointerOver"] = WindowsDialogs.Brush(Theme.Line);
        native.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap;
        native.PreviewKeyDown -= ComposerKeyDown; native.PreviewKeyDown += ComposerKeyDown;
    }
    private async void ComposerKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key != global::Windows.System.VirtualKey.Enter || (GetKeyState(0x10) & 0x8000) != 0) { return; }
        args.Handled = true;
        try { await SendAsync().ConfigureAwait(true); }
        catch (Exception error) when (error is InvalidOperationException or IOException) { Notice(_ui.Get("ui.chat.error")); }
    }
    private Task ChatActionsAsync()
    {
        var menu = new MenuFlyout();
        Add("ui.chat.clear", ClearChatAsync); Add("ui.chat.export", ExportChatAsync); Add("ui.chat.diagnostics", ExportDiagnosticsAsync);
        Add("ui.logs.title", async () => await Navigation.PushAsync(_services.GetRequiredService<LogsPage>()).ConfigureAwait(true));
        if (_chatActionsButton.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement native) { menu.ShowAt(native); }
        return Task.CompletedTask;
        void Add(string key, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = _ui.Get(key) };
            item.Click += async (_, _) =>
            {
                _chatActionsButton.IsEnabled = false;
                try { await action().ConfigureAwait(true); }
                catch (OperationCanceledException) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or System.Text.RegularExpressions.RegexMatchTimeoutException)
                { LauraDiagnostics.Record("chat.action_failed", outcome: error.GetType().Name); Notice(_ui.Get("ui.chat.error")); }
                finally { _chatActionsButton.IsEnabled = true; }
            };
            menu.Items.Add(item);
        }
    }
    private async Task ClearChatAsync()
    {
        if (!await WindowsDialogs.ConfirmAsync(_ui.Get("ui.chat.clearTitle"), _ui.Get("ui.chat.clearHelp") + "\n\n" + _ui.Get("ui.chat.clearActive"), _ui.Get("ui.chat.clearAccept"), _ui.Get("ui.chat.clearCancel")).ConfigureAwait(true)) { return; }
        await ClearConfirmedChatAsync().ConfigureAwait(true);
    }
    private async Task ClearConfirmedChatAsync()
    {
        _clearing = true; UpdateSendAction();
        try
        {
            _command?.Cancel(); _holdLifetime?.Cancel();
            await _conversations.ClearAsync().ConfigureAwait(true);
            lock (_streamGate) { _pendingStream = null; }
            RemoveProvisional();
            _messages.Clear(); _emptyState.IsVisible = true; _conversationScroll.Follow(); Notice(_ui.Get("ui.chat.cleared"));
        }
        finally { _clearing = false; UpdateSendAction(); Reload(); }
    }
    private nint OwnerHandle => WinRT.Interop.WindowNative.GetWindowHandle((Microsoft.UI.Xaml.Window)Window.Handler!.PlatformView!);
    private async Task ExportChatAsync()
    { if (await WindowsDocumentExport.ConversationAsync(OwnerHandle, _conversations, _services.GetRequiredService<IApiKeyStore>()).ConfigureAwait(true)) { Notice(_ui.Get("ui.chat.exported")); } }
    private async Task ExportDiagnosticsAsync()
    { if (await WindowsDocumentExport.DiagnosticsAsync(OwnerHandle, _services.GetRequiredService<RuntimeJournal>()).ConfigureAwait(true)) { Notice(_ui.Get("ui.chat.diagnosticsSaved")); } }
    private void Notice(string text) { _chatNotice.Text = text; _chatNotice.IsVisible = text.Length > 0; }
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
}
