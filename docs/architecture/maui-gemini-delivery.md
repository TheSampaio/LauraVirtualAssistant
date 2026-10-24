# MAUI and Gemini Live delivery

> **Laura 2.2 runtime notice (2026-10-20):** Live/audio behavior and measurements below describe historical implementations. Current operation uses local Whisper/Kokoro, supervised local inference and optional text providers. See [current architecture](laura-local-first.md) and [implementation evidence](../../LAURA-2.2-MIGRATION.md). Historical results are not new acceptance measurements.

Windows Forms was replaced with .NET 10 / .NET MAUI hosted by native WinUI. The reactor is a GraphicsView with counter-rotating rings, microphone/output amplitude, red listening pulse matching idle and bright red speaking glow. The HUD measures actual CPU, RAM, disk occupancy, network throughput and GPU engine utilization. Setup is split into four tabs with fixed Save controls; no offline model/provider or Windows voice controls remain.

## Speech and input

Google AI Studio is the sole provider. Credentials use Windows DPAPI. Laura independently implements public Gemini Live models/native voices and can explicitly import the reference key/voice without changing its source configuration. Native audio models use a system instruction for regional language; pt-BR explicitly requests Brazilian accent, vocabulary and pronunciation. The server can still vary its delivery.

Continuous input enables automatic activity detection with a configurable 300–2000 ms silence interval (default 650 ms), high speech-start/end sensitivity and 150 ms prefix padding. Continuous mode needs no stop click for each answer. Global Ctrl+Space instead starts manual activity only while held; release closes capture and sends activityEnd. A pending connection is cancelled on early release. There is no microphone activation at startup. M mute, Esc interrupt and F11 fullscreen are focused-window controls; plain M is suppressed in text editors.

Native output is 24 kHz PCM, measured at the output reader rather than assuming received packets are already audible. Session compression/resumption, bounded saved history and transcript events preserve conversation. Continuous microphone packets are gated while playback/tail is active to reduce echo. Full-duplex acoustic echo cancellation and unrestricted barge-in are not implemented.

Protocol decisions follow the [official Live capabilities](https://ai.google.dev/gemini-api/docs/live-api/capabilities), [tool documentation](https://ai.google.dev/gemini-api/docs/live-api/tools) and [Live API reference](https://ai.google.dev/api/live). Deployment follows [MAUI unpackaged Windows deployment](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli?view=net-maui-10.0).

## Visual polish and credential indication

The title bar uses an explicit MAUI TitleBar with background #100d12 and white foreground, alongside native caption button colors. Native AppWindow colors alone do not style MAUI's overlaid XAML title layer. The Windows bootstrap requests Dark theme before creating that layer. Verification includes a real active-window caption capture: background pixels are (16, 13, 18), title glyphs reach (255, 255, 255), and the three native buttons render correctly. GPU use comes from Windows PDH GPU Engine counters: processes on one physical engine are summed, then the busiest engine defines the displayed percentage. Missing driver counters display Unavailable. A new key is checked with a Live setup handshake before being saved. A fixed bullet placeholder represents the protected saved key; saving unchanged settings cannot write that mask as a credential. Native smoke checks confirmed real GPU telemetry, retained dark chrome, successful key masking and rejection of an invalid replacement while preserving the old key.

## Application launch diagnosis

The previous skill accepted only a small static application catalog, and the Live session exposed only recognized local commands. General names such as PCSX2 could therefore return a refusal before reaching Windows. A discovered application catalog now scans Start Menu/Desktop shortcuts, App Paths, uninstall DisplayIcon and portable folders. Natural requests route through Gemini's explicit open_app/list_apps functions. System uninstall/setup executables are excluded from launch candidates; exact existing paths bypass fuzzy name matching.

On this machine the successful targets are the Steam Start Menu shortcut and Desktop PCSX2 shortcut. Native processes resolved to `C:\Program Files (x86)\Steam\steam.exe` and `C:\Program Files\Emulators\PCSX2\pcsx2-qt.exe`. A real Gemini conversation selected both open_app calls and launched both applications.

## Capability comparison

| Reference capability | Laura implementation |
| --- | --- |
| Live native voice, transcripts, continuous turns, hold-to-talk | Google-only client and five native voices |
| Real installed/portable app launch | Discovered Windows targets; verified Steam and PCSX2 |
| Desktop mouse/keyboard/window control | Structured native adapters with screen verification |
| Browser/search/current information | Default browser navigation and Google Search grounding |
| Screen and camera vision | One confirmed JPEG frame attached to the Live context |
| File processing/editing/development | Text/code reading, confirmed reversible file actions, actual executable exit/output |
| Memory and undo | SQLite facts/history and guarded file backups/trash |
| System controls | Volume/media, lock, desktop/window, theme/wallpaper and confirmed power actions |
| Persistent reminders | Local schedules; native voice while app runs, retry on service failure |
| Messaging/site interactions | General browser/desktop tools; consequential submission requires confirmation |
| Hardware telemetry | Actual CPU/RAM/disk/network/GPU meters; GPU temperature alerts not implemented |
| Native OS reminders when app is closed | Not implemented |
| Proactive news/topic watching and session summaries | Not implemented |
| Embedded video playback, arbitrary Python plugins, phone dashboard | Not implemented |
| Reference dev-agent/browser automation internals | General desktop/file/program tools; reference subsystem code not ported |
| Wake word | Not implemented; explicit button/global hold capture only |

This is substantial desktop capability coverage, not complete parity with every reference subsystem. Mark-LV Python code and assets are not bundled. The Windows x64 shipping target requires platform adapters before other MAUI targets can be supported.

## Verification

- Release solution builds with zero warnings/errors; 80 domain tests cover settings, protocol, automatic detection/regional instructions, key transitions, app matching, PCM levels, memory privacy/routing and legacy skills.
- Real API checks exercised all five voices, follow-up conversation and interrupted reconnect. A synthetic Brazilian question was transcribed as “Quanto é 2 + 2?” and answered “É quatro” after silence without activityEnd/audioStreamEnd. Playback was muted; no physical microphone was opened.
- End-to-end natural conversation “Abra a Steam e o meu emulador de PS2, PCSX2.” selected and executed both actual launch tools.
- Native render checks include HUD/resource meters, red listening/speaking motion, fullscreen, compact layout, all four Setup tabs and Memory in EN/PT.
- Package checks cover fresh/update installation, unrelated-data preservation, dependency/CRT hashes, absence of removed providers/private credentials and corrupted-archive refusal.

Artifacts are ignored under `_Output`. Physical microphone behavior and perceived accent remain user/device-dependent checks; the server-side turn transition was exercised against the real endpoint using explicit synthetic audio.
