# Extract and verify the generated single-cabinet installer without launching its setup program.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Installer, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (!$outputRoot.StartsWith((Join-Path $repository '_Output') + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Installer verification requires a new owned output directory.' }
$ancestor = $outputRoot
while ($ancestor) {
    $entry = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
    if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installer output cannot traverse redirected paths.' }
    if ($ancestor -eq $repository) { break }
    $ancestor = Split-Path $ancestor -Parent
}
$source = [IO.File]::OpenRead([IO.Path]::GetFullPath($Installer))
try {
    $header = New-Object byte[] ([Math]::Min(1048576, $source.Length))
    $read = 0
    while ($read -lt $header.Length) {
        $amount = $source.Read($header, $read, $header.Length - $read)
        if ($amount -eq 0) { throw 'Installer header is truncated.' }; $read += $amount
    }
    $cabinetOffset = -1; $cabinetSize = 0
    for ($offset = 0; $offset -lt $header.Length - 44; $offset++) {
        if ($header[$offset] -ne 77 -or $header[$offset+1] -ne 83 -or $header[$offset+2] -ne 67 -or $header[$offset+3] -ne 70 -or $header[$offset+24] -ne 3 -or $header[$offset+25] -ne 1) { continue }
        $size = [BitConverter]::ToUInt32($header, $offset+8)
        $position = [long]$offset + [BitConverter]::ToUInt32($header, $offset+16)
        if ($size -lt 44 -or $size -gt [int]::MaxValue -or [long]$offset + $size -gt $source.Length -or [BitConverter]::ToUInt16($header, $offset+28) -ne 3 -or
            [BitConverter]::ToUInt16($header, $offset+30) -ne 0) { continue }
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $expandedBytes = 0L
        for ($file = 0; $file -lt 3; $file++) {
            if ($position -lt $offset + 36 -or $position + 17 -ge $header.Length) { break }
            $expandedBytes += [BitConverter]::ToUInt32($header, $position)
            if ($expandedBytes -gt [int]::MaxValue) { break }
            $nameStart = $position + 16; $nameEnd = $nameStart
            while ($nameEnd -lt $header.Length -and $nameEnd - $nameStart -lt 64 -and $header[$nameEnd] -ne 0) { $nameEnd++ }
            if ($nameEnd -ge $header.Length -or $nameEnd - $nameStart -ge 64) { break }
            $name = [Text.Encoding]::ASCII.GetString($header, $nameStart, $nameEnd - $nameStart)
            if ($name -cnotin @('Laura-win-x64.zip', 'Install.ps1', 'Install.cmd') -or !$names.Add($name)) { break }
            $position = $nameEnd + 1
        }
        if ($names.Count -eq 3) { $cabinetOffset = $offset; $cabinetSize = $size; break }
    }
    if ($cabinetOffset -lt 0) { throw 'Installer has no bounded single cabinet with the exact expected payload names.' }
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
    $cabinet = Join-Path $outputRoot 'payload.cab'
    $destination = [IO.File]::Open($cabinet, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $source.Position = $cabinetOffset; $remaining = [long]$cabinetSize; $buffer = New-Object byte[] 131072
        while ($remaining -gt 0) {
            $amount = $source.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($amount -eq 0) { throw 'Installer cabinet is truncated.' }
            $destination.Write($buffer, 0, $amount); $remaining -= $amount
        }
    } finally { $destination.Dispose() }
} finally { $source.Dispose() }
$payload = Join-Path $outputRoot 'Payload'
New-Item -ItemType Directory -Path $payload | Out-Null
& "$env:WINDIR/System32/expand.exe" '-F:*' $cabinet $payload | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Native cabinet extraction failed.' }
$script = Get-Content -LiteralPath (Join-Path $payload 'Install.ps1') -Raw
$expected = [regex]::Match($script, "\.Hash -ne '([A-Fa-f0-9]{64})'")
if (!$expected.Success) { throw 'Extracted installer lacks its fixed package hash.' }
$actual = (Get-FileHash -LiteralPath (Join-Path $payload 'Laura-win-x64.zip') -Algorithm SHA256).Hash
if ($actual -ne $expected.Groups[1].Value) { throw 'Actual embedded ZIP does not match the extracted installer hash.' }
[ordered]@{ InstallerSha256 = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash; CabinetBytes = $cabinetSize;
    EmbeddedZipSha256 = $actual; PayloadFiles = 3; NeverLaunched = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'installer-verification.json') -Encoding UTF8
Write-Output 'Verified actual SFX cabinet extraction and embedded ZIP hash without launching setup.'
