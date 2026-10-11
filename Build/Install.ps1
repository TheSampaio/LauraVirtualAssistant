# This local script is embedded in LauraSetup.exe with the checked package.
[CmdletBinding()]
param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/Laura'), [switch]$SkipShortcut, [switch]$Launch)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-Child([string]$Path, [string]$Parent) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe installation path: $resolved" }
    $current = $resolved
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked installation path: $current" }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $resolved
}
try {
    $destination = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/')
    $parent = [IO.Path]::GetDirectoryName($destination)
    $destination = Assert-Child $destination $parent
    foreach ($running in (Get-Process -Name Laura -ErrorAction SilentlyContinue)) {
        if (!$running.Path -or $running.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Close this Laura installation before updating.' }
    }
    if ((Test-Path -LiteralPath $destination) -and !(Test-Path -LiteralPath (Join-Path $destination '.laura-install'))) { throw 'The destination already exists and is not a Laura installation.' }
    $archive = Join-Path $PSScriptRoot 'Laura-win-x64.zip'
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '__PACKAGE_SHA256__') { throw 'The embedded package failed integrity verification.' }
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $staging = Assert-Child (Join-Path $parent ('.Laura-stage-' + [Guid]::NewGuid().ToString('N'))) $parent
    $backup = Assert-Child (Join-Path $parent ('.Laura-backup-' + [Guid]::NewGuid().ToString('N'))) $parent
    New-Item -ItemType Directory -Path $staging | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.Contains(':') -or $entry.FullName.StartsWith('/') -or $entry.FullName.StartsWith('\')) { throw 'Invalid package entry.' }
            $target = Assert-Child (Join-Path $staging $entry.FullName) $staging
            if (!$entry.Name) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $zip.Dispose() }
    if (!(Test-Path -LiteralPath (Join-Path $staging 'Laura.exe'))) { throw 'Laura.exe is missing from the package.' }
    if ($Launch) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LauraInstallerDialog {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
}
"@
        $terms = 'Laura includes Microsoft C++ runtime components under the terms below. Do you accept these terms and install Laura?' + [Environment]::NewLine + [Environment]::NewLine + (Get-Content -LiteralPath (Join-Path $staging 'licenses/VisualCpp-Runtime-LICENSE.txt') -Raw)
        if ([LauraInstallerDialog]::MessageBox([IntPtr]::Zero, $terms, 'Laura Setup', 0x124) -ne 6) { return }

    }
    Set-Content -LiteralPath (Join-Path $staging '.laura-install') -Value 'Laura per-user installation' -Encoding ascii
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath (Assert-Child $destination $parent) -Destination $backup }
    try { Move-Item -LiteralPath (Assert-Child $staging $parent) -Destination $destination }
    catch { if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath (Assert-Child $backup $parent) -Destination $destination }; throw }
    if (!$SkipShortcut) {
        $shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Laura'
        New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $shortcutDirectory 'Laura.lnk'))
        $shortcut.TargetPath = Join-Path $destination 'Laura.exe'
        $shortcut.WorkingDirectory = $destination
        $shortcut.Save()
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath (Assert-Child $backup $parent) -Recurse -Force }
    if ($Launch) { Start-Process -FilePath (Join-Path $destination 'Laura.exe') -ArgumentList '--show' -WorkingDirectory $destination -WindowStyle Hidden }
    Write-Output "Laura installed: $destination"
} catch {
    if ($Launch -and ('LauraInstallerDialog' -as [type])) { [LauraInstallerDialog]::MessageBox([IntPtr]::Zero, $_.Exception.Message, 'Laura installation', 0x10) | Out-Null }
    throw
} finally {
    if ($staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath (Assert-Child $staging $parent) -Recurse -Force }
}
