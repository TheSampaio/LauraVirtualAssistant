# Build Laura's self-contained Windows package and embedded offline installer.
[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$Repackage,
    [string]$VcRedistDirectory = (Join-Path $env:ProgramFiles 'Microsoft Visual Studio/18/Community/VC/Redist/MSVC/14.51.36231/x64')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$distribution = Join-Path $root '_Output/Distribution'
$publish = Join-Path $distribution 'Laura-win-x64'
$payload = Join-Path $distribution 'InstallerPayload'
# Every destructive operation stays under this checkout's owned output, including existing ancestor directories.
function Assert-OwnedOutput([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root '_Output')).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output escaped the owned workspace.' }
    $ancestor = $resolved
    while ($ancestor) {
        $entry = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Publish output cannot traverse a redirected path.' }
        if ($ancestor -eq $root) { break }
        $ancestor = Split-Path $ancestor -Parent
    }
    return $resolved
}
function Assert-OwnedOutputTree([string]$Path) {
    $resolved = Assert-OwnedOutput $Path
    if (Test-Path -LiteralPath $resolved -PathType Container) {
        $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($resolved)
        while ($pending.Count -gt 0) {
            foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Cannot replace a publish tree containing redirected entries.' }
                if ($entry.PSIsContainer) { $pending.Push($entry.FullName) }
            }
        }
    }
    return $resolved
}
$distribution = Assert-OwnedOutput $distribution
$publish = Assert-OwnedOutputTree $publish
$payload = Assert-OwnedOutput $payload
if (!$Repackage -and (Test-Path -LiteralPath $publish)) {
    $resolvedPublish = [IO.Path]::GetFullPath($publish)
    $resolvedDistribution = [IO.Path]::GetFullPath($distribution).TrimEnd('\') + '\'
    if (!$resolvedPublish.StartsWith($resolvedDistribution, [StringComparison]::OrdinalIgnoreCase) -or
        ((Get-Item -LiteralPath $publish -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe publish directory.' }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publish, $payload | Out-Null
Push-Location $root
try {
    if ($Repackage) {
        # Repackage only an intact verified binary tree; this cannot legitimize changed application binaries.
        & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -PackageDirectory $publish
    } else {
        & dotnet restore Laura.sln --locked-mode -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
        & dotnet publish Laura.App/Laura.App.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true --no-restore -m:1 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $publish
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        # Some packages expose runtime-specific content; current Windows assets may instead be flattened at publish time.
        $nativeRoot = Join-Path $publish 'runtimes'
        if (Test-Path -LiteralPath $nativeRoot -PathType Container) {
            foreach ($runtime in Get-ChildItem -LiteralPath $nativeRoot -Directory) {
                if ($runtime.Name -ne 'win-x64') { Remove-Item -LiteralPath (Assert-OwnedOutputTree $runtime.FullName) -Recurse -Force }
            }
        }
        # App-local CRT avoids a separate system-wide prerequisite installation.
        # The builder must have redistribution rights under the supplied Microsoft terms.
        $crtInventory = Get-Content -LiteralPath (Join-Path $root 'licenses/vc-runtime-inventory.json') -Raw | ConvertFrom-Json
        foreach ($component in $crtInventory) {
            $source = Join-Path $VcRedistDirectory $component.RelativePath
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $component.Sha256) { throw "VC runtime version/hash mismatch: $source" }
            Copy-Item -LiteralPath $source -Destination (Join-Path $publish $component.File)
        }
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'), (Join-Path $root 'README.md') -Destination $publish
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $publish -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $publish -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Verify-Package.ps1') -Destination $publish
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -PackageDirectory $publish -CreateManifest
    $archive = Join-Path $distribution 'Laura-win-x64.zip'
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    Set-Content -LiteralPath (Join-Path $distribution 'SHA256SUMS.txt') -Value "$hash  Laura-win-x64.zip" -Encoding ascii
    if (!$SkipInstaller) {
        Copy-Item -LiteralPath $archive -Destination $payload -Force
        $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Raw
        Set-Content -LiteralPath (Join-Path $payload 'Install.ps1') -Value ($template.Replace('__PACKAGE_SHA256__', $hash)) -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.cmd') -Destination $payload -Force
        $installer = Join-Path $distribution 'LauraSetup.exe'
        & (Join-Path $PSScriptRoot 'Build-OfflineInstaller.ps1') -PayloadDirectory $payload -InstallerPath $installer
        $installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        Add-Content -LiteralPath (Join-Path $distribution 'SHA256SUMS.txt') -Value "$installerHash  LauraSetup.exe" -Encoding ascii
    }
    Write-Output "Distribution ready: $distribution"
} finally { Pop-Location }
