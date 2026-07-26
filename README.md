# Laura - Virtual Assistant

Laura is a personal assistant for Windows inspired by Jarvis, in a feminine version.
She lives in the system tray, speaks responses, and handles simple everyday tasks:
telling the time and date, searching the web, opening applications, adjusting the
volume, and locking the computer.

The interface stays hidden and appears with **Alt + L**, where you can change the
voice, tone, generative AI mode, and other preferences. Speech and chat processing
happen off the UI thread, so the window never freezes while Laura answers.

## Requirements

- Windows 10 or 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build

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
| `Laura.Platform.Windows` | Windows adapters: speech synthesis, volume control, automatic startup. |
| `Laura.App` | Tray application and settings window in Windows Forms. |
| `Laura.Core.Tests` | Domain unit tests. |

The engine talks to the outside world only through interfaces (`ISpeechSynthesizer`,
`ISkill`, `ISettingsService`, ...), and the composition root in
[`ServiceConfiguration`](Laura.App/Composition/ServiceConfiguration.cs) connects each
abstraction to its implementation.

### Skills

Each capability is an `ISkill` implementation that decides whether a command belongs
to it. Adding a new capability means registering one more skill without touching the
engine or the existing skills. The phrases that trigger each skill live in language
files, not in code.

### Language

Laura is English-only. Text and triggers live in
`Laura.Core/Localization/Locales/en-US.json`.

### Generative Mode

Generative AI is optional. When enabled, typed chat commands that no built-in skill
recognizes are forwarded to a local model through the `IConversationEngine`
interface. When disabled, the chat page remains a read-only conversation history and
Laura keeps working through the built-in offline skills.

## Tests

```powershell
dotnet test
```
