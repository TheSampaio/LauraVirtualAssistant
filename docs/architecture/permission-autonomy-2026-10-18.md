# Permission autonomy audit — 2026-10-18

## 1. Root cause

`Approve for me` selected `ApprovalMode.Automatic`, but `PermissionPolicy.AllowsAutomatic` admitted only a small set of local data extensions, installed application opening, version queries and the bounded public GitHub clone profile. Source files, file moves and reversible deletion in an assigned task workspace fell through to `PermissionManager`'s native approval queue. `WindowsCommandExecutor` independently classifies process effects and sends them to that same manager; arbitrary PowerShell, build, test and dependency commands were deliberately not eligible because their working directory is not an OS sandbox. Thus the repeated requests came from a narrow policy, not from two conflicting approval managers. Model prose could also become stale if it described the setting chosen when a Live session started rather than the effective mode at tool dispatch.

The localization catalogs and an older agent report mentioned Full access, but `PermissionOptions` and the native settings picker exposed only Ask and Automatic. Numeric mode 2 was previously sanitized to Ask.

## 2. Architecture examined

- `MauiAssistantTools` normalizes model arguments and sends registered file, input, system and other effects through `MauiToolPermissions` and `PermissionManager`. Specialized adapters own their concrete effects; `run_program` is an internal routing operation at this layer and `WindowsCommandExecutor` submits the actual process action to the same manager.
- `PermissionManager` owns pending requests, decisions, grants, revalidation, audit outcomes and dispatch. Its one-hour repository/workspace grants bind an actual native task identity and recipient. The model cannot manufacture a grant by supplying JSON.
- `WindowsCommandExecutor` pins the executable and optional entry script while reviewing, freezes arguments and the environment, uses an owned Windows job, bounds output/time and cancels owned descendants. Its job controls lifetime, not filesystem/network access.
- `CognitiveTaskRun` and `CognitiveTasks` already retain checkpoints, bound call counts/repetition/duration, distinguish tool outcomes from goal verification, and expose pause/cancel/resume. The HUD has an interrupt action and Tasks exposes task cancellation.
- The recent LPAC/AppContainer experiment in `laura-cognitive-evolution.md` proved an isolated small offline .NET SDK fixture. It also found CoreCLR compatibility and network/package restoration constraints. That diagnostic is not a production command sandbox and was not promoted into one here.
- Windows ACLs, the effective process token, UAC, AppContainer and LPAC are distinct from Laura's application approval. No internal approval can grant elevation or override an OS denial. Windows `UnauthorizedAccessException` and Win32 5/740 already receive distinct tool statuses.

## 3. Changes implemented

`ApprovalMode.FullAccess` is again a third explicit settings choice. Selecting and saving it presents a native confirmation. The persisted INI choice alone has no effect on restart: the owner must activate it in the current process. A visible HUD shield badge and settings status identify an active session, and the settings button turns it off immediately for new dispatches. Scopes created solely through Full access are tagged and removed on deactivation. The manager rechecks activation immediately before dispatch and records `full_access` in its payload-free audit. Unknown operations, application security storage, resource integrity checks, registered-tool boundaries, cancellation and Windows protections still apply. Existing effects already dispatched are not rolled back by turning the mode off.

Automatic mode now admits code/text creation and edits, directory creation, reads for the configured provider, copies, moves and reversible trash operations when a trusted native task ID and every resource lie within Laura's assigned Workspace. Sensitive paths, unknown disclosure hosts and out-of-scope resources are not eligible. Existing explicit one-hour task workspace grants continue to cover reviewed external projects. Ask still reviews ungranted effects. These rules are in the central policy rather than a new tool-level approval switch.

The Live persona now directs the model to call concrete tools and rely on the policy evaluated at dispatch. It no longer names a potentially stale mode captured at session start.

## 4. Mode behavior

| Mode | Current behavior |
| --- | --- |
| Ask for approval | Registered protected effects wait for native approval unless an exact existing grant or trusted native gesture applies. Denial prevents dispatch. |
| Approve for me | Eligible operations in the assigned Workspace and existing bounded installed-app/version-query/public-clone profiles dispatch automatically. Effects outside that policy still require native review or are blocked. |
| Full access | After native session activation, registered effects dispatch without per-action application approval. It runs with the process's effective Windows privileges and keeps Laura's hard resource/integrity guards. Session activation is lost on restart. |

## 5. Executor and task cycle

