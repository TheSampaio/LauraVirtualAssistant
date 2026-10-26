# AGENTS.md

Guidelines for working in this repository. Read this before changing code.

## What Laura Is

Laura is a personal assistant for Windows inspired by Jarvis, in a feminine version.
She lives in the system tray, speaks responses, accepts typed chat commands, and
shows the settings window with **Alt + L**. Migrated from Python to **C# / .NET 10 and MAUI**.

English (en-US) is the default; Brazilian Portuguese (pt-BR) is optional. Keep interface and conversation language independently configurable through centralized resources.

## Commands

```powershell
dotnet build Laura.sln          # build everything
dotnet test                     # run the tests
dotnet run --project Laura.App  # run (Build/Run.bat does the same)
```

`Build/Publish.bat` creates a self-contained Windows x64 folder, ZIP and offline installer in `_Output/Distribution`. Native libraries and licenses must accompany Laura.

## Folder Structure

Each project lives in its **own folder at the root**. There is no grouping `src/`
folder and no shared `Directory.Build.props` file (each `.csproj` declares its own
properties). Do not reintroduce either one.

```
Laura.sln
Laura.Core/            Laura.Platform.Windows/
Laura.App/             Laura.Core.Tests/
Build/  Data/
```

## Architecture - Respect the Layers

Dependencies flow in only one direction: `App -> Platform.Windows -> Core`.
**`Core` never depends on platform or UI.**

| Project | Role | May Depend On |
| --- | --- | --- |
| `Laura.Core` | Pure domain (net10.0): engine, skills, settings, localization | no platform |
| `Laura.Platform.Windows` | Supervised local inference/voice and Win32/WinRT adapters | `Core` |
| `Laura.App` | Native MAUI / WinUI UI + Win32 tray | `Core`, `Platform.Windows` |
| `Laura.Core.Tests` | xUnit tests | `Core` |

Rules:
- Every external dependency (speech, system, network, UI) enters `Core` through an
  **interface** in `Laura.Core/Abstractions`. Windows implementations live in `Platform.Windows` or `App`; portable file/HTTP/SQLite implementations may live in Core behind injected interfaces, following the existing INI/weather pattern.
- The abstraction-to-implementation connection happens **only** in
  [`ServiceConfiguration`](Laura.App/Composition/ServiceConfiguration.cs). Do not
  instantiate services with `new` outside the composition root and tests.
- For the domain to act on the UI (open a window, exit), use `IShellController`;
  never reference Windows Forms from `Core`.

## UI: .NET MAUI

The owner requested the MAUI migration. Use native MAUI controls and GraphicsView
for the counter-rotating reactor rings; do not restore Windows Forms or the face.
The initial supported platform remains Windows. The domain stays UI independent.
Tray, global hotkey and desktop capture use Win32/WinRT adapters, without Forms.

## Concurrency Model - Do Not Block the UI

`AssistantEngine` processes everything in a **single consumer loop** fed by a queue
(`System.Threading.Channels`). Rules when changing it:
- UI calls only **enqueue** messages; they never do work on the caller. The UI must
  not freeze while Laura speaks or waits for a model.
- Only the loop changes engine state. Do not introduce locks to protect shared state;
  enqueue a message.
- Events from background threads that touch the UI go through `IUiDispatcher`.

## How to Add a Skill

1. Create the class in `Laura.Core/Skills/Builtin/` implementing `ISkill` (or
   inheriting from `PhraseSkillBase` when activated by a phrase list).
2. Define `Priority`: **lower** values are evaluated first. Specific triggers need
   to precede generic ones (for example, "open settings" before "open").
3. Activation phrases and responses go in the **language catalogs**, not in
   code. Add the key to `LocalizationKeys` and the text to `Locales/en-US.json`.
4. Register it in `AddSkills` in `ServiceConfiguration`.
5. A skill should not throw for normal flow (for example, missing app -> spoken
   response, not an exception). Unexpected failures are isolated by the dispatcher.

Never hard-code spoken text or fixed triggers in a skill.

## Localization

- Text and triggers: `Laura.Core/Localization/Locales/en-US.json`.
- A key may contain a single string or a list (random response variants / multiple
  triggers).
- English is the default. Maintain pt-BR catalog parity and independent UI/conversation settings.
- Keys accessed by the domain must exist in `LocalizationKeys` (typos become compile
  errors).

## AI Providers and Voice

Laura offers optional local llama.cpp inference with multilingual Whisper input and
local female Kokoro/Piper speech, Gemini Live with native audio, and separately
authorized ChatGPT. Automatic Light requests use the owner's explicit Local or
Gemini choice. Local inference starts disabled and releases its supervised process
when deselected. Cloud conversation needs explicit enablement and authorization.
The AI Studio key lives in a separate DPAPI file, never INI, logs, screenshots or
repository files. Import from a selected Mark-LV configuration validates the key
before storing it and leaves the source unchanged. No reference code or assets are bundled.

