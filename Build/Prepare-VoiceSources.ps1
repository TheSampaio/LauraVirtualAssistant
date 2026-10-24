# Prepare the redistributable corresponding-source companion without downloading model weights.
[CmdletBinding()]
param([switch]$Download)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $repository '_Output/VoiceSources'
$upstream = Join-Path $destination 'Upstream'
New-Item -ItemType Directory -Force -Path $upstream | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'voice-source-assets.json') -Raw | ConvertFrom-Json
foreach ($asset in $assets) {
    if ($asset.Sha256 -notmatch '^[a-f0-9]{64}$' -or $asset.File -match '[/\\:]') { throw 'Invalid pinned source asset.' }
    $target = Join-Path $upstream $asset.File
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) {
        if (!$Download) { throw ('Prepare source archives explicitly with -Download: ' + $asset.File) }
        Invoke-WebRequest -Uri $asset.Url -OutFile ($target + '.partial')
        if ((Get-FileHash -LiteralPath ($target + '.partial') -Algorithm SHA256).Hash -ne $asset.Sha256) { throw 'Source archive integrity failed.' }
        Move-Item -LiteralPath ($target + '.partial') -Destination $target
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $asset.Sha256) { throw 'Source archive integrity failed.' }
}
# Include the current exact managed worker and all source needed by its Core reference, with lock files and license texts.
foreach ($project in @('Laura.VoiceWorker', 'Laura.Core')) {
    $projectRoot = Join-Path $repository $project
    foreach ($source in Get-ChildItem -LiteralPath $projectRoot -Recurse -File | Where-Object { $_.FullName.Substring($projectRoot.Length) -notmatch '\\(bin|obj)\\' }) {
        $relative = $source.FullName.Substring($repository.Length + 1)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $target -Force
    }
}
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE'), (Join-Path $repository 'global.json'), (Join-Path $PSScriptRoot 'voice-source-assets.json'), (Join-Path $repository 'docs/architecture/local-voice-sources.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $repository 'licenses') -Destination $destination -Recurse -Force
$entries = Get-ChildItem -LiteralPath $destination -Recurse -File | Where-Object Name -ne 'source-manifest.json' | ForEach-Object {
    [ordered]@{ File = $_.FullName.Substring($destination.Length + 1).Replace('\', '/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $destination 'source-manifest.json') -Encoding utf8
Write-Output ('Verified voice source companion: ' + $entries.Count + ' files.')