Every added automatic decision enters `PermissionManager.ExecuteAsync`, which validates resources before authorization and again before dispatch, checks the current mode, and records the outcome. File effects keep the editor's byte verification and receipts. Process effects keep the existing exact native executor and owned job behavior. Existing task checkpoints and call budgets remain the recovery mechanism; this change does not create replayable one-shot tokens or reset a task after failure. Tool success, including process exit zero, is not by itself a verified user objective.

## 6. Verification

- `dotnet build Laura.sln -c Release --no-restore`: passed, zero warnings/errors.
- `dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj -c Release --no-restore`: final run passed **702 tests**, zero failed/skipped. New cases cover session activation/deactivation, restart, removal of Full-access-derived scopes, task-scoped code/file operations, out-of-scope denial, and INI round-trip. Result: `Laura.Core.Tests/TestResults/permission-rework-final.trx`.
- `dotnet build Build/Smoke/Laura.Smoke.csproj -c Release --no-restore`: passed, zero warnings/errors.
- Console Windows integration: `Laura.Smoke.dll <isolated-root> --permission-autonomy-smoke` passed with actual file bytes and a real PowerShell child (exit 0). It exercised automatic task code creation, out-of-scope denial, Ask approval, Full access PowerShell execution and immediate deactivation. Final evidence: `_Output/Validation/PermissionAutonomy/ConsoleFinal/permission-autonomy-result.json`.
- The MAUI `--permissions-smoke` harness passed in isolated storage after its old text-result assertion was updated to compare actual file bytes and the automatic fixture received a real native task identity. It exercised visible/hidden/minimized native approval windows, approval/denial/cancellation/expiry, protected storage, automatic code creation, Full-access dispatch, immediate off switch behavior, a rendered Security tab with all three picker entries, and real settings save. Evidence: `_Output/Validation/PermissionAutonomy/MauiResultsBadge/result.txt` and native captures, including the visually inspected `full-access-hud.png` showing the red `FULL` shield badge. The harness activated Full access through trusted diagnostic code, so it does not prove a physical click on the activation confirmation.
- The existing PowerShell script smoke failed while starting its `.ps1` fixture: Windows PowerShell returned `AuthorizationManager check failed`, exit 1. Its observed entry script was hash-pinned and approved; this appears to be host script policy, not a Laura approval denial. The result is a failed regression gate until reproduced in an environment that permits that script or the fixture is adapted without bypassing policy. Evidence: `_Output/Validation/PermissionAutonomy/PowerShellRegression/powershell-script-observation.json`.

## 7. Partial and pending requirements

**Implemented and verified:** central decisions for the added automatic task file operations; three effective modes in Core; native-session Full access gate and immediate revocation for new dispatches; actual Windows file/PowerShell console effects; current task checkpoint/cancel behavior retained.

**Implemented but not fully validated:** native activation confirmation and the settings off button. The isolated GUI harness checked the three picker items, rendered the Security tab, visually confirmed the active HUD badge and exercised effective mode changes, but physical activation/deactivation clicks, multiple monitors and screen readers remain untested.

**Pending:** routine automatic PowerShell, build/test and dependency installation in `Approve for me`; task-wide process authority; a production LPAC/AppContainer broker with controlled network/package access; live model/provider comparison; end-to-end React Native creation, repair and build. Running project scripts under the ordinary user token can access resources outside a working directory. Automatically approving every such command would claim a scope the current executor cannot enforce. These commands still use exact native review, while Full access can run them after explicit session activation.

## 8. Remaining risks

Full access exposes all registered effects to the configured conversational model under the user's effective Windows token. A model mistake or malicious source text could request consequential actions before the user turns the mode off; the tool registry and resource guards reduce, but do not eliminate, this risk. Process children in Full access can act outside their working directory. Task cancellation terminates owned command descendants where the job API permits, but cannot undo completed filesystem or external effects. Physical activation and the settings off button still need owner-side confirmation.

## 9. Recommended next work

Build the reviewed production process broker from the LPAC experiment before widening automatic command policy. It needs immutable runtime controls, an explicitly granted task source/output tree, constrained dependency retrieval, verifiable child cleanup and adversarial tests for outside paths and network access. Then add task-bound process grants and run actual compile, test, repair and restart scenarios through MAUI and Gemini. Physically check the Full-access activation/off controls and rerun the PowerShell script fixture with its effective host policy documented.
