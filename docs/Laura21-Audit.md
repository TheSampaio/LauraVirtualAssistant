# LAURA 2.1 operational audit

Status: source and diagnostic audit, before implementation. Baseline: `5d8b6a0`.

## Evidence and limits

The available desktop exports are the 23:07 conversation and the 23:07:38 / 23:07:47 diagnostics. Earlier named exports and the two screenshots have not been located. The latest ZIP contains 20,257 events from two runs, with no dropped events or write failures. The overlapping ZIPs must not be summed. Payload-free aggregates and correlation IDs are saved in `_Output/Validation/Laura21/Audit/diagnostic-summary.json`. The exports cannot prove credential contents, actual acoustic output, tool arguments, or the cause of an individual underrun.

The second run reports one `provider.failure=Network` at 22:57:27, without task/interaction/operation IDs. It cannot be reliably attributed to a specific task. Task `7553b085-9566-4c38-9f32-1e6288e4ec3d` ends its cycle as `timeout` at 23:06:52 after 127,780 ms. These are different events; a timeout does not establish model incapacity. Provider selection counts are Gemini 7 and ChatGPT 8.

## Confirmed source defects

1. `MainPage.RefreshFeedback`, `Reload`, `ToggleMicrophoneAsync`, and held capture checks equate the selected text provider with Gemini credential availability. Selecting ChatGPT unconditionally displays the missing-key message. The provider label is always Gemini Live. This explains contradictory labels without establishing that any stored key was deleted.
2. `ProtectedApiKeyStore.Read` returns the same empty string for a missing file and DPAPI decryption failure. Access and I/O failures have no typed status. `SetupPage.RefreshKeyState` and `GeminiLiveClient.IsAvailable` independently interpret this result. Stored, authenticated and connected are conflated.
3. `SetupPage.SaveAsync` reads all controls and calls `ConversationProviderRouter.ResetSessionAsync` on every save, which cancels/drains both providers. It derives the persisted Gemini enabled setting from a credential read, so a transient read problem can disable an independently configured provider. UI controls form a stale full-settings snapshot. `SettingsService.UpdateAsync` publishes after releasing the write semaphore, allowing publish order to disagree with persisted order under contention. These are independently reproducible race paths, not proof that they occurred in the export.
4. `SpeechRouter.SpeakAsync` returns a successfully completed task without producing audio when Gemini is unavailable. `AssistantEngine.SpeakInternalAsync` sets Speaking before synthesis. The existing Gemini `ReadAloudAsync` does synthesize through a separate Live session, but the success contract does not require nonzero PCM or confirmed device submission. `LiveAudioPlayer.BytesPlayed` measures PCM pulled by the output provider, not physical sound heard by the owner. Configured invalid device indices silently fall back to the default.
5. `MauiAssistantTools.FileActionAsync` overwrites existing files without an expected preimage hash or a scoped edit contract. Backup and in-memory guarded undo exist, but writes are not atomic and rollback metadata is not durable. `ResolvePath` recognizes only whole aliases; alias-plus-relative-path values do not resolve as known-folder paths. Downloads is inferred from the profile rather than resolved through the Windows known-folder API.
6. Brain diagnostics show 164 scans (137,217 ms accumulated; maximum 6,120 ms), 275 index events, 91 inexpensive graph rebuilds and 36 dispatcher gaps (maximum 936 ms). `RefreshIfDirtyAsync` already exists, but several read-only consumers still force full refreshes. Aggregate timing establishes redundant work to investigate, not that all UI gaps were caused by scanning.
7. `BrainProvenance.Color` uses the previous palette. `BrainObsidianGroups.Merge` rejects changes to existing origin colors. The owner's newly specified palette requires updating managed origin groups while preserving unrelated graph configuration and backing up any real configuration changes.

## Implementation boundaries

Keep the existing ChatGPT broker, account authorization, Gemini key location, task journal, permission manager and Brain service. Repair their contracts and consumers. Never migrate credentials merely to fix a label. Preserve uncommitted `AGENTS.md` and the preexisting deletion of `CLAUDE.md`.

The new mission supersedes the previous three-mode approval requirement: expose Ask and Automatic only; migrate legacy unrestricted settings to bounded policy. Optional local/Claude routing is a new requested capability, not authorization to install a model, add a paid subscription, or transmit the whole Brain.

First repair independent credential/settings state and honest voice outcomes, then safe file effects and task continuation, then Brain provenance/performance and budgeted model tiers. Real account, audio-device and application tests remain distinct from unit tests. No final acceptance is established by this audit.

