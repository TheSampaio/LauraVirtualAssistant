# Third-party notices and integration provenance

Laura retains its existing LICENSE. No endorsement by the reference projects is implied.

## Reference projects (not redistributed)

- OpenJarvis, Open Jarvis Contributors, Apache-2.0, reviewed at `ecbde4b32091e2a8b02e8acfa41cb5f95e4da560`: https://github.com/open-jarvis/OpenJarvis. Architecture/capability reference; no Python/Rust source is bundled.
- Mark-LV, copyright (c) 2026 FatihMakes, CC BY-NC 4.0, reviewed at `c3e795af71bff38bb406c62a2e7a63869cdc8eb2`: https://github.com/FatihMakes/Mark-LV. Behavior, general HUD layout and public Live identifiers informed Laura's independent implementation. No reference code, translated code or assets are redistributed. Commercial permission has not been obtained. Canonical license: https://creativecommons.org/licenses/by-nc/4.0/legalcode.

## Application dependencies

| Component | Pinned version | License / source |
| --- | --- | --- |
| .NET runtime / Microsoft.Extensions.* | .NET 10; exact resolved packages in inventory | MIT, https://github.com/dotnet/runtime |
| MAUI Controls/Graphics | 10.0.20 | MIT, https://github.com/dotnet/maui |
| Windows App SDK | 1.7.250909003 | Included Microsoft license/notices |
| Windows SDK APIs | target 10.0.19041.0 | Windows platform terms |
| NAudio.Core / NAudio.WinMM | 2.2.1 | MIT, https://github.com/naudio/NAudio |
| Microsoft.Data.Sqlite | 9.0.10 | MIT, https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.12 | Apache-2.0; bundled SQLite public domain, https://www.sqlite.org/copyright.html |
| ProtectedData | 9.0.13 | MIT, https://github.com/dotnet/runtime |
| xUnit (development only) | 2.9.2 / runner 2.8.2 | Apache-2.0, https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk (development only) | 17.12.0 | MIT, https://github.com/microsoft/vstest |

Exact transitive dependency versions, declarations, repository commits and NuGet SHA512 values are in `licenses/dependency-inventory.json` and package lock files. Upstream license and third-party notice texts accompany the release. Historical attribution files for removed packages remain in the license archive; they do not indicate that those engines ship. Windows Forms, System.Speech, LLamaSharp, Whisper, OllamaSharp and Sherpa have been removed from the runtime dependency graph. No local AI weights or voice assets are packaged.

Microsoft Visual C++ CRT/OpenMP 14.51.36247.0 (redistributable directory 14.51.36231, Visual Studio 2026 Release channel) is bundled as unmodified, signed app-local binaries under Microsoft software license terms, separately from Laura's MIT code. Official runtime/Visual Studio license documents and the distributable list are included. Builders/redistributors need the corresponding redistribution rights; end-user runtime terms are presented by the installer. `licenses/vc-runtime-inventory.json` pins binary SHA256/version. Updating these app-local dependencies requires republishing Laura.

Google Gemini Live is the required hosted conversation/voice service, not a redistributed model or voice asset. Its use is governed by the user's Google AI Studio account terms. Laura implements the documented Live protocol independently.