The local llama.cpp runtime is an owned loopback-only, authenticated process;
it cannot require Ollama as a global daemon. Local ASR and female voice synthetic pt-BR and
en-US round trips passed; physical microphone/accent evaluation remains separate.
Missing voice assets must leave text conversation available. Native ONNX and
GPL phonemization live in the isolated `Laura.VoiceWorker` process; distribution
includes exact licenses and the verified corresponding-source companion. Never
download weights during a normal build, restore or startup.
The Brain may also use optional local CPU ONNX sentence embeddings for retrieval.
Brain Markdown is authoritative. FTS5/vector caches live outside its shared root,
are rebuildable, and must never become the source of durable facts. New vaults
must be Git-protected before writes. Never log note bodies or YAML parse contents.

Use `Brain` for the default vault and capitalize the first letter of every newly
created Brain folder (for example `Knowledge/Facts`, `Owner/Preferences`, `Skills`
and `Archive/Revisions`). Preserve existing folder spelling, document filenames,
stable identities and links; do not rename user-authored vault contents implicitly.
Native tools use explicit structured arguments and report actual results.
Consequential operations use trusted MAUI confirmation. Capture requires an
explicit listening button, held Ctrl+Space chord, or the user's opt-in double-clap
wake setting, and an unmuted setting. The clap stream is local only, without
recording or cloud transmission, until wake starts a conversation. Disabling the
option closes local detection. Muting, stopping, early key release and shutdown
close the active provider's capture. Continuous
listening uses automatic speech-end detection. Brazilian conversation settings
must request Brazilian vocabulary and accent.

## Code Style

- **Document every method with `<summary>` in Google-style docstring form**:
  description, then `Args:` / `Returns:` / `Raises:` when applicable.
  Use `<inheritdoc />` for interface implementations.
- Comments explain **why**, not what. Follow the surrounding code's comment density.
- Apply SOLID, DRY, KISS, and Clean Code. Use descriptive names; one method does one
  thing.
- `Nullable` and `ImplicitUsings` are enabled; `TreatWarningsAsErrors` is enabled.
  The build must pass **without warnings**.
- Settings are immutable `record` types with a `Sanitized()` method that fixes
  out-of-range values; data read from disk always goes through it.
- The language of ALL code, including identifiers, comments, `<summary>`, logs,
  exception messages, and commit messages, is ENGLISH.

### Analyzers

The domain and platform projects disable `CA1848` and `CA1716` (noise for this app);
the UI also disables `WFO1000`/`CA1859` (designer and micro-performance rules). Each
`.csproj` declares this in its own `NoWarn`. Only expand `NoWarn` with a comment
justification; prefer fixing the warning.

## Settings on Disk

Settings are persisted in `Documents/Laura Virtual Assistant/settings.ini` (through
`Environment.SpecialFolder.MyDocuments`) in **INI** format on purpose, so users can
edit it by hand. Writes are UTF-8 **with BOM**, otherwise local editors corrupt
accented characters. `IniDocument` parses tolerantly (malformed lines are ignored
instead of breaking reads); `IniSettingsStore` maps INI <-> `LauraSettings`. Do not
go back to JSON or AppData. The default language is **en-US** (a feminine English
voice sounds more natural).

## MAUI UI Verification

Use the shared Theme helpers for native controls. Centralize translated UI text in
the EN/PT catalogs and keep conversation language independent from UI language.
Use `Theme.PageHeading` and `Theme.BackButton` for secondary workspace navigation,
and `Theme.ActionButton`/`Theme.SetSelected` for consistent action and tab styling.
Share spacing, typography and pointer/keyboard states instead of duplicating them
inside pages. Preserve workspace-specific layouts and navigation guards.
Use explicit grid coordinates when reparenting views during responsive layout.
Pause the animation timer while the HUD is off screen. The --ui-smoke option
renders the app itself with isolated storage; it must never capture a microphone.

## Tests

- Always build and run both **Debug and Release** for material code changes. A
  successful Debug run does not validate Release. Use isolated storage and relevant
  native smoke checks to verify actual startup and changed behavior in each
  configuration; report any unverified configuration explicitly. When delivering a
  published build, run that exact published executable as well.
- New domain logic (normalization, phrase matching, sanitization, dispatch) needs a
  test in `Laura.Core.Tests`.
- Use the fakes in `TestDoubles/` instead of mocking frameworks.
- The platform layer (audio, Win32) and UI are not covered by unit tests; keep logic
  testable in `Core`.

## Git

- Do not commit unless the user asks.
- Commit messages in English.