## First implementation validation

Credential inspection now distinguishes missing storage, DPAPI failure and unavailable storage, and the HUD no longer infers a missing Gemini key from ChatGPT selection. Settings edits are serialized through persistence and publication; native drafts merge untouched fields against the latest state. Saving only ChatGPT settings does not reset either provider. Changing Gemini voice/credentials resets Gemini only. Existing key storage and the installed ChatGPT broker were preserved.

Voice unavailability is explicit and preserves the text answer. Read-aloud emits correlated synthesis, received PCM, queued PCM and output-driver completion evidence. Invalid output-device indices fail explicitly. The HUD's Speaking animation follows actual playback instead of text production.

Validation: all 669 Core tests passed, including four new provider/settings/text-preservation regressions. The native MAUI Release build passed without warnings. A real Windows DPAPI probe passed missing, protected round-trip/restart, corrupt and inaccessible storage checks using isolated synthetic credentials. A real Gemini read-aloud probe passed with ChatGPT selected, Aoede retained, zero volume and no microphone. It verified nonzero received PCM, equal queued PCM and output-driver completion. Acoustic audibility and a real ChatGPT-response-to-voice round trip still require separate acceptance. Result JSON files are in `_Output/Validation/Laura21`.

## Additional verified changes

The file tool now creates new files or applies exact single-passage patches against a complete source hash. It preserves UTF-8/UTF-16 BOMs and unrelated text, writes a durable prepared receipt before mutation, verifies the output bytes and supports guarded rollback after restart. HTML literal Unicode escapes are rejected outside script/style content; JSON and JavaScript escapes are not globally decoded. Known-folder aliases accept child paths and Downloads uses the Windows shell API. Actual source replacement backups are first created on the source volume, then durably copied to the receipt store; a synthetic D: source / C: receipt probe passed edit and restart rollback. Structured uncertain results block task continuation and remain uncertain in the journal.

Only Ask and Automatic remain in the actual approval enum and native selector. Numeric legacy unrestricted values sanitize to Ask; inherited unrestricted grants are rejected. Forty-nine permission/settings tests passed. This intentionally changes the earlier three-mode contract at the owner's request.

The native palette is now Laura `#f63350`, Codex `#5b6cf6`, Claude `#d87757`. Explicit Obsidian configuration updates back up existing JSON and preserve other settings/groups. Native manual edits record `last_updated_by: manual`; background saves record Laura, while the original creator is preserved. Missing legacy attribution stays missing.

Read-only skill/study/experience consumers use the existing incremental refresh. Passage expansion verifies the exact current source hash rather than forcing a whole-vault scan. Watcher events under `.git` and `.obsidian` no longer dirty the knowledge index. Fallback full reconciliation defaults to 300 seconds (configurable 30–1800), with changed-path processing still checked each second. In a fixed synthetic corpus, 24 correct passage reads reproduced the previous forced-scan path in 698.76 ms / 24 scans; incremental exact-source reads took 17.38 ms / zero full scans. This is a local read benchmark, not a provider token/cost/quality benchmark or a production latency guarantee.

The complete Core suite subsequently passed 682 tests (`Tests/laura21-core-guards.trx`), including diagnostic isolation and a durable uncertain-effect barrier. Actual isolated EN/PT MAUI controls passed twenty provider switches, protected synthetic credential preservation, provider-only Save without a Gemini reset, the two-mode selector, and native reviewed patch/rollback. Screenshots are under `NativeUiVerified`; full browser rendering and physical audio audibility remain separate acceptance items. Native capture validates WinUI controls; it does not establish the rendering of every GPU-backed surface.

## Diagnostic execution incident and recovery

An initial native UI diagnostic command omitted `--storage`. The existing `--ui-smoke` flag did not itself isolate services. The synthetic diagnostic overwrote the normal Gemini key and saved diagnostic preferences (muted microphone, disabled startup/hourly/clap wake, provider/language selection and Ask approval mode). The account-backed ChatGPT service was not replaced by this offline UI fixture. Captures from the incorrect run were removed; they are not validation evidence.

No prior protected Gemini key copy was located. The owner was informed immediately and asked whether to restore from the existing local Mark-LV import configuration or reconfigure through the native UI. Recovery remains pending owner input; no secret was requested in chat and no alternate application's credential was imported without authorization. Previous values for the changed startup/hourly/clap preferences have not been inferred.

