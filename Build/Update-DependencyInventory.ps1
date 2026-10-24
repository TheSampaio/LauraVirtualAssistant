[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$assetsPath = Join-Path $repository 'Laura.App/obj/project.assets.json'
if (!(Test-Path -LiteralPath $assetsPath)) { throw 'Restore the application before refreshing its dependency inventory.' }
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$allLibraries = @{}
foreach ($projectAssets in @($assetsPath, (Join-Path $repository 'Laura.VoiceWorker/obj/project.assets.json'))) {
    $projectData = Get-Content -LiteralPath $projectAssets -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in $projectData.libraries.Keys) { $allLibraries[$key] = $projectData.libraries[$key] }
}
$inventory = foreach ($identity in ($allLibraries.Keys | Sort-Object)) {
    $library = $allLibraries[$identity]
    if ($library.type -ne 'package') { continue }
    $packagePath = $null
    foreach ($cacheRoot in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cacheRoot $library.path
        if (Test-Path -LiteralPath $candidate) { $packagePath = $candidate; break }
    }
    if (!$packagePath) { throw "Restored package missing: $identity" }
    $specPath = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
    [xml]$specification = Get-Content -LiteralPath $specPath.FullName -Raw
    $metadata = $specification.package.metadata
    $hashPath = Get-ChildItem -LiteralPath $packagePath -Filter '*.nupkg.sha512' -File | Select-Object -First 1
    [ordered]@{
        Package = [string]$metadata.id
        Version = [string]$metadata.version
        License = [string]$metadata.license.InnerText
        Copyright = [string]$metadata.copyright
        Repository = [string]$metadata.repository.url
        Commit = [string]$metadata.repository.commit
        NugetSHA512 = (Get-Content -LiteralPath $hashPath.FullName -Raw).Trim()
    }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repository 'licenses/dependency-inventory.json') -Encoding utf8
Write-Output "Dependency inventory refreshed from restored application assets: $($inventory.Count) packages; no cache paths or credentials retained."
