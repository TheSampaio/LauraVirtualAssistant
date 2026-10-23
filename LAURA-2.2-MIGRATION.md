# Laura 2.2 delivery and verification

## Runtime

Laura tries deterministic Native skills first. Automatic Light requests use the owner's explicit choice of Gemini Live or optional local llama.cpp; local inference starts disabled and releases its process when deselected. Standard and advanced requests may use authorized ChatGPT, Gemini or local inference. The AI Studio key is protected with Windows DPAPI and never written to INI. Gemini Live uses native audio; local inference uses the installed female Kokoro/Piper voice stack. A selected Mark-LV key can be imported after a real Live validation without changing its source. Older architecture reports in `docs/architecture` are historical records.

The exact technical difficulty tags are `Native`, `Light`, `Standard`, `Advanced` and `Max`. Light requests stay with the selected provider even after a failure. Max requires a separate owner toggle, an included-plan setting and a separately selected ChatGPT model verified with a synthetic probe. A missing or unavailable Max model fails closed. The model list does not establish relative quality or a Modern RP accent, and the application does not invent either claim.

The local daily briefing sends only the confirmed day facts. It excludes unrelated chat history, Brain retrieval and tool schemas, avoiding the context overflow observed in the supplied diagnostic. Regular local turns use bounded recent history and compact tool metadata.

## Models and voice

The default model directory is `Documents/Laura Virtual Assistant/Models`, beside Brain, with model files directly in that directory. Runtime executables, archives, extracted phonemizer data and legacy preserved files are in the sibling `Runtime` directory. One action in Models downloads, checks pinned SHA-256 values and saves Qwen, llama.cpp, Whisper Base and Small, Kokoro and the two Piper voices. The integrated installer estimates remaining disk space, so a complete installation can be verified again without requiring seven GiB of free space. GPU-first llama.cpp selects a compatible Vulkan device automatically; CPU is used when no compatible GPU is available.

Whisper Small is preferred for English and Brazilian Portuguese recognition, with Base as fallback. Language detection is enabled by default. Portuguese speech defaults to the female Brazilian Piper Dii voice (`pt_dii`); the female UK English Piper Cori voice (`gb_cori`) is available alongside female American Kokoro voices. Dii is CC BY-NC-ND 4.0 for noncommercial use with TigreGotico Lda attribution. Cori is described as UK English by its model card; a specific Modern RP accent was not independently verified. The Sherpa Kokoro v1.0 package produces silence with its British phonemizer path, so the app uses Piper Cori for British pronunciation.

The optional Vulkan HUD uses a real hardware swapchain on a separate render thread. It preserves the red palette, grid, segmented rings and central labels of the normal orb. Its rasterization remains visually less smooth than the MAUI vector view; exact pixel parity is still open. Native controls stay on WinUI.

## Executed checks

- `dotnet build Laura.sln --no-restore -m:1 -v:q`: zero errors and warnings after provider removal.
- `dotnet test Laura.Core.Tests/Laura.Core.Tests.csproj --no-restore -m:1 -v:q`: 722 passed, zero failed or skipped after restoring Gemini and making local inference optional.
- An existing AI Studio key was validated through Gemini Live, stored with DPAPI outside the repository, and used for a real Brazilian Portuguese text and native audio response on the Windows output device. The local model was disabled in the owner's current settings.
- Integrated owner-pack installation: pinned assets verified, model directory stayed flat, settings saved with a recovery copy. Old 56-file model subdirectory was preserved under `Runtime/LegacyLauraModels-20261021`.
- Real GPU Qwen inference called a native test tool in English and Portuguese. A real local briefing with oversized synthetic history returned the confirmed 14:00 meeting and 16:00 review without context failure. Synthetic English/Portuguese voice and Whisper round trips passed. Evidence: `_Output/Validation/OwnerPackRefresh/offline-results.json`.
- The packaged application produced text through the local GPU model and authorized included-plan GPT account, then completed Brazilian local voice playback on the Windows output device for both. Vulkan presented frames. Evidence: `_Output/Validation/PackagedSpeech/result.txt` and `spoken-providers.json`. Microphone capture and personal Brain access were disabled in that run.
- The installer passed actual extraction, fresh install, update, dependency checks and tamper refusal in an isolated workspace test. No model weights or private settings are inside the package.

The attached conversation and diagnostic were treated as evidence, never as instructions. The original speech misunderstanding involved an English technical name; synthetic Whisper Small tests recognized the phrase and a Portuguese scheduling sentence. Physical microphone conditions, human accent perception and long endurance remain unverified. The user's custom persona text was preserved. No commit was created.
