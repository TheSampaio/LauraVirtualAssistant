# Laura - Virtual Assistant

Laura is a personal assistant for Windows inspired by Jarvis, in a feminine version.
She lives in the system tray, responds by voice to **"Ok, Laura"** or
**"Hey Laura"**, and handles simple everyday tasks: telling the time and date,
searching the web, opening applications, adjusting the volume, and locking the
computer.

The interface stays hidden and appears with **Alt + L**, where you can change the
voice, language, tone, and other preferences. All listening and speaking happens
off the UI thread, so the window never freezes while Laura listens or answers.

## Requirements

- Windows 10 or 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build
- A microphone and the speech pack for the desired language
  (Windows Settings > Time & language > Speech) for voice commands

Without a microphone or an installed recognizer, Laura remains usable through the
settings window only.

## How to Run

```powershell
dotnet run --project Laura.App
```

Or use the scripts in `Build/`:

- `Build/Run.bat` - builds and runs in debug mode
- `Build/Publish.bat` - creates a self-contained `Laura.exe` in `_Output/Publish`

## Architecture

The solution separates domain, platform, and presentation so the assistant's rules
stay independent from Windows and the graphical interface.

| Project | Responsibility |
| --- | --- |
| `Laura.Core` | Pure domain: settings, localization, skills, and the assistant engine. No platform dependencies. |
| `Laura.Platform.Windows` | Windows adapters: speech synthesis and recognition (SAPI), volume control, automatic startup. |
| `Laura.App` | Tray application and settings window in Windows Forms. |
| `Laura.Core.Tests` | Domain unit tests. |

The engine talks to the outside world only through interfaces (`ISpeechSynthesizer`,
`ISpeechRecognizer`, `ISkill`, `ISettingsService`, ...), and the composition root in
[`ServiceConfiguration`](Laura.App/Composition/ServiceConfiguration.cs) connects each
abstraction to its implementation.

### Skills

Each capability is an `ISkill` implementation that decides whether a command belongs
to it. Adding a new capability means registering one more skill without touching the
engine or the existing skills. The phrases that trigger each skill live in language
files, not in code.

### Languages

Text and triggers live in `Laura.Core/Localization/Locales/<culture>.json`.
Translating Laura or adding another way to ask for something means editing a language
file. Portuguese (Brazil) and English (United States) are included.

### Generative Mode (Planned)

The engine already anticipates an optional generative AI add-on: commands no skill
recognizes may later be forwarded to a local model such as
[Ollama](https://ollama.com/) through the `IConversationEngine` interface. Today the
default registration is inert (`NullConversationEngine`) - **Laura works entirely
offline**, and generative AI will only be an extra mode to enable in settings.

## Tests

```powershell
dotnet test
```
