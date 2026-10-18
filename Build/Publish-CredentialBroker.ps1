# Produces an offline self-contained credential-service payload; does not install or elevate.
[CmdletBinding()]
param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root '_Output/CredentialBroker' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $root '_Output')).TrimEnd('\') + '\'
if (!$output.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Service publish output must stay inside the workspace output.' }
for ($current = $output; $current; $current = Split-Path $current -Parent) {
    $entry = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
    if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Service output cannot traverse redirected entries.' }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $root 'Laura.CredentialBroker/Laura.CredentialBroker.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o $output -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Credential service publish failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File | Copy-Item -Destination $output -Force
$manifest = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'broker-manifest.json' | ForEach-Object { [ordered]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'broker-manifest.json') -Encoding UTF8
Write-Output "Credential service ready for reviewed administrative installation: $output"
