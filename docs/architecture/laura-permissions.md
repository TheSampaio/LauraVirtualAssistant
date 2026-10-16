# LAURA Permissions

## Laura 2.0 bounded workspace and process foundation — 2026-10-15

`authorize_workspace` requests a native review for the current trusted task identity, existing directory and optional **actual provider** disclosure host. The resulting one-hour grant is session-only and appears in the existing revocable grants list. It authorizes ordinary project text reads/creation/edits only through the file adapter. An approval without a recipient cannot disclose project text; an OpenAI recipient cannot be reused for Gemini. Other tasks, sibling paths, reparse points, protected storage, credentials/policy files, deletion, commands and system changes cannot inherit it. Task grants are deliberately not restored after restart. Task orchestration must explicitly reacquire authority when safely resuming.

Automatic mode additionally permits bounded data-note writes/directory creation within Laura's own Workspace and focus of a native identity-pinned window. Arbitrary input injection, remote disclosure, processes and sensitive paths retain their specific approval requirements. Existing installed-application policy and exact remembered grants remain supported. Audits identify the native task and whether authorization came from policy, native gesture, reviewed task scope, remembered grant or current user approval.

`WindowsCommandExecutor` replaces the inline process handler. It validates/fixes executable and working paths, snapshots literal arguments, keeps executable bytes read-locked from hashing through dispatch, requests exact central authorization and uses `ProcessStartInfo.ArgumentList`. It clears inherited environment variables except an explicit Windows/runtime allowlist, closes stdin, drains stdout/stderr concurrently in bounded chunks, reports optional ordered output delivery, preserves exit code/process ID, bounds output and duration, and terminates only its owned process tree on timeout/cancellation. PowerShell can be resolved like any other executable; no ExecutionPolicy bypass is inserted. **A working directory is not an OS sandbox.** Task file grants never authorize builds/scripts/processes. Safe command profiles, OS isolation, persistent background-process monitoring and task resume remain open gates; this foundation is not represented as unrestricted coding autonomy.

`close_app` resolves exactly one existing window, pins its handle/process/title and requests protected normal close. `WM_CLOSE` preserves application save prompts; the adapter observes whether the window disappeared and never force-kills. An unchanged open window is reported as unresolved, not success or an invented Windows block. Cross-application unsaved-state detection remains unavailable; normal close therefore still receives explicit approval.

Evidence: **487 domain tests passed**, zero failed/skipped, `_Output/Validation/Laura2/phase2-final.trx`; solution builds with zero warnings/errors. Native `--command-smoke` passed exact approval/denial, spaces, Unicode working directories and arguments, literal shell syntax, controlled environment, separate stderr, nonzero exit 7, bounded 400,000-character output, timeout, cancellation and owned-process cleanup. The smoke uses synthetic local child programs without cloud credentials or microphone capture. Native application close/save prompts and PowerShell-specific integration still need dedicated acceptance runs. File writes now compare the actual bytes against the requested content before reporting completion.

## Architecture and trust boundary

Laura remains .NET 10, MAUI and native WinUI on Windows. `Laura.Core/Permissions` defines the deterministic policy, immutable execution identities, asynchronous approval coordinator and bounded JSON store. Windows presentation uses the existing palette, spacing, SVG-derived assets, native controls and tray adapter. No package or framework was added.

The previous `WindowsConfirmation` used the main window's `XamlRoot`; hidden startup and close-to-tray made that unsuitable for approvals. `WindowsApprovalCenter` instead owns a secondary WinUI window. Background requests call `AppWindow.Show(false)`, never set topmost and remain available from the taskbar, tray menu and HUD shield/count. Explicit opening activates the window. Placement uses native monitor work areas and foreground DPI, including negative screen coordinates. General alerts, text prompts and choices now use `WindowsDialogs`, without treating information as a permission. File pickers and platform credential integration remain native.

