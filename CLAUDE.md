# CLAUDE.md

Guidelines for working in this repository. Read this before changing code.

## What Laura Is

Laura is a personal assistant for Windows inspired by Jarvis, in a feminine version.
She lives in the system tray, speaks responses, accepts typed chat commands, and
shows the settings window with **Alt + L**. Migrated from Python to **C# / .NET 9**.

The only supported language is **English (en-US)**. Keep UI text, command
phrases, and generative AI prompts aligned with English only.

## Commands

```powershell
dotnet build Laura.sln          # build everything
dotnet test                     # run the tests
dotnet run --project Laura.App  # run (Build/Run.bat does the same)
```

`Build/Publish.bat` creates a self-contained `Laura.exe` in `_Output/Publish`.

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
| `Laura.Core` | Pure domain (net9.0): engine, skills, settings, localization | no platform |
| `Laura.Platform.Windows` | SAPI and Win32 adapters | `Core` |
| `Laura.App` | Tray + Windows Forms UI | `Core`, `Platform.Windows` |
| `Laura.Core.Tests` | xUnit tests | `Core` |

Rules:
- Every external dependency (speech, system, network, UI) enters `Core` through an
  **interface** in `Laura.Core/Abstractions`. Concrete implementations live in
  `Platform.Windows` or `App`.
- The abstraction-to-implementation connection happens **only** in
  [`ServiceConfiguration`](Laura.App/Composition/ServiceConfiguration.cs). Do not
  instantiate services with `new` outside the composition root and tests.
- For the domain to act on the UI (open a window, exit), use `IShellController`;
  never reference Windows Forms from `Core`.

## UI: Windows Forms, Not WPF or MAUI

This is the project owner's decision. **Do not migrate to WPF or MAUI.** For a
Windows-only tray app with a global hotkey, WinForms is the right fit (native
`NotifyIcon`, trivial hidden window). The UI is built **in code** (no designer). All
logic lives in `Core`, so the presentation can be replaced without touching the
domain.

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
3. Activation phrases and responses go in the **English language file**, not in
   code. Add the key to `LocalizationKeys` and the text to `Locales/en-US.json`.
4. Register it in `AddSkills` in `ServiceConfiguration`.
5. A skill should not throw for normal flow (for example, missing app -> spoken
   response, not an exception). Unexpected failures are isolated by the dispatcher.

Never hard-code spoken text or fixed triggers in a skill.

## Localization

- Text and triggers: `Laura.Core/Localization/Locales/en-US.json`.
- A key may contain a single string or a list (random response variants / multiple
  triggers).
- The app is English-only; do not add Portuguese catalogs or UI language switching.
- Keys accessed by the domain must exist in `LocalizationKeys` (typos become compile
  errors).

## Generative AI Is Optional

A command not recognized by any skill **may** go to a model through
`IConversationEngine`, but the default is `NullConversationEngine` (inert).
**Laura must work 100% offline.** Never make generative AI a required path; it is an
extra mode enabled in settings.

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

## Windows Forms UI - Solved Pitfalls (Do Not Regress)

- Custom controls (`Slider`, `ToggleSwitch`) paint over an **opaque background**
  (`Palette.Surface`/`Field`), never `Color.Transparent`; transparency in WinForms
  repaints the parent on every frame and **flickers** while dragging.
- Every content `Label` uses `UseMnemonic = false`; otherwise `&` in text such as
  "Time & language" becomes an access key and disappears from the screen.
- Input fields go through `InputFactory`/`ThemedComboBox` so they do not show the
  system's bright border and arrow over the dark theme.
- A slider's value label is initialized by reading `slider.Value`, not the range
  minimum.
- `Application.SetUnhandledExceptionMode` must be called **before** any control
  exists (at the start of `Main`), or it throws at runtime.
- Card layout: `TableLayoutPanel` with `Dock = Fill` in a 100% column, so every row
  has the same width.

## Tests

- New domain logic (normalization, phrase matching, sanitization, dispatch) needs a
  test in `Laura.Core.Tests`.
- Use the fakes in `TestDoubles/` instead of mocking frameworks.
- The platform layer (SAPI, Win32) and UI are not covered by unit tests; keep logic
  testable in `Core`.

## Git

- Do not commit unless the user asks.
- Commit messages in English.
