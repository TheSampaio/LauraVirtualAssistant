# Third-party notices and integration provenance

Laura retains its existing LICENSE. No endorsement by the reference projects is implied.

## Reference projects (not redistributed)

- OpenJarvis, Open Jarvis Contributors, Apache-2.0, reviewed at `ecbde4b32091e2a8b02e8acfa41cb5f95e4da560`: https://github.com/open-jarvis/OpenJarvis. Architecture and capabilities informed Laura's integration assessment. No Python/Rust source is copied in this integration.
- Mark-LV, copyright (c) 2026 FatihMakes, CC BY-NC 4.0, reviewed at `c3e795af71bff38bb406c62a2e7a63869cdc8eb2`: https://github.com/FatihMakes/Mark-LV. Capability and general HUD layout reference. Laura's HUD is independently implemented; its geometric face does not use Mark-LV's mesh or assets. No source, translated code or assets are redistributed. Commercial permission has not been obtained. Canonical license: https://creativecommons.org/licenses/by-nc/4.0/legalcode.

## Existing application dependencies

| Component | Pinned version | License / source |
|---|---|---|
| .NET / Microsoft.Extensions.* / System.Speech | 9.0/10.0 package families; bundled runtime 9.0.20 | MIT, https://github.com/dotnet/runtime |
| Windows SDK APIs | target 10.0.19041.0 | Windows platform terms; supplied by Windows |
| NAudio | 2.2.1 | MIT, https://github.com/naudio/NAudio |
| LLamaSharp / CPU backend | 0.27.0 | MIT, https://github.com/SciSharp/LLamaSharp; embeds MIT llama.cpp, https://github.com/ggml-org/llama.cpp |
| Microsoft.Data.Sqlite | 9.0.10 | MIT, https://github.com/dotnet/efcore; SQLitePCLRaw bundles SQLite (public domain), https://www.sqlite.org/copyright.html |
| Whisper.net / CPU runtime | 1.9.1 | MIT, https://github.com/sandrohanea/whisper.net; MIT whisper.cpp, https://github.com/ggml-org/whisper.cpp |
| OllamaSharp | 4.0.11 | MIT, https://github.com/awaescher/OllamaSharp |
| sherpa-onnx | 1.13.4 | Apache-2.0, https://github.com/k2-fsa/sherpa-onnx |
| xUnit (development only) | 2.9.2 / runner 2.8.2 | Apache-2.0, https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk (development only) | 17.12.0 | MIT, https://github.com/microsoft/vstest |

AI weights and voices have separate distribution terms. An installed engine package does not grant permission to redistribute arbitrary models. The implementation assessment and package lock files track added dependencies; the release includes the corresponding upstream license texts, dependency inventory and model attribution under `licenses/`.

The optional reviewed Qwen 2.5 0.5B Instruct GGUF weights are provided by the Qwen team under Apache-2.0, https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF. Weights are downloaded only by user choice and are excluded from the Laura application package.

Optional multilingual Whisper base weights originate from OpenAI Whisper under MIT, https://github.com/openai/whisper, converted by Georgi Gerganov at https://huggingface.co/ggerganov/whisper.cpp. Weights are separate downloads, never microphone uploads. GGML dependencies in Whisper's native runtime use suffixed library names to coexist with llama.cpp; the smoke test exercises both engines in one process.

SQLitePCLRaw 2.1.10 is Apache-2.0 (SourceGear, LLC). CommunityToolkit.HighPerformance 8.4.2 is MIT (.NET Foundation and Contributors); System.Linq.Async / System.Interactive.Async 7.0.0 are MIT (.NET Reactive). Sherpa bundles ONNX Runtime 1.27.0 under MIT plus its third-party notices. Exact resolved versions and package SHA512 values, including transitive Microsoft 10.0.x dependencies, are recorded in `licenses/dependency-inventory.json` and the project lock files.

Microsoft Visual C++ CRT/OpenMP 14.51.36247.0 (redistributable directory 14.51.36231, Visual Studio 2026 Release channel) is bundled as unmodified, signed app-local binaries under Microsoft software license terms, separately from Laura's MIT code. The official runtime/Visual Studio license documents and distributable list are included. Builders/redistributors must hold the corresponding redistribution rights; end-user runtime terms are presented by the installer. `licenses/vc-runtime-inventory.json` pins each binary's SHA256 and version. App-local copies require Laura releases to deliver security updates; they are not serviced independently by Windows Update.
