# LAURA Screenshot

> **Laura 2.2 runtime notice (2026-10-20):** Live/audio behavior and measurements below describe historical implementations. Current operation uses local Whisper/Kokoro, supervised local inference and optional text providers. See [current architecture](laura-local-first.md) and [implementation evidence](../../LAURA-2.2-MIGRATION.md). Historical results are not new acceptance measurements.

Native Windows capture and editable visual references, built after the [permissions gate](laura-permissions.md#gate-executed-on-2026-10-13). The existing .NET 10 MAUI/WinUI, settings, Gemini Live conversation, Brain storage and icon pipeline are reused. No package or separate application was added.

## Capture and lifetime

`ScreenshotCoordinator` owns one local editing session. The HUD scan action and registered shortcuts are explicit native gestures; assistant tool requests cannot manufacture this trusted marker. Both paths enter `PermissionManager` at the actual capture/export adapter. Capture does not automatically send an image, create a disk file or add a memory.

Region selection uses a frozen BGRA virtual-desktop frame, a temporary borderless WinUI overlay, physical cursor coordinates and explicit Enter/Esc actions. It shows pixel dimensions, allows dragging, moving and resizing from the lower-right corner, and rejects changed display geometry. The overlay is temporarily topmost during selection; the editor and Approval Center are ordinary independent windows. Negative desktop origins are retained in crop calculations. Full-desktop capture can include all displays or the active window's monitor.

`WindowsScreenCapture` extends the existing GDI capture and WinRT codecs. Window selection enumerates visible native windows under the cursor and uses DWM extended bounds for the outline. `WindowsWindowCapture` captures the selected HWND/PID with a one-shot [Windows Graphics Capture frame pool](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded) and [CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow). D3D11 supports hardware/WARP; frame/session/device/bitmap/COM references are released. Window identity, capture affinity, dimensions and availability are checked before/after capture. No PrintWindow fallback bypasses protected windows. A five-second frame timeout and cancellation prevent an indefinite wait.

Frames are limited to 32 million pixels; encoded attachments are limited to 32 MiB. Very large virtual desktops fail with a visible error rather than unbounded allocation. HDR conversion and physical mixed-DPI display changes still need manual coverage. The process never continuously monitors the screen.

## Editor, export and shortcuts

`ScreenshotDocument` retains the original pixels and immutable image-coordinate annotations. Tools include selection, rectangle, ellipse, line, arrow, freehand drawing, text, highlighter and annotation removal. Selected annotations can move and resize from their lower-right handle. Hit testing uses annotation bounds; it is deliberately coarse for lines and freehand strokes. The session has a maximum of 1,000 annotations, 4,096 points per stroke and 100 undo/redo steps. A new edit invalidates redo.

The native editor reuses the dark/red palette and SVG-to-PNG resource pipeline. A compact horizontal toolbar has accessible names/tooltips, a native color picker and thickness selector. The image scales through a Viewbox while annotations retain original coordinates. Export hides selection handles and renders at the image's original resolution. PNG/JPEG encodings up to 32 MiB each are reused for the same immutable document state, so repeated actions do not introduce raster/encoding drift or duplicate Brain references. Annotation changes invalidate them; closing clears the private encoded buffers. Editing is frozen during export/actions; canceled/lost pointer capture discards the unfinished stroke. Closing the editor cancels pending image transmission.

- **Copy / Ctrl+C:** PNG bitmap on the actual Windows clipboard; bounded retries for temporary clipboard contention.
- **Save / Ctrl+S:** an asynchronous Laura WinUI form selects folder, name and PNG/JPEG format, with optional Windows FolderPicker browsing. Invalid destinations remain in the form; replacing an existing image requires confirmation. No destination is created/truncated before acceptance and authorization. The encoded bytes/path are bound to the action. A flushed sidecar is atomically moved into place; failed writes remove the sidecar. Local filesystem redirect/system/security guards are revalidated. Generic UNC/mapped-network exports are conservatively unsupported. The prototype COM save dialog was removed after native cancellation tests exposed a modal lifetime failure; the established asynchronous dialog host avoids that dependency.
- **Analyze with LAURA:** asks a question and submits a visual command through the existing assistant queue and conversation UI.
- **Save in Brain:** asks for an explicit topic and uses the existing Brain service.
- **Cancel / Esc:** releases the current local session without automatically saving it.

`WindowsScreenshotShortcuts` uses `RegisterHotKey` on the existing native application handle. Defaults are PrtSc / Ctrl+PrtSc / Shift+PrtSc. Preferences allow modifiers with letters, F1–F12 or PrtSc. Registration/duplicate failures are displayed in preferences and optionally notified; unavailable chords are never silently replaced. Registrations work independently of primary-window visibility and are removed on reconfiguration/shutdown. Actual key dispatch still requires interactive verification on this Windows desktop.

## Model and privacy integration

`SubmitVisualCommandAsync`, `SkillRequest.Image` and `ConversationTurn.Image` extend the existing flow. A command clones its exact PNG before queueing; chat persistence contains the question/answer, not binary screenshots. The current application has Gemini Live only; a local vision provider was not invented. Known primary/fallback model identifiers have reviewed image capability; unknown overrides fail closed with a clear message. The fallback identifier is supported by policy but was not separately exercised by the native fixture.

The Live adapter sends text and inline image data in the same user turn, as supported by the [Live protocol](https://ai.google.dev/api/live). Separate realtime video, function-response image parts and interleaved visual user turns proved unreliable for these discrete tool images. A tool now prepares its local image and completes its matching function result; the normal conversation then runs an explicitly authorized visual follow-up with the exact pixels and original question. The intermediate result cannot claim visual findings. Continuations are bounded to three, canceled with the originating editor and released on shutdown. Continuous voice uses the same follow-up adapter and can resume the configured listening mode after success. This also serves the existing camera/screen tool instead of creating another model integration.

Every image disclosure is critical and requires a native, reinforced one-time decision, including in Automatic Approval. The approval binds provider destination, model/format where known, image digest or a content-addressed Brain asset, and origin. A native local capture/save/copy gesture cannot authorize cloud disclosure. Tool-generated risk/permission fields and image/Markdown instructions cannot alter policies. No screenshot is uploaded merely because it was captured or edited. A synchronized Brain directory selected by the user can be synchronized by its own software; an explicit network vault attachment is classified as critical.

The existing `screenshot_control` tool exposes region/window/fullscreen/copy/save/analyze/brain/cancel to normal Gemini intent recognition, including voice. `read_brain_image` accepts only the actual `Assets/Screenshots/<sha256>.png` reference, recovers the file locally and verifies its content hash. The shared visual sender requests disclosure approval around the actual network send, with that recovered image's exact digest. Microphone commands cannot approve permissions. The screenshot tool does not add another command parser or unauthenticated voice authorizer.

## Brain integration

`BrainService.AttachVisualAsync` matches a real node by ID, exact title or alias. Ambiguity prompts explicit selection; superficial word similarity cannot invent a relationship. Without a matching node it creates a normal `References` note using existing naming/metadata conventions. Images live under `Assets/Screenshots`, with capitalized directories and SHA-256 filenames. Exact bytes are deduplicated; a corrupt existing asset is rejected.

The note receives a relative Markdown reference in a localized Visual References section. Existing stable IDs, YAML metadata, evidence/relations, optimistic saves and revisions remain in use. The authorized vault is included in the action identity and rechecked under the Brain write gate for both asset and note; changing vaults cannot redirect an approved attachment. Moving, duplicating, copying or archiving notes rebases image paths; deletion/movement of a referenced generated asset is blocked, including references retained by revisions. Removing references before removing the shared asset remains a deliberate user operation. There is no automatic asset garbage collection. If the image write succeeds but a later note save fails, an unreferenced content-addressed asset can remain; retry reuses it. This is not a transactional two-file database.

The Brain reader displays bounded local PNG/JPEG references and selected image assets. It never fetches a remote Markdown image URL. `ReadAttachmentAsync` and `ResolveAttachment` allow the assistant to recover exact bytes, rather than only index the filename. Portable path, link and traversal checks remain in the existing storage adapter.

## Reproduction and validation limits

```powershell
dotnet build Laura.sln -c Release --no-restore
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-restore
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage _Output/ScreenshotCheck/Storage --screenshot-smoke _Output/ScreenshotCheck
```

`--screenshot-live-smoke` additionally references the already DPAPI-protected default credential in-process. It does not copy/print the key. It transmits only generated image fixtures after native test-button approval; no actual desktop, microphone or personal Brain is sent. The test distinguishes new image pixels from words supplied in prompts/prior turns. Test clipboard contents are a synthetic screenshot.

`--screenshot-ui-smoke` keeps an isolated synthetic editor open with the main window hidden and temporary Ctrl+Alt+Shift+F9/F10/F11 registrations. A `ui-ready.txt` marker identifies readiness; creating `ui-done.txt` ends the fixture. It times out after 15 minutes. This assists manual testing and does not imply that mouse/key gestures passed.

Core tests cover negative origins/crops, annotations/history/limits, shortcuts, INI persistence, native-gesture/cloud boundaries, remote-path rejection, frozen visual queue data, Brain creation/update/dedup/recovery, relative paths during moves/copies/archives, shared-asset protection and protocol identity. Native fixtures exercise rendered annotation pixels, original-resolution PNG/JPEG, invalid output paths, actual image clipboard, WGC of the synthetic editor, local virtual-desktop geometry, actual hotkey registration/conflicts and recovered Brain bytes.

Required manual coverage remains: region/window mouse selection and adjustment, actual global keystrokes while hidden/minimized, Windows folder browsing by mouse, physical multiple/mixed-DPI monitors and display disconnection, narrator, OS notification suppression/Do Not Disturb, audible selected devices, disk-full, protected third-party capture, and a full end-user microphone-to-screenshot conversation. The attempted Computer Use gesture test returned `Computer Use app approval timed out`; no gesture success is claimed. Available automated/native evidence does not replace these acceptance checks.

See the [delivery report](laura-permissions-screenshot-delivery.md) for actual final run results and file groups.