`MauiAssistantTools` uses a closed tool/effect registry, snapshots and canonicalizes relevant arguments, resolves file aliases and executables before review, and authorizes before entering execution. Its mutation queue is acquired **before** approval, so approval is not followed by an unguarded wait in that queue. Desktop skills go through `MauiDesktopTools`; launch and browser skills use the asynchronous `IProcessLauncher` APIs. The Windows launcher rejects legacy synchronous entry points to prevent bypass. Code that calls a Windows adapter directly is trusted application code; this is not an OS sandbox for arbitrary programs explicitly approved by the user.

The model cannot select its risk, origin, mode, grants or decision. `confirm_action` cannot grant a future approval: the concrete tool must obtain its own authorization. Unknown operations fail closed. Native input pins the target HWND, PID and title before review, validates them and foreground focus again, checks mouse endpoints and refuses Laura's own windows. This prevents input tools from operating Laura's permission controls. Windows may refuse input or focus, particularly across elevated processes; Laura respects this and does not bypass UAC.

## Policy, scope and persistence

`PermissionPolicy` considers operation, canonical resources, adapter origin, external destination and permitted local data extensions. File/cloud reads, clipboard disclosure, screen/camera disclosure, arbitrary application input and undo are critical. Programs returning output to Gemini also disclose their output and are critical; launching other programs, moving/trashing files and changing settings require explicit approval. Writes of executables, scripts, configuration or sensitive locations cannot inherit an ordinary data-folder grant. Reading a sensitive path never lowers cloud disclosure risk.

One-time approval belongs to a single pending request and exact parameter fingerprint. It is never exported as a reusable token. Only local data writes and directory creation can be remembered. Exact grants bind operation, origin, resource and parameter/content digest. Optional directory grants bind one operation and origin to a canonical folder with separator-aware ancestry matching; they do not grant reading, execution, deletion or disclosure. `Solicitar Aprovação` honors exact previously granted operations. `Aprovação Automática` additionally honors explicit folder policies. Neither mode automatically approves higher risks or unknown tools.

Settings live in the existing `settings.ini` `[Permissions]` section. Grants and minimal audit records live in `Security/permissions.json` beneath the selected application data root, outside Brain. Writes replace the store atomically after flushing. Invalid or unreadable stores restore no grants. Session grants are memory-only; successful persistent revocation is saved and applies after restart. A persistence failure is surfaced and must not be treated as a successful durable change. Restoration cancels pending work, clears grants and restores safe preferences. Audit retention is configurable from 0–365 days and bounded to 2,000 records; payloads, command arguments, clipboard contents, URL query strings, secrets and note contents are not retained in the audit.

The generic model file tools cannot access application/security storage or the configured Brain vault. The dedicated `Workspace` remains available through its guarded file operations, preserving the existing assistant workspace. Brain's Markdown/YAML and hash-reviewed skills remain context, not security authority. Existing Brain storage, local learning, stable IDs, wikilinks, relation evidence, optimistic writes and reversible archives were reused rather than replaced.

Resources and link ancestry are checked before review and immediately before dispatch, including file metadata changes while waiting. Arguments cannot change after approval. Redirecting junctions, symlinks and alternate streams are rejected. This is a conservative application guard, not an atomic kernel sandbox against a hostile process running with the same Windows account; native process execution and shared desktop input remain within the explicitly approved Windows user's privileges.

## Asynchrony, cancellation and voice

Requests use asynchronous completion sources, cancellation linked to the caller and application lifetime, a configurable 10–600 second deadline, a 32-request limit and exact-identity duplicate rejection. Cancellation, denial, expiry and late decisions cannot dispatch an action. Reset/shutdown invalidate in-flight decisions; persistent grants are retained on normal exit, while session grants are discarded. Secondary windows and common dialogs are closed on shutdown, rather than only destroying an `AppWindow` and retaining the WinUI window lifetime.

