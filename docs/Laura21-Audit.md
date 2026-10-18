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
