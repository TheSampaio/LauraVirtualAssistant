# Read-only audit; never removes files, changes the index or rewrites history.
[CmdletBinding()]
param([string[]]$BrainDirectories = @('brain'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$repoPrefix = $repoRoot + [IO.Path]::DirectorySeparatorChar
$tracked = @(& git -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the Git index.' }
$failures = [Collections.Generic.List[string]]::new()
foreach ($directory in $BrainDirectories) {
    $selectedPath = if ([IO.Path]::IsPathRooted($directory)) { $directory } else { Join-Path $repoRoot $directory }
    $absolute = [IO.Path]::GetFullPath($selectedPath)
    if (!$absolute.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $relative = [IO.Path]::GetRelativePath($repoRoot, $absolute).Replace('\', '/').TrimEnd('/')
    $privateTracked = @($tracked | Where-Object { $_.Equals($relative, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($relative + '/', [StringComparison]::OrdinalIgnoreCase) })
    if ($privateTracked.Count -gt 0) { $failures.Add('Configured vault contains tracked files. Preserve local copies and review index removal separately.') }
    & git -C $repoRoot check-ignore -q -- ($relative + '/')
    if ($LASTEXITCODE -ne 0) { $failures.Add('Configured vault lacks a Git exclusion.') }
    $historical = @(& git -C $repoRoot log --all --format= --name-only -- (':(icase,literal)' + $relative + '/')) | Where-Object { $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git history.' }
    if ($historical.Count -gt 0) { $failures.Add('Vault paths appear in Git history. Ignoring them cannot remove historical disclosures; history rewriting requires separate authorization.') }
}
if (@($tracked | Where-Object { $_ -match '(^|/)(brain-index[^/]*\.db[^/]*|brain-vectors[^/]*\.db[^/]*|memory\.db[^/]*|google-key\.dat[^/]*|api_keys\.json[^/]*|settings\.ini[^/]*)$' }).Count -gt 0) {
    $failures.Add('Private local settings, credentials or memory caches are tracked.')
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Output 'Brain privacy audit passed: configured repository vaults are ignored, with no tracked contents, private caches or vault paths in history.'
