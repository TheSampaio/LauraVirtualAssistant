# Third-party notices and integration provenance

Laura retains its existing LICENSE. No endorsement by the reference projects is implied.

## Reference projects (not redistributed)

- OpenJarvis, Open Jarvis Contributors, Apache-2.0, reviewed at `ecbde4b32091e2a8b02e8acfa41cb5f95e4da560`: https://github.com/open-jarvis/OpenJarvis. Architecture and capabilities informed Laura's integration assessment. No Python/Rust source is copied at this stage.
- Mark-LV, copyright (c) 2026 FatihMakes, CC BY-NC 4.0, reviewed at `c3e795af71bff38bb406c62a2e7a63869cdc8eb2`: https://github.com/FatihMakes/Mark-LV. Behavioral reference only. No source, translated code or assets are redistributed. Commercial permission has not been obtained. Canonical license: https://creativecommons.org/licenses/by-nc/4.0/legalcode.

## Existing application dependencies

| Component | Pinned version | License / source |
|---|---|---|
| .NET / Microsoft.Extensions.* / System.Speech | 9.0.0 package family | MIT, https://github.com/dotnet/runtime |
| Windows SDK APIs | target 10.0.19041.0 | Windows platform terms; supplied by Windows |
| NAudio | 2.2.1 | MIT, https://github.com/naudio/NAudio |
| OllamaSharp | 4.0.11 | MIT, https://github.com/awaescher/OllamaSharp |
| sherpa-onnx | 1.13.4 | Apache-2.0, https://github.com/k2-fsa/sherpa-onnx |
| xUnit (development only) | 2.9.2 / runner 2.8.2 | Apache-2.0, https://github.com/xunit/xunit |
| Microsoft.NET.Test.Sdk (development only) | 17.12.0 | MIT, https://github.com/microsoft/vstest |

AI weights and voices have separate distribution terms. An installed engine package does not grant permission to redistribute arbitrary models. The implementation assessment and package lock files track added dependencies; release packaging must include the corresponding upstream license texts and model attribution.
