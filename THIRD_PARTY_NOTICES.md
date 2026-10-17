# Third-party notices and integration provenance

Laura retains its existing LICENSE. No endorsement by the reference projects is implied.

## Reference projects (not redistributed)

- OpenJarvis, Open Jarvis Contributors, Apache-2.0, reviewed at `ecbde4b32091e2a8b02e8acfa41cb5f95e4da560`: https://github.com/open-jarvis/OpenJarvis. Architecture/capability reference; no Python/Rust source is bundled.
- Mark-LV, copyright (c) 2026 FatihMakes, CC BY-NC 4.0, reviewed at `c3e795af71bff38bb406c62a2e7a63869cdc8eb2`: https://github.com/FatihMakes/Mark-LV. Behavior, general HUD layout and public Live identifiers informed Laura's independent implementation. No reference code, translated code or assets are redistributed. Commercial permission has not been obtained. Canonical license: https://creativecommons.org/licenses/by-nc/4.0/legalcode.

## Application dependencies

The HUD bundles selected Lucide SVG icons from https://github.com/lucide-icons/lucide, downloaded on 2026-10-12. Only stroke colors are adapted to Laura's palette. The ISC license and Feather-derived MIT notice are preserved in `licenses/Lucide-License.txt`; no icon package or runtime dependency is added.

| Component | Pinned version | License / source |
| --- | --- | --- |
| .NET runtime / Microsoft.Extensions.* | .NET 10; exact resolved packages in inventory | MIT, https://github.com/dotnet/runtime |
| MAUI Controls/Graphics | 10.0.20 | MIT, https://github.com/dotnet/maui |
| Windows App SDK | 1.7.250909003 | Included Microsoft license/notices |
| Windows SDK APIs | target 10.0.19041.0 | Windows platform terms |
| NAudio.Core / NAudio.WinMM | 2.2.1 | MIT, https://github.com/naudio/NAudio |
| Microsoft.Data.Sqlite | 9.0.10 | MIT, https://github.com/dotnet/efcore |
| Markdig | 1.4.0 | BSD-2-Clause, https://github.com/xoofx/markdig; included license |
| YamlDotNet | 18.1.0 | MIT, https://github.com/aaubry/YamlDotNet; included license |
| ModelContextProtocol.Core | 2.2.0 | Apache-2.0 package declaration; upstream transition preserves MIT portions; pinned upstream notice in `licenses/ModelContextProtocol.Core-2.2.0-LICENSE.txt`, https://github.com/modelcontextprotocol/csharp-sdk |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | MIT; SDK transitive protocol types, not a second model integration, https://github.com/dotnet/extensions |
| SQLitePCLRaw | 2.1.12 | Apache-2.0; bundled SQLite public domain, https://www.sqlite.org/copyright.html |
| ProtectedData | 9.0.13 | MIT, https://github.com/dotnet/runtime |
| xUnit (development only) | 2.9.2 / runner 2.8.2 | Apache-2.0, https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk (development only) | 17.12.0 | MIT, https://github.com/microsoft/vstest |

Exact transitive dependency versions, declarations, repository commits and NuGet SHA512 values are in `licenses/dependency-inventory.json` and package lock files. Upstream license and third-party notice texts accompany the release. Windows Forms, System.Speech, OllamaSharp and Sherpa remain absent. Conversational inference, audio recognition and synthesis are provided by Gemini Live. No model weights or Windows voice assets are packaged.

Microsoft Visual C++ CRT 14.51.36247.0 (redistributable directory 14.51.36231, Visual Studio 2026 Release channel) is bundled as unmodified, signed app-local binaries under Microsoft software license terms, separately from Laura's MIT code. Official runtime/Visual Studio license documents and the distributable list are included. Builders/redistributors need the corresponding redistribution rights; end-user runtime terms are presented by the installer. `licenses/vc-runtime-inventory.json` pins binary SHA256/version. Updating these app-local dependencies requires republishing Laura.

Google Gemini Live is the required hosted conversation/voice service, not a redistributed model or voice asset. Its use is governed by the user's Google AI Studio account terms. Laura implements the documented Live protocol independently.

Optional ChatGPT plan authorization uses the official Sign in with ChatGPT public-client flow and the public Responses endpoint. Microsoft.IdentityModel.JsonWebTokens, Tokens, Logging and Abstractions 8.16.0 verify signed OpenID Connect tokens under the included MIT license (`licenses/Microsoft.IdentityModel-8.16.0-LICENSE.txt`). No Codex executable, conversation model or OpenAI credential is redistributed.

The optional Laura Credential Broker Windows service uses System.ServiceProcess.ServiceController and System.Diagnostics.EventLog 10.0.0 under MIT (Microsoft; dotnet/dotnet commit b0f34d51fccc69fd334253924abd8d6853fad7aa). Their package third-party notices and the .NET MIT license accompany the distribution. The service provides credential authorization and inference only; it includes no command executor.
