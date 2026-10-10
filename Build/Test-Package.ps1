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
foreach ($relative in @('Laura.exe', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll', 'e_sqlite3.dll', 'runtimes/win-x64/whisper.dll', 'runtimes/win-x64/native/avx2/llama.dll', 'Localization/Locales/en-US.json', 'Localization/Locales/pt-BR.json', 'licenses/dependency-inventory.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $installed $relative))) { throw "Missing packaged dependency: $relative" }
}
foreach ($component in (Get-Content -LiteralPath (Join-Path $installed 'licenses/vc-runtime-inventory.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath (Join-Path $installed $component.File) -Algorithm SHA256).Hash -ne $component.Sha256) { throw 'Installed VC runtime integrity failed.' }
}
if (Get-ChildItem -LiteralPath $installed -Recurse -File -Filter '*.gguf') { throw 'Model weights must be separate downloads.' }
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
