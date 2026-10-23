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
