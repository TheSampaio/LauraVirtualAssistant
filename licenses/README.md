# License bundle

These are unmodified upstream license and third-party attribution texts. `dependency-inventory.json` records the current resolved application NuGet graph, license declarations, package hashes, repositories and commits where supplied. Some upstream packages resolve additional architectures; the supported release is Windows x64. Lock files record the graph. `upstream-sources.json` identifies separately retrieved official texts; other texts were extracted from NuGet packages.

The MAUI package includes .NET 10, Windows App SDK, NAudio and SQLite. Brain adds YamlDotNet (MIT), Markdig (BSD-2-Clause) and ONNX Runtime (MIT with third-party notices). Optional sentence model weights are user-provided and are not packaged. SQLite itself is public domain; its wrapper is Apache-2.0. See https://www.sqlite.org/copyright.html. Microsoft MIT packages also include their third-party notices; Windows App SDK/WebView2 have separate included terms.

Historical licenses for removed local AI/audio dependencies and model attribution remain archived here for provenance. LLamaSharp, Whisper, Sherpa, OllamaSharp, Windows Forms and System.Speech are no longer distributed runtime dependencies. No local model weights or reference-project source/assets ship.

Microsoft CRT/OpenMP binaries are proprietary redistributable components, not MIT. Official runtime/Visual Studio terms and the distributable list are included. Signed Release-channel files are pinned in `vc-runtime-inventory.json` and copied only from the builder's licensed redistributable directory. They are not committed; security updates require updating the manifest and republishing.