Voice modes are Always / When hidden or minimized / Never. The existing Gemini Live voice, model selection/fallback, device, output volume, read-aloud renderer and microphone playback gating are reused. A serialized read-aloud gate lets generic approval announcements run while the conversation semaphore is occupied by a tool awaiting approval. Stale queued announcements are skipped. No action details or private resources are spoken to the cloud; the announcement uses a fixed localized phrase. Without an available Gemini credential/network or with zero output volume, visual requests remain actionable. Audio responses cannot authorize requests because the current architecture has no trusted speaker authentication.

Native Windows tray notifications contain generic text. Explorer, notification settings or Do Not Disturb can suppress them; suppression never grants access or removes pending requests. The independent window, HUD count and tray entry provide the durable fallback.

## Validation

Build and tests require the SDK in `global.json` and the existing MAUI workload. No new dependency restore is needed.

```powershell
dotnet build Laura.sln -c Release --no-restore
dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-restore
```

The permission tests exercise dispatch counters and actual isolated persistence: denial, once-only identity, cancellation, expiry, late decisions, duplicate/queue limits, simultaneous requests, exact and folder scopes, origin/effect boundaries, mode changes, restart, revocation, reset, storage errors, modified resources, a native Windows junction substitution and independence from Markdown contents. Tests also cover protected storage, the Workspace exception, sensitive paths, audit payload exclusion and INI round-trip. Existing Brain, conversation, dispatcher and configuration tests run with them.

Actual application integration uses harmless Markdown fixtures and native WinUI button automation peers:

```powershell
Laura.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Laura.exe --storage _Output/PermissionsCheck/Storage --permissions-smoke _Output/PermissionsCheck
```

Add `--permissions-live-smoke` only for an explicitly requested live test. It references the current user's already protected default credential **in-process**, without printing/copying it into the fixture store. It sends only a synthetic announcement, never microphone audio, personal files or Brain context. Two announcements must generate PCM/transcripts while the conversation gate is held. Test output volume is zero; this confirms generated/queued audio, not subjective speaker audibility.

The native run checks open/hidden/minimized primary windows, a separate visible non-topmost approval window, actual approved file writes, denial, cancellation, expiry, protected-store rejection, the security settings tab and saving settings without a cloud key. `_Output` reports and UI renders are ignored artifacts. The existing `--ui-smoke` regression now covers all five settings tabs, two interface languages and compact/minimum layouts.

Manual coverage still required: mixed-DPI/multiple physical monitors, display disconnection, narrator/screen-reader interaction, OS notification suppression/Do Not Disturb and audible playback on the user's selected devices. These are not claimed by isolated renderer tests. An approved arbitrary external program is not restricted by a new process sandbox. Voice-only authorization is deliberately unavailable.

## Principal files

### Gate executed on 2026-10-13

- `dotnet build Laura.sln -c Release --no-restore`: passed with zero warnings/errors.
- `dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-restore`: 202 passed, zero failures/skips.
- Actual permission fixtures: `_Output/PermissionsGate/result.txt` passed open/hidden/minimized windows, native decisions, real writes, denial/cancellation/expiry, security-store rejection and settings save without credentials.
- Actual Gemini announcement generation/serialization: `_Output/PermissionsLiveSmoke/result.txt` passed with generated PCM/transcripts and no microphone/private context.
- Full existing UI regression, including five tabs, EN/PT and 720×600: `_Output/PermissionsUiVerified/result.txt` passed. Earlier failures exposed retained secondary-window lifetime, a no-op scroll wait and loss of the compact application viewport; all were corrected and rerun. Test processes were verified to exit.
- The physical/manual limitations above remain pending and are not reported as tested. The automated and principal available integration gate is satisfied; Screenshot implementation can begin.

- `Laura.Core/Permissions/{PermissionAction,PermissionOptions,PermissionPolicy,PermissionStore,PermissionManager}.cs`
- `Laura.App/Infrastructure/{MauiToolPermissions,LauraNotifications,PermissionSmokeRunner}.cs`
- `Laura.App/Platforms/Windows/{WindowsApprovalCenter,WindowsDialogs,WindowsConfirmation,WindowsTrayHost}.cs`
- `Laura.App/Views/PermissionSettingsView.cs`, `SetupPage.cs`, `MainPage.cs`, `MemoryPage.cs`
- Existing composition, application lifetime, INI settings, desktop tools, launcher/skills, computer input and Gemini Live voice services.
- `Laura.Core.Tests/Permissions/PermissionManagerTests.cs` and the two existing locale catalogs.

