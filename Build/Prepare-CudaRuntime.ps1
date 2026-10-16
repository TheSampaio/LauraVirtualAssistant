[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$preparedRoot = [IO.Path]::GetFullPath((Join-Path $repository '_Output/Dependencies/Cuda12'))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $repository '_Output/Dependencies/Downloads'))
foreach ($target in @($preparedRoot, $cacheRoot)) {
    if (!$target.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'CUDA dependency output escaped the repository.' }
    $ancestor = $target
    while ($ancestor.Length -ge $repository.Length) {
        $entry = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'CUDA dependency output cannot traverse a redirected directory.' }
        if ($ancestor -eq $repository) { break }
        $ancestor = Split-Path $ancestor -Parent
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}
$packages = @(
    @{ Name = 'cuda_cudart'; Version = '12.9.79'; Size = 3521238L; Sha256 = '179e9c43b0735ffe67207b3da556eb5a0c50f3047961882b7657d3b822d34ef8'; Files = @('cudart64_12.dll') },
    @{ Name = 'libcublas'; Version = '12.9.1.4'; Size = 549755186L; Sha256 = 'd534d98b0b453a98914dbf3adf47d7e84b55037abf02f87466439e1dcef581ed'; Files = @('cublas64_12.dll', 'cublasLt64_12.dll') }
)
Add-Type -AssemblyName System.IO.Compression
$inventory = @()
foreach ($package in $packages) {
    $stem = $package.Name + '-windows-x86_64-' + $package.Version + '-archive'
    $archivePath = Join-Path $cacheRoot ($stem + '.zip')
    $uri = 'https://developer.download.nvidia.com/compute/cuda/redist/' + $package.Name + '/windows-x86_64/' + $stem + '.zip'
    if (Test-Path -LiteralPath $archivePath) {
        if ((Get-Item -LiteralPath $archivePath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'CUDA download cache cannot contain redirected files.' }
    }
    $valid = (Test-Path -LiteralPath $archivePath) -and (Get-Item -LiteralPath $archivePath).Length -eq $package.Size
    if ($valid) { $valid = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -eq $package.Sha256 }
    if (!$valid) {
        Write-Host ('Downloading pinned NVIDIA redistributable: ' + $package.Name + ' ' + $package.Version)
        Invoke-WebRequest -Uri $uri -OutFile $archivePath -MaximumRedirection 0
    }
    if ((Get-Item -LiteralPath $archivePath).Length -ne $package.Size -or (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $package.Sha256) {
        throw 'CUDA redistributable length or SHA-256 verification failed.'
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($name in $package.Files + @('LICENSE')) {
            $selectedEntries = @($archive.Entries | Where-Object { $_.FullName -eq ($stem + '/bin/' + $name) -or ($name -eq 'LICENSE' -and ($_.FullName -eq ($stem + '/LICENSE') -or $_.FullName -eq ($stem + '/LICENSE.txt'))) })
            if ($selectedEntries.Count -ne 1 -or $selectedEntries[0].Length -gt 1073741824) { throw ('CUDA archive is missing a unique bounded reviewed entry: ' + $name) }
            $outputName = if ($name -eq 'LICENSE') { $package.Name + '-LICENSE.txt' } else { $name }
            $outputPath = Join-Path $preparedRoot $outputName
            if ((Test-Path -LiteralPath $outputPath) -and ((Get-Item -LiteralPath $outputPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'CUDA output cannot replace a redirected file.' }
            $inputStream = $selectedEntries[0].Open()
            try {
                $outputStream = [IO.File]::Open($outputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            } finally { $inputStream.Dispose() }
            $inventory += [ordered]@{ File = $outputName; Size = (Get-Item -LiteralPath $outputPath).Length; Sha256 = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant(); Source = $uri; ArchiveSha256 = $package.Sha256; Version = $package.Version }
        }
    } finally { $archive.Dispose() }
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $preparedRoot 'runtime-inventory.json') -Encoding UTF8
Write-Host 'Verified CUDA runtime and cuBLAS DLLs prepared locally with original license files. No driver, Toolkit, service or system configuration was installed.'
