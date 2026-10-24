# Verify the actual installer payload using isolated workspace destinations.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$distribution = Join-Path $root '_Output/Distribution'
$run = Join-Path $root ('_Output/PackageTests-' + [Guid]::NewGuid().ToString('N'))
$extract = Join-Path $run 'Payload'
$installed = Join-Path $run 'Installed'
New-Item -ItemType Directory -Force -Path $extract | Out-Null
$process = Start-Process -FilePath (Join-Path $distribution 'LauraSetup.exe') -ArgumentList '/Q', ('/T:' + $extract), '/C' -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'Installer extraction failed.' }
$installerScript = Join-Path $extract 'Install.ps1'
& $installerScript -InstallDirectory $installed -SkipShortcut
# Exercise an update and ensure unrelated personal data outside the install survives.
$sentinel = Join-Path $run 'personal-data.txt'
Set-Content -LiteralPath $sentinel -Value 'Preserve this data.'
& $installerScript -InstallDirectory $installed -SkipShortcut
if (!(Test-Path -LiteralPath $sentinel)) { throw 'Update changed unrelated personal data.' }
foreach ($relative in @('Laura.exe', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'Microsoft.Maui.Controls.dll', 'Microsoft.UI.Xaml.dll', 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'e_sqlite3.dll', 'NAudio.Core.dll', 'NAudio.WinMM.dll', 'Localization/Locales/en-US.json', 'Localization/Locales/pt-BR.json', 'licenses/dependency-inventory.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $installed $relative))) { throw "Missing packaged dependency: $relative" }
}
foreach ($brainDependency in @('YamlDotNet.dll', 'Markdig.dll', 'licenses/YamlDotNet-18.1.0-LICENSE.txt', 'licenses/Markdig-1.4.0-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $installed $brainDependency))) { throw "Missing Brain dependency or license: $brainDependency" }
}
foreach ($mcpDependency in @('ModelContextProtocol.Core.dll', 'Microsoft.Extensions.AI.Abstractions.dll', 'licenses/ModelContextProtocol.Core-2.2.0-LICENSE.txt', 'licenses/Microsoft.Extensions.AI.Abstractions-10.8.3-LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $installed $mcpDependency))) { throw "MCP dependency/license missing: $mcpDependency" }
}
if (Get-ChildItem -LiteralPath $installed -Recurse -Directory | Where-Object { $_.Name -eq 'brain' -and !$_.FullName.StartsWith((Join-Path $installed 'VoiceSources') + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Personal Brain folders must not be distributed.' }
if (Get-ChildItem -LiteralPath $installed -Recurse -File -Filter '*.onnx') { throw 'Optional user model weights must not be distributed.' }
foreach ($removed in @('System.Windows.Forms.dll', 'System.Speech.dll', 'OllamaSharp.dll', 'LLamaSharp.dll', 'Whisper.net.dll', 'whisper.dll', 'llama.dll', 'Microsoft.ML.OnnxRuntime.dll', 'vcomp140.dll')) {
    if (Get-ChildItem -LiteralPath $installed -Recurse -File -Filter $removed) { throw "Removed integration must not ship: $removed" }
}
foreach ($relative in @('VoiceWorker/Laura.VoiceWorker.exe', 'VoiceWorker/sherpa-onnx-c-api.dll', 'VoiceWorker/onnxruntime.dll', 'VoiceSources/source-manifest.json', 'licenses/eSpeak-NG-GPL-3.0.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $installed $relative))) { throw ('Local voice component/source/license missing: ' + $relative) }
}
foreach ($privateFile in @('google-key.dat', 'api_keys.json', 'settings.ini', 'memory.db', 'chatgpt-credentials*')) {
    if (Get-ChildItem -LiteralPath $installed -Recurse -File -Filter $privateFile) { throw "Private data must not be part of the distribution: $privateFile" }
}
foreach ($component in (Get-Content -LiteralPath (Join-Path $installed 'licenses/vc-runtime-inventory.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath (Join-Path $installed $component.File) -Algorithm SHA256).Hash -ne $component.Sha256) { throw 'Installed VC runtime integrity failed.' }
}
if (Get-ChildItem -LiteralPath $installed -Recurse -File -Filter '*.gguf') { throw 'The package must not contain local conversation model weights.' }
$archive = Join-Path $extract 'Laura-win-x64.zip'
$original = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$expected = (Get-FileHash -LiteralPath (Join-Path $distribution 'Laura-win-x64.zip') -Algorithm SHA256).Hash
if ($original -ne $expected) { throw 'Installer does not contain the current portable package.' }
# Deliberately corrupt only the extracted test copy, then require refusal before installation.
Add-Content -LiteralPath $archive -Value 'tamper'
$refused = $false
try { & $installerScript -InstallDirectory (Join-Path $run 'Tampered') -SkipShortcut }
catch { if ($_.Exception.Message -match 'integrity verification') { $refused = $true } else { throw } }
if (!$refused -or (Test-Path -LiteralPath (Join-Path $run 'Tampered/Laura.exe'))) { throw 'Tampered installer payload was accepted.' }
Write-Output "Installer extraction, fresh install, update, dependency presence and tamper refusal passed: $run"