Screenshot implementation began only after this gate passed. Trusted native local capture/copy/save/Brain-attachment gestures authorize their exact local operation; model JSON can never set that marker. Cloud-image disclosure remains critical even for a native gesture and is checked around the actual Live network send with an exact pixel digest. Generic local filesystem adapters reject UNC/mapped-network resources before metadata IO, and a network Brain attachment has critical external effects. See [Screenshot architecture](laura-screenshot.md) and the [final delivery report](laura-permissions-screenshot-delivery.md).

## GUI opening correction — 2026-10-14

The reported NeuralScreen attempts were authorized `run_program` dispatches, not UI decision failures. That adapter redirected output, awaited exit and could terminate a long-running GUI at its CLI timeout. `open_app` also did not directly accept absolute executable paths. It now accepts the exact path; no-argument PE GUI entrypoints mistakenly sent to `run_program` are routed to interactive launching. The native launcher uses the application's directory by default, preserves an explicit custom directory under its own authorization, does not redirect GUI streams and never waits for or kills the GUI as a CLI task.

`ExecutableMetadata` reads bounded PE headers/resources and XML without loading code or resolving external entities. Installed GUI executables in Program Files without arguments, elevation/uiAccess or installer/interpreter classification use the distinct moderate `ApplicationLaunch` effect. This classification is not publisher verification or a sandbox for the external application. Unknown programs, custom launch directories, arguments, commands, installations and cloud disclosure retain individual approval. Windows UAC remains enforced.

Automatic mode has the persisted `OpenInstalledApplications` switch. Unreviewed automatic opening requires native installation checks, including ACLs that prevent ordinary user writes. NeuralScreen's actual executable/directory grant the current user FullControl, so it needs an exact application approval rather than a blanket folder policy. Session/persistent application grants pin the canonical executable, SHA-256, empty arguments and working directory. They never authorize a parent directory, another executable, changed bytes or arbitrary command arguments. One-shot approval never becomes a remembered grant. The UI omits folder scope for application grants, shows their limits and supports ordinary revocation. Legacy data-grant fingerprints are unchanged.

The launcher hashes before authorization and again with a read handle at dispatch, rechecks classification/installation constraints, and checks learned identities. Native failure/nonzero command outcomes are recorded as failed execution instead of being conflated with authorization success. Model JSON cannot supply `AutomaticEligible`; Brain memory still grants no permissions.

Validation: **303 Core tests passed**, zero failed/skipped, `_Output/GuiLaunchFix/gui-launch-verified.trx`; Release build passed without warnings/errors. Coverage includes PE GUI/console/DLL distinction, manifest elevation/uiAccess, prohibited XML entities, mode/switch changes, failed authorized callbacks, narrow application grants, folder/argument/hash isolation, persistence/revocation and legacy fingerprint compatibility. `_Output/GuiLaunchNative/result.txt` passed native denial, one-shot UI approval, absolute paths and actual execution of a dedicated four-second C# GUI fixture. It verified the working directory, immediate tool return and survival beyond the CLI timeout; the fixture exited naturally. The real NeuralScreen was created after an exact session approval and its policy was reused without another prompt or launching a second instance. Its local log subsequently recorded 3,333 processed frames and a clean user-requested exit (code 0). No screenshot of its window or graphics-quality measurement was taken.

The test-only `Build/Smoke/GuiFixture` has no external dependencies or desktop/network access. Native diagnostics use isolated storage; they do not modify the user's permission store or stop the user's LAURA. The currently running Debug executable does not acquire source changes until rebuilt/restarted; the verified Release executable is separate.