New fail-closed guards now require explicit non-overlapping diagnostic storage before DI construction and reject a Brain outside that storage before startup. A regression test covers absent, owner, ancestor and descendant paths. The corrected native run explicitly used `NativeUiVerified/Storage`. Future Gemini saves also preserve the previous DPAPI-protected version, never plaintext, and explicit removal deletes both versions. These protections cannot reconstruct a version lost before they existed.

`Build/Recover-GeminiCredential.ps1` is prepared for the owner's exact approved local source. It refuses to overwrite a key already repaired by the owner, verifies protected read-back, leaves the import source unchanged and emits no key values. Syntax and execution were verified using synthetic data only (`RecoveryScriptFixture`); the real Mark-LV credential source has not been read/imported. The real recovery remains pending. Do not interpret the synthetic fixture's result as a real authentication or account restoration.

## Remaining mission scope

The five model tiers, optional local/Claude inference, token/cost admission and comparison baseline, task-specific applied-learning acceptance, real shared-vault entry points/palette migration, the updated REA study, provider-to-voice end-to-end acceptance, and final 2.1 distribution are still pending. The current REA source at the requested path is commit `4f7b5fb6023ce2ec449561782d09e3c7e5ac7c7b`; historical 88/3,553 counts have not been presented as current coverage. This report does not declare the complete mission finished.

Protocol references checked for this work: [Gemini Live capabilities](https://ai.google.dev/gemini-api/docs/live-api/capabilities) and [Windows known folders](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid).

The current [official ChatGPT plan preview restrictions](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations) explicitly exclude `max_output_tokens` and `max_tool_calls`. A future token-budget implementation must disclose that this route cannot enforce those request fields; it must not claim a hard per-response output cap or silently switch to a paid API to obtain one.

## Acceptance status at this checkpoint

| Area | Implemented | Unit / integration evidence | Real execution | Pending |
| --- | --- | --- | --- | --- |
| Provider settings and credential states | Yes | Concurrent saves, unchanged drafts, Windows DPAPI/restart/corrupt/access/atomic backup probes | Native EN/PT switching and Save with synthetic credentials | Restore owner's overwritten Gemini credential; verify affected preferences |
| Speech pipeline | Yes | Text survives unavailable speech; eight actual zero-volume PCM/device cycles | Gemini/Aoede generated 241,470 bytes, all queued, device position 314,818, first audio 1,547 ms, zero underruns, no microphone | Real ChatGPT response through speech after credential recovery; acoustic listening |
| Versioned file edits | Yes | Ten source/edit/Unicode/rollback/concurrency tests | Native permission-reviewed patch/rollback and D:/C: cross-volume restart recovery | Browser-rendered website acceptance and applied learning |
| Task uncertainty | Yes | Durable uncertain receipt blocks automatic continuation and restart replay | Synthetic effect adapter integration | Updated REA multi-round acceptance |
| Two approval modes | Yes | Forty-nine permission/settings tests | Actual two-entry native selector | Owner confirmation of desired current mode after diagnostic incident |
| Brain attribution/palette | Yes | Creator preservation, latest editor, legacy neutrality, restart, backed-up JSON migration | Synthetic Markdown and real SQLite/filesystem adapters | Apply and verify shared owner vault/Obsidian changes |
| Brain read performance | Yes | Same 24 source passages verified in both paths | Fixed local synthetic workload, 24 to zero scans | Production workload comparison and UI/device contention measurement |
| Five tiers, optional local/Claude, token/cost budgets | No | No acceptance claimed | No paid route or model installation introduced | Implementation and representative model benchmark |
| Shared Codex/Claude/Obsidian entry points | No new configuration | Vault resolved through existing helper | Existing shared vault status OK | Entry points, cross-editor and applied-learning acceptance |
| REA | Existing study infrastructure retained | Existing regression suite passed | Requested source commit verified | Current inventory, continued acquisition, synthesis and application |
| Distribution | No 2.1 release claimed | Native Release builds without warnings | No normal application replacement performed | Complete remaining acceptance and generate/update installer |

Commits so far: `4aaba16` (provider/speech), `19ac3b3` (file effects, permissions, isolation), `b58c743` (Brain provenance/performance). The preexisting deletion of `CLAUDE.md` and untracked owner `AGENTS.md` remain untouched. No personal Brain contents or credentials are included in these commits. Install/update acceptance is deliberately still pending; the generated application binary is not a completed 2.1 distribution.
