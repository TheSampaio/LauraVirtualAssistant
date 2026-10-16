# Verify a complete app-local distribution without launching Laura or installing prerequisites.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory, [switch]$CreateManifest)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
$manifestPath = Join-Path $packageRoot 'package-manifest.json'
$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force)
if ((Get-Item -LiteralPath $packageRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package root is redirected.' }
if (Get-ChildItem -LiteralPath $packageRoot -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Package contains a redirected entry.' }
$required = @('Laura.exe', 'Laura.dll', 'Laura.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'Microsoft.ui.xaml.dll', 'e_sqlite3.dll',
    'runtimes/win-x64/native/noavx/llama.dll', 'runtimes/win-x64/native/avx2/llama.dll', 'runtimes/win-x64/native/cuda12/ggml-cuda.dll',
    'runtimes/win-x64/whisper.dll', 'runtimes/win-x64/ggml-cpu-whisper.dll',
    'LICENSE', 'THIRD_PARTY_NOTICES.md', 'licenses/dependency-inventory.json', 'licenses/VisualCpp-Runtime-LICENSE.txt',
    'licenses/NVIDIA-CUDA-12.9.79-LICENSE.txt', 'licenses/NVIDIA-cuBLAS-12.9.1.4-LICENSE.txt')
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $packageRoot $relative) -PathType Leaf)) { throw ('Required package dependency is missing: ' + $relative) }
}
foreach ($relative in @('licenses/cuda12-runtime-inventory.json', 'licenses/vc-runtime-inventory.json')) {
    $components = ConvertFrom-Json -InputObject (Get-Content -LiteralPath (Join-Path $packageRoot $relative) -Raw)
    foreach ($component in $components) {
        $asset = if ($relative.Contains('cuda12')) { Join-Path (Join-Path $packageRoot 'CudaRuntime') $component.File } else { Join-Path $packageRoot $component.File }
        if (!(Test-Path -LiteralPath $asset -PathType Leaf) -or (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash -ne $component.Sha256) { throw ('Pinned native dependency is missing or modified: ' + $component.File) }
        if ($component.Size -and (Get-Item -LiteralPath $asset).Length -ne $component.Size) { throw 'Pinned native dependency size mismatch.' }
    }
}
$entries = @($files | Where-Object FullName -ne $manifestPath | ForEach-Object {
    $relative = $_.FullName.Substring($packageRoot.TrimEnd('\', '/').Length + 1).Replace('\', '/')
    if ($_.Extension -in @('.gguf', '.db', '.task', '.study') -or $_.Name -in @('settings.ini', 'api_keys.json') -or $relative.StartsWith('.git/')) { throw 'Package unexpectedly contains model weights or private state.' }
    [ordered]@{ File = $relative; Size = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
} | Sort-Object { $_.File })
if ($CreateManifest) {
    $entries | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
} else {
    $expected = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifestPath -Raw)
    if ($entries.Count -ne $expected.Count) { throw 'Package file inventory changed.' }
    $byName = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($entry in $expected) {
        if ($byName.ContainsKey($entry.File)) { throw 'Package manifest contains duplicate entries.' }
        $byName.Add($entry.File, $entry)
    }
    foreach ($entry in $entries) {
        $known = $null
        if (!$byName.TryGetValue($entry.File, [ref]$known) -or $entry.Size -ne $known.Size -or $entry.Sha256 -ne $known.Sha256) { throw 'Package file hash or inventory changed.' }
    }
}
Write-Output ('Verified complete Windows x64 package: ' + $entries.Count + ' files; native CUDA/CPU/Whisper/SQLite/VC dependencies and original license inventories present. No model weights or private state bundled.')
