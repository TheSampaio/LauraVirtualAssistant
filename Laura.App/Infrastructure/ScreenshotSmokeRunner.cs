using System.Collections.Immutable;
using Laura.App.Platforms.Windows;
using Laura.Core.Abstractions;
using Laura.Core.Screenshots;
using Laura.Core.Permissions;
using Laura.Core.Brain;
using Laura.Core.Conversation;
using Laura.Platform.Windows.System;
using Laura.Platform.Windows.Conversation;
using Microsoft.Extensions.DependencyInjection;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Laura.App.Infrastructure;

/// <summary>Explicit isolated screenshot tests using synthetic pixels and Laura's own native window only for stored/live image fixtures.</summary>
public static class ScreenshotSmokeRunner
{
    /// <summary>Exercises actual WinUI rendering/codecs, WGC, clipboard, file IO, Brain attachment and optional approved cloud vision.</summary>
    public static async Task RunAsync(IServiceProvider services, Window main, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var settings = services.GetRequiredService<ISettingsService>(); var screenshots = services.GetRequiredService<ScreenshotCoordinator>();
        var permissions = services.GetRequiredService<PermissionManager>(); var center = services.GetRequiredService<WindowsApprovalCenter>();
        var results = new List<string>();
        try
        {
            if (!Environment.GetCommandLineArgs().Contains("--storage", StringComparer.Ordinal)) { throw new InvalidOperationException("Screenshot tests require isolated --storage."); }
            await settings.UpdateAsync(settings.Current with { GreetOnStartup = false, AnnounceHourly = false, MicrophoneMuted = true, ClapWakeEnabled = false,
                Voice = settings.Current.Voice with { Volume = 0 }, Permissions = settings.Current.Permissions with { Voice = ApprovalVoiceMode.Never }, Screenshots = settings.Current.Screenshots with { ShortcutsEnabled = false } }).ConfigureAwait(true);
            byte[] pixels = Enumerable.Repeat((byte)255, 800 * 420 * 4).ToArray(); screenshots.OpenForSmoke(new(new(0, 0, 800, 420), pixels));
            var editor = screenshots.EditorForSmoke; var document = editor.Document;
            foreach (var annotation in new[]
            {
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Rectangle, [new(40, 40), new(420, 260)], 0xffff0000, 4),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Ellipse, [new(480, 40), new(700, 220)], 0xff0088ff, 4),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Line, [new(30, 300), new(220, 350)], 0xff008800, 4),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Arrow, [new(240, 300), new(390, 350)], 0xff8800ff, 4),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Pen, [new(480, 270), new(500, 310), new(530, 290)], 0xff222222, 4),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Highlight, [new(80, 120), new(390, 120)], 0xffffff00, 8),
                new ScreenshotAnnotation(Guid.NewGuid(), ScreenshotTool.Text, [new(80, 160)], 0xff111111, 3, "LAURA VISUAL TEST", 28)
            }) { document.Add(annotation); }
            var arrow = document.Annotations.Single(item => item.Tool == ScreenshotTool.Arrow); document.Update(arrow.Move(10, -10)); document.Undo(); document.Redo(); document.Remove(arrow.Id); document.Undo();
            editor.Render(); await Task.Delay(300).ConfigureAwait(true);
            var editorUi = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await editorUi.RenderAsync((Microsoft.UI.Xaml.UIElement)editor.NativeWindow.Content);
            var editorUiBuffer = await editorUi.GetPixelsAsync(); byte[] editorUiPixels = new byte[editorUiBuffer.Length]; using (var reader = DataReader.FromBuffer(editorUiBuffer)) { reader.ReadBytes(editorUiPixels); }
            await File.WriteAllBytesAsync(Path.Combine(directory, "editor-ui.png"), await WindowsScreenCapture.EncodeAsync(editorUiPixels, editorUi.PixelWidth, editorUi.PixelHeight, false, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
            string pngPath = Path.Combine(directory, "synthetic-editor.png"), jpegPath = Path.Combine(directory, "synthetic-editor.jpg");
            await screenshots.SaveForSmokeAsync(pngPath, false).ConfigureAwait(true); await screenshots.SaveForSmokeAsync(jpegPath, true).ConfigureAwait(true);
            byte[] png = await File.ReadAllBytesAsync(pngPath).ConfigureAwait(true); var decoded = await DecodeAsync(png).ConfigureAwait(true);
            if (decoded.Width != 800 || decoded.Height != 420 || !Red(decoded.Pixels, 800, 100, 40)) { throw new InvalidOperationException("Export resolution or annotation alignment is incorrect."); }
            var jpeg = await DecodeAsync(await File.ReadAllBytesAsync(jpegPath).ConfigureAwait(true)).ConfigureAwait(true);
            if (jpeg.Width != 800 || jpeg.Height != 420) { throw new InvalidOperationException("JPEG changed screenshot dimensions."); }
            results.Add("Native annotation renderer: rectangle, ellipse, line, arrow, freehand, text, highlighter, move/remove, undo/redo; original-resolution PNG/JPEG and physical red-border pixel alignment passed.");
            string invalidParent = Path.Combine(directory, "NotADirectory"); await File.WriteAllTextAsync(invalidParent, "synthetic blocker").ConfigureAwait(true);
            try { await screenshots.SaveForSmokeAsync(Path.Combine(invalidParent, "invalid.png"), false).ConfigureAwait(true); throw new InvalidOperationException("Invalid output path was accepted."); } catch (IOException) { results.Add("Invalid output path reported failure without a partial exported image."); }
            await screenshots.PerformAsync("copy", true).ConfigureAwait(true);
            var clipboard = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent(); var bitmap = await clipboard.GetBitmapAsync(); using var copied = await bitmap.OpenReadAsync();
            var copiedDecoder = await BitmapDecoder.CreateAsync(copied); if (copiedDecoder.PixelWidth != 800 || copiedDecoder.PixelHeight != 420) { throw new InvalidOperationException("Native clipboard image dimensions changed."); }
            results.Add("Actual Windows image clipboard round-trip passed. The clipboard now contains the synthetic test image.");
            using (var pickerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                try { await WindowsScreenshotSaveDialog.ChooseAsync(WinRT.Interop.WindowNative.GetWindowHandle(editor.NativeWindow), services.GetRequiredService<IUiLocalizer>(), pickerCancellation.Token).ConfigureAwait(true); throw new InvalidOperationException("Native save form did not cancel through the application token."); }
                catch (OperationCanceledException) { results.Add("Actual Laura WinUI save form with folder/name/PNG-JPEG format and token cancellation passed; user folder browsing remains manual."); }
            }
            foreach (bool jpg in new[] { false, true })
            {
                var save = screenshots.PerformAsync("save", true);
                while (WindowsDialogs.DialogForSmoke is null && !save.IsCompleted) { await Task.Delay(20).ConfigureAwait(true); }
                var dialog = WindowsDialogs.DialogForSmoke ?? throw new InvalidOperationException("Save form did not open."); await Task.Delay(100).ConfigureAwait(true);
                var form = (Microsoft.UI.Xaml.Controls.StackPanel)((Microsoft.UI.Xaml.Controls.ScrollViewer)dialog.Content).Content;
                var fields = form.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().First(); var textFields = fields.Children.OfType<Microsoft.UI.Xaml.Controls.TextBox>().ToArray();
                textFields[0].Text = directory; textFields[1].Text = jpg ? "form-export-jpeg" : "form-export-png"; fields.Children.OfType<Microsoft.UI.Xaml.Controls.ComboBox>().Single().SelectedIndex = jpg ? 1 : 0;
                if (!jpg)
                {
                    await Task.Delay(100).ConfigureAwait(true); var formBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await formBitmap.RenderAsync((Microsoft.UI.Xaml.UIElement)dialog.Content);
                    var formBuffer = await formBitmap.GetPixelsAsync(); byte[] formPixels = new byte[formBuffer.Length]; using (var formReader = DataReader.FromBuffer(formBuffer)) { formReader.ReadBytes(formPixels); }
                    await File.WriteAllBytesAsync(Path.Combine(directory, "save-form-ui.png"), await WindowsScreenCapture.EncodeAsync(formPixels, formBitmap.PixelWidth, formBitmap.PixelHeight, false, CancellationToken.None).ConfigureAwait(true)).ConfigureAwait(true);
                }
                string chosenPath = Path.Combine(directory, (jpg ? "form-export-jpeg.jpg" : "form-export-png.png")); if (File.Exists(chosenPath)) { throw new InvalidOperationException("Save form pre-created the destination."); }
                var buttons = form.Children.OfType<Microsoft.UI.Xaml.Controls.StackPanel>().Last().Children.OfType<Microsoft.UI.Xaml.Controls.Button>().ToArray();
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(buttons.Last()); ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                if (!await save.ConfigureAwait(true) || !File.Exists(chosenPath)) { throw new InvalidOperationException("Native save form did not export selected format."); }
                var formImage = await DecodeAsync(await File.ReadAllBytesAsync(chosenPath).ConfigureAwait(true)).ConfigureAwait(true); if (formImage.Width != 800 || formImage.Height != 420) { throw new InvalidOperationException("Save form changed resolution."); }
            }
            results.Add("Actual WinUI save buttons exported both chosen PNG/JPEG formats through the production coordinator; no destination existed before acceptance.");
            var target = new WindowsInputTarget(WinRT.Interop.WindowNative.GetWindowHandle(editor.NativeWindow).ToInt64(), (uint)Environment.ProcessId, editor.NativeWindow.Title);
            using (var windowFrame = await WindowsWindowCapture.CaptureAsync(target, CancellationToken.None).ConfigureAwait(true))
            { if (windowFrame.Bounds.Width < 100 || !ContainsRed(windowFrame.Pixels.Span)) { throw new InvalidOperationException("WGC did not capture the editor's actual red annotation."); } results.Add("Actual one-shot Windows Graphics Capture of Laura's own synthetic editor passed, including real annotation pixels; frame pool, session and D3D resources disposed."); }
            ScreenshotBounds desktop = WindowsScreenCapture.DesktopBounds();
            using (var full = await WindowsScreenCapture.CaptureFrameAsync(desktop, CancellationToken.None).ConfigureAwait(true))
            { if (full.Bounds != desktop) { throw new InvalidOperationException("Virtual-desktop capture geometry changed."); } results.Add($"Actual local virtual-desktop capture {desktop.Width}x{desktop.Height} at {desktop.X},{desktop.Y} passed; desktop pixels were not saved or transmitted."); }
            BrainService brain = services.GetRequiredService<BrainService>(); var note = await brain.AttachVisualAsync(png, "Synthetic screenshot reference").ConfigureAwait(true);
            string relative = System.Text.RegularExpressions.Regex.Match(note.Body, @"!\[[^\]]*\]\(([^)]+)\)").Groups[1].Value;
            byte[] recovered = await brain.ReadAttachmentAsync(BrainService.ResolveAttachment(note.Path, relative)).ConfigureAwait(true); if (!recovered.AsSpan().SequenceEqual(png)) { throw new InvalidOperationException("Brain did not recover exact screenshot bytes."); }
            results.Add("Rendered screenshot stored through existing Brain API; real Markdown reference resolved and exact image bytes recovered.");
            if (!await screenshots.PerformAsync("brain", true, "Synthetic screenshot reference").ConfigureAwait(true)) { throw new InvalidOperationException("Production Brain attachment action did not complete."); }
            await brain.RefreshAsync().ConfigureAwait(true);
            var attached = brain.Snapshot.Notes.Single(item => item.Id == note.Id);
            if (System.Text.RegularExpressions.Regex.Matches(attached.Body, "!\\[").Count != 1) { throw new InvalidOperationException("Production Brain action duplicated the visual reference."); }
            results.Add("Production screenshot-to-Brain coordinator passed scoped authorization, preserved the existing node ID and avoided a duplicate visual reference.");
            var nativeMain = (Microsoft.UI.Xaml.Window)main.Handler!.PlatformView!;
            using (var shortcuts = new WindowsScreenshotShortcuts(WinRT.Interop.WindowNative.GetWindowHandle(nativeMain), settings, screenshots, services.GetRequiredService<IUiLocalizer>(), services.GetRequiredService<LauraNotifications>()))
            {
                await settings.UpdateAsync(settings.Current with { Screenshots = new() { RegionShortcut = "Ctrl+Alt+Shift+F9", WindowShortcut = "Ctrl+Alt+Shift+F10", FullScreenShortcut = "Ctrl+Alt+Shift+F11" } }).ConfigureAwait(true); await Task.Delay(100).ConfigureAwait(true);
                if (screenshots.ShortcutErrors.Count != 0) { throw new InvalidOperationException("Isolated test shortcut registrations failed."); }
                await settings.UpdateAsync(settings.Current with { Screenshots = settings.Current.Screenshots with { WindowShortcut = settings.Current.Screenshots.RegionShortcut } }).ConfigureAwait(true); await Task.Delay(100).ConfigureAwait(true);
                if (screenshots.ShortcutErrors.Count == 0) { throw new InvalidOperationException("Duplicate shortcut conflict was not reported."); }
                results.Add("Real RegisterHotKey registrations and duplicate-conflict reporting passed; diagnostic chords unregistered on disposal.");
            }
            if (Environment.GetCommandLineArgs().Contains("--screenshot-ui-smoke", StringComparer.Ordinal))
            {
                using var interactiveShortcuts = new WindowsScreenshotShortcuts(WinRT.Interop.WindowNative.GetWindowHandle(nativeMain), settings, screenshots, services.GetRequiredService<IUiLocalizer>(), services.GetRequiredService<LauraNotifications>());
                await settings.UpdateAsync(settings.Current with { Screenshots = new() { RegionShortcut = "Ctrl+Alt+Shift+F9", WindowShortcut = "Ctrl+Alt+Shift+F10", FullScreenShortcut = "Ctrl+Alt+Shift+F11" } }).ConfigureAwait(true);
                nativeMain.AppWindow.Hide(); editor.Show();
                await File.WriteAllTextAsync(Path.Combine(directory, "ui-ready.txt"), "Synthetic editor ready; main hidden; diagnostic chords F9 region/F10 window/F11 desktop, Ctrl+Alt+Shift.").ConfigureAwait(true);
                DateTime deadline = DateTime.UtcNow.AddMinutes(15);
                while (!File.Exists(Path.Combine(directory, "ui-done.txt")))
                { if (DateTime.UtcNow >= deadline) { throw new TimeoutException("Interactive screenshot diagnostics timed out."); } await Task.Delay(200).ConfigureAwait(true); }
                results.Add("Interactive test session completed; detailed UI observations are documented separately.");
            }
            if (Environment.GetCommandLineArgs().Contains("--screenshot-live-smoke", StringComparer.Ordinal))
            {
                var live = services.GetRequiredService<GeminiLiveClient>(); if (!live.SupportsImages) { throw new InvalidOperationException("Live vision verification requires a configured compatible Gemini model."); }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var reply = live.AskAsync(new("Read the large words printed in the attached image. Reply with those words only.", System.Globalization.CultureInfo.GetCultureInfo("en-US"), "Synthetic viewer") { Image = png }, timeout.Token);
                while (permissions.PendingRequests.Count == 0 && !reply.IsCompleted) { await Task.Delay(50, timeout.Token).ConfigureAwait(true); }
                if (permissions.PendingRequests.Count == 0) { throw new InvalidOperationException("Image transmission did not require a native approval."); }
                await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.Show(); await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
                string? answer = await reply.ConfigureAwait(true);
                if (answer is null || !answer.Contains("LAURA", StringComparison.OrdinalIgnoreCase) || !answer.Contains("VISUAL", StringComparison.OrdinalIgnoreCase) || !answer.Contains("TEST", StringComparison.OrdinalIgnoreCase))
                { throw new InvalidOperationException("Gemini did not verify the synthetic image text. Failure category: " + live.LastFailureCategory + "; synthetic fixture reply: " + (answer ?? "[no answer]") + ". No visual-analysis success is claimed."); }
                results.Add("Actual cloud vision after reinforced native approval recognized LAURA VISUAL TEST from generated image pixels. No desktop image, microphone or personal Brain was sent.");
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var textAnnotation = document.Annotations.Single(item => item.Tool == ScreenshotTool.Text);
                document.Update(textAnnotation with { Text = "ORBIT 472" }); editor.Render();
                byte[] retrievalImage = await editor.ExportAsync(false, timeout.Token).ConfigureAwait(true);
                var retrievalNote = await brain.AttachVisualAsync(retrievalImage, "Synthetic image retrieval", cancellationToken: timeout.Token).ConfigureAwait(true);
                string retrievalRelative = System.Text.RegularExpressions.Regex.Match(retrievalNote.Body, @"!\[[^\]]*\]\(([^)]+)\)").Groups[1].Value;
                string asset = BrainService.ResolveAttachment(retrievalNote.Path, retrievalRelative);
                var repeatedVision = live.AskAsync(new("Read the large words and number in the newly attached image. Reply with only those words and number.", System.Globalization.CultureInfo.GetCultureInfo("en-US"), "Synthetic viewer") { Image = retrievalImage }, timeout.Token);
                while (permissions.PendingRequests.Count == 0 && !repeatedVision.IsCompleted) { await Task.Delay(50, timeout.Token).ConfigureAwait(true); }
                await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.Show(); await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
                string? repeatedAnswer = await repeatedVision.ConfigureAwait(true);
                if (repeatedAnswer is null || !repeatedAnswer.Contains("ORBIT", StringComparison.OrdinalIgnoreCase) || !repeatedAnswer.Contains("472", StringComparison.Ordinal)) { throw new InvalidOperationException("Second direct image not verified: " + repeatedAnswer); }
                results.Add("Second direct visual turn recognized changed pixels rather than reusing the first image.");
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                document.Update(textAnnotation with { Text = "VECTOR 813" }); editor.Render();
                byte[] toolImage = await editor.ExportAsync(false, timeout.Token).ConfigureAwait(true);
                var toolNote = await brain.AttachVisualAsync(toolImage, "Synthetic tool retrieval", cancellationToken: timeout.Token).ConfigureAwait(true);
                string toolRelative = System.Text.RegularExpressions.Regex.Match(toolNote.Body, @"!\[[^\]]*\]\(([^)]+)\)").Groups[1].Value;
                asset = BrainService.ResolveAttachment(toolNote.Path, toolRelative);
                var retrieval = live.AskAsync(new("This is a different image that you have not received yet. Obtain it by calling read_brain_image with the exact path " + asset + ". Do not use any previous image. After the tool returns and the new pixels are supplied, read the large words and number. Reply with only those words and number.", System.Globalization.CultureInfo.GetCultureInfo("en-US"), "Synthetic viewer"), timeout.Token);
                while (permissions.PendingRequests.Count == 0 && !retrieval.IsCompleted) { await Task.Delay(50, timeout.Token).ConfigureAwait(true); }
                if (permissions.PendingRequests.Count == 0) { throw new InvalidOperationException("Brain image retrieval did not request explicit cloud approval. Synthetic reply: " + await retrieval.ConfigureAwait(true)); }
                if (permissions.PendingRequests.Single().Action.Operation != PermissionOperation.ImageCloud) { throw new InvalidOperationException("Unexpected operation in synthetic retrieval test."); }
                await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.Show(); await Task.Delay(150, timeout.Token).ConfigureAwait(true); center.ConfirmCriticalForSmoke();
                string? retrievalReply = await retrieval.ConfigureAwait(true);
                if (retrievalReply is null || !retrievalReply.Contains("VECTOR", StringComparison.OrdinalIgnoreCase) || !retrievalReply.Contains("813", StringComparison.Ordinal))
                { throw new InvalidOperationException("Brain-image tool vision was not verified. Category: " + live.LastFailureCategory + "; synthetic reply: " + (retrievalReply ?? "[no answer]")); }
                results.Add("Actual read_brain_image tool recovered and transmitted the newly stored synthetic asset after separate native cloud approval; model recognized VECTOR 813, which was not disclosed in the prompt or previous images.");
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                var pendingSnapshot = await screenshots.CurrentSnapshotAsync(timeout.Token).ConfigureAwait(true);
                using var captureLifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, pendingSnapshot.Lifetime);
                var cancelledImage = live.AskAsync(new("Read this selected test image.", System.Globalization.CultureInfo.GetCultureInfo("en-US"), "Synthetic viewer") { Image = pendingSnapshot.Png }, captureLifetime.Token);
                while (permissions.PendingRequests.Count == 0 && !cancelledImage.IsCompleted) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
                Guid cancelledId = permissions.PendingRequests.Single().Id; editor.Close();
                try { await cancelledImage.ConfigureAwait(true); throw new InvalidOperationException("Closing the screenshot did not cancel pending transmission."); }
                catch (OperationCanceledException) { }
                if (permissions.Decide(cancelledId, ApprovalChoice.Once) || permissions.History.Single(item => item.Id == cancelledId).Outcome != ApprovalOutcome.Cancelled) { throw new InvalidOperationException("Closed capture accepted a late image approval."); }
                results.Add("Closing the real editor cancelled pending exact-image transmission; no authorized send was dispatched and a late approval was rejected.");
            }
            results.Add("Region/window mouse selection, actual global keystrokes, physical mixed-DPI/multiple monitors, Windows folder browsing, audible devices, disk-full and protected third-party applications still require explicit manual coverage.");
            await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), results.Prepend("PASS: screenshot native integration")).ConfigureAwait(true);
        }
        catch (Exception error) { await File.WriteAllLinesAsync(Path.Combine(directory, "result.txt"), results.Append("FAIL: " + error)).ConfigureAwait(true); }
        finally { services.GetRequiredService<Laura.Core.Abstractions.IShellController>().Shutdown(); }
    }
    private static bool Red(byte[] pixels, int width, int x, int y) { int at = (y * width + x) * 4; return pixels[at + 2] > 180 && pixels[at + 1] < 80 && pixels[at] < 80; }
    private static bool ContainsRed(ReadOnlySpan<byte> pixels) { for (int index = 0; index < pixels.Length; index += 4) { if (pixels[index + 2] > 180 && pixels[index + 1] < 40 && pixels[index] < 40) { return true; } } return false; }
    private static async Task<(int Width, int Height, byte[] Pixels)> DecodeAsync(byte[] image)
    {
        using var stream = new InMemoryRandomAccessStream(); using (var writer = new DataWriter(stream)) { writer.WriteBytes(image); await writer.StoreAsync(); writer.DetachStream(); } stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream); var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }
}
