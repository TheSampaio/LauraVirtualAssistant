# Laura Virtual Assistant

Laura is one local-first Windows tray application with a Mark-LV-inspired three-column HUD, independently implemented in WinForms with Laura branding and red accents. Press **Alt + L** to open or hide it. Built-in text commands work without a model or cloud credentials. Native llama.cpp and Whisper run inside Laura; neither reference project is launched or redistributed.

## Install and first run

Use `_Output/Distribution/LauraSetup.exe`, produced by `Build/Publish.bat`. The per-user installer presents the Microsoft runtime license terms, includes .NET 9.0.20, the C++ CRT/OpenMP runtime and native CPU libraries, verifies its embedded ZIP, installs under `%LOCALAPPDATA%/Programs/Laura`, creates a Start Menu shortcut and opens Laura. Close Laura before updating. The installer is unsigned; organizational execution policies may block it. A portable alternative is `Laura-win-x64.zip`: extract the entire folder and run `Laura.exe`. Keep the bundled libraries and licenses beside the executable. No Python, Git, .NET installation, developer toolchain or model server is required by either distribution.

Validated on this Windows x64 machine. The minimum API target is Windows 10 build 19041; other Windows versions and a clean virtual machine have not been validated. Uninstall by closing Laura, disabling automatic startup in settings, removing its installation folder and Start Menu shortcut. Personal data is kept separately and must be deleted explicitly if desired.

The HUD has a system monitor on the left, an original geometric face in the center, and activity/chat commands with microphone controls on the right. **Setup** opens the central settings workspace; **LAURA** returns to the HUD. First run opens **Models**; launching with `--show` opens the HUD, where **Setup** also provides model installation. Hardware details and compatibility estimates appear before download. Select a reviewed model, choose **Download / resume**, then **Load and use**. Downloads use HTTPS, pinned publisher revisions, exact size and SHA256 verification. Cancel retains resumable partial data; verification failures permit retry. Models can be unloaded, switched and deleted from this page. Compatible GGUF files placed in the Models directory are detected, but imported files are the user's responsibility.

The reviewed catalog starts with Qwen 2.5 0.5B Instruct Q4_K_M, a 469 MiB multilingual download under Apache-2.0. CPU inference uses a 2,048-token context and at most 256 output tokens. The hardware estimate recommends at least 4 GiB total RAM and accounts for available RAM and disk space. This small model's quality varies. GPU/VRAM detection and GPU acceleration are not implemented. Optional Ollama is supported only at an explicitly configured loopback endpoint and requires an existing user-managed Ollama installation; embedded inference is the default.

## Voice and languages

English is the default. **General** provides independent interface and conversation language settings: English (`en-US`) and Brazilian Portuguese (`pt-BR`). Settings persist after restart. Speech recognition and synthesis follow the conversation language, including voice previews. Missing UI translations fall back to English; missing local voices are reported and do not silently speak another language.

On **Voice**, install the optional multilingual Whisper Base model (141 MiB, MIT), select audio devices, unmute the microphone and press **Record**. Press it again to finish, or allow the 15-second limit. Audio stays in memory and is transcribed locally. The recognized text enters the chat draft for review; press **Send** to execute it. Installed Windows offline recognizers provide a fallback when Whisper is absent and the selected language is available. Windows offline voices provide TTS. Existing Sherpa voice support remains available for compatible user-supplied assets with their own licenses.

Recording begins only while Laura is idle, preventing Laura's speech from being recorded during normal use. Cancel discards capture or stops inference. This is half-duplex interaction; full-duplex acoustic echo cancellation is unavailable. Continuous listening and **Hey Laura** are unavailable until an appropriately licensed detection model has measured accuracy and false positives. The HUD uses an original geometric face with animated rings and a speaking-state mouth indicator. It does not implement phoneme timing or audio-reactive lip sync; advanced viseme animation is deferred.

## Memory and tools

Saved conversation history appears in **Chat**. **Memory** searches and inspects explicit keyed preferences. Add, inspect or delete a preference. Disable **Remember conversations** to stop storing and retrieving history/preferences; existing saved data remains until explicitly deleted. Clear history removes saved messages; current-session messages remain visible until restart. SQLite uses one versioned database, bounded storage and parameterized queries. Corrupt and future-version databases are preserved, with errors reported rather than destructive replacement. No automatic learning, extraction or telemetry runs.

Examples in the selected conversation language:

| English | Brazilian Portuguese |
|---|---|
| `what time is it` | `que horas são` |
| `save note Buy milk` | `salvar nota Comprar leite` |
| `list assistant files` | `listar arquivos da assistente` |
| `read clipboard` | `ler área de transferência` |
| `capture screen` | `capturar tela` |
| `undo last file` | `desfazer último arquivo` |
| `run steps: what time is it; save note Buy milk` | `executar etapas: que horas são; salvar nota Comprar leite` |

Clipboard reading, screen capture and undo require a trusted UI confirmation with **No** selected by default. Notes and PNGs receive generated names inside Laura's dedicated Workspace. Undo applies only to an unchanged file created by Laura in this session. Explicit command sequences permit one to four known commands; unknown or nested steps are rejected before execution. Model responses cannot execute tools or supply approval. Existing allowlisted app launch, web search, volume and lock skills remain available. Arbitrary filesystem access, browser DOM control, remote dashboards, plugin execution and proactive reminders are unavailable.

Settings, `Models/`, `memory.db` and `Workspace/` live in **Documents/Laura Virtual Assistant**. These files contain local personal data and are not encrypted. Cloud-synced Documents folders may sync them under the user's Windows configuration. No microphone audio, screenshot, file, prompt or memory is uploaded by embedded inference. User-requested downloads contact Hugging Face and its CDN; weather contacts wttr.in; web searches open the browser. Cloud AI/voice and privileged remote APIs are absent.

## Build and verify

Development requires the SDK pinned in `global.json` and Windows SDK access. Publishing also requires a licensed Visual Studio release redistributable directory: the pinned x64 CRT/OpenMP files are hash-checked against `licenses/vc-runtime-inventory.json`. The default directory is Visual Studio 2026 Community's `VC/Redist/MSVC/14.51.36231/x64`; pass `-VcRedistDirectory` to `Build/Publish.ps1` for another location containing those exact files. End users receive these files inside Laura. Dependencies are pinned by project references and committed NuGet lock files. Analyzer rules remain fixed at .NET 9 recommendations. Projects stay at the repository root; composition is App → Platform.Windows → Core.

```powershell
dotnet restore Laura.sln --locked-mode
dotnet build Laura.sln -c Release --no-restore
dotnet test Laura.sln -c Release --no-restore
Build/Publish.bat
```

`Build/Publish.ps1` creates the self-contained folder, ZIP, offline installer and `SHA256SUMS.txt` under ignored `_Output/Distribution`. `Build/Run.bat` runs the development application. Optional native smoke uses isolated ignored storage and synthetic speech, never microphone recordings:

```powershell
dotnet run --project Build/Smoke/Laura.Smoke.csproj -c Release -- _Output/Smoke --download --native --speech
```

The smoke may download the reviewed weights (about 610 MiB total) and requires installed English/Portuguese TTS voices for its bilingual speech fixture. Tests cover downloads, cancellation, resume, corruption, path validation, memory restart/search/deletion, localization and bounded tools. See [the assessment](docs/architecture/native-integration-assessment.md), [delivery report](docs/architecture/native-integration-delivery.md), and [third-party notices](THIRD_PARTY_NOTICES.md) for evidence, provenance and remaining limitations. No Mark-LV source, translated code or assets are included; its noncommercial license is respected.
