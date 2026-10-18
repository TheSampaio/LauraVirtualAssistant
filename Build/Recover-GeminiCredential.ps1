[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApprovedSource,
    [switch]$OwnerAuthorized,
    [string]$CredentialDirectory,
    [string]$ResultPath
)

# This recovery requires the owner's explicit authorization for the named import source.
# It never prints credential values and refuses to replace a key the owner already repaired.
$ErrorActionPreference = 'Stop'
if (-not $OwnerAuthorized) { throw 'Owner authorization for this exact import source is required.' }
if ([string]::IsNullOrWhiteSpace($CredentialDirectory)) {
    $CredentialDirectory = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Laura Virtual Assistant'
}
$taskSource = [IO.Path]::GetFullPath($ApprovedSource)
$taskDirectory = [IO.Path]::GetFullPath($CredentialDirectory)
if ($taskSource.StartsWith('\\') -or $taskDirectory.StartsWith('\\')) { throw 'Recovery requires local paths.' }
foreach ($taskCandidate in @($taskSource, $taskDirectory)) {
    for ($taskAncestor = $taskCandidate; $null -ne $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
        if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Recovery refuses redirected paths.'
        }
    }
}
Add-Type -AssemblyName System.Security
$taskTarget = Join-Path $taskDirectory 'google-key.dat'
$taskCurrentBytes = $null
$taskPlainBytes = $null
$taskTemporary = $null
try {
    $taskCurrentBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskTarget), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    if ([Text.Encoding]::UTF8.GetString($taskCurrentBytes) -ne 'synthetic-key-not-valid-for-inference') {
        throw 'The current key is no longer the accidental synthetic key. No credential was changed.'
    }
    $taskSourceHash = (Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash
    try { $taskConfig = [IO.File]::ReadAllText($taskSource) | ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'The approved import configuration could not be parsed. No credential was changed.' }
    $taskKey = $taskConfig.gemini_api_key
    if ($taskKey -isnot [string] -or [string]::IsNullOrWhiteSpace($taskKey)) {
        throw 'The approved import source does not contain a Gemini API key in the supported schema. No credential was changed.'
    }
    $taskPlainBytes = [Text.Encoding]::UTF8.GetBytes($taskKey.Trim())
    $taskProtectedBytes = [Security.Cryptography.ProtectedData]::Protect($taskPlainBytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $taskTemporary = $taskTarget + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $taskStream = [IO.FileStream]::new($taskTemporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try { $taskStream.Write($taskProtectedBytes, 0, $taskProtectedBytes.Length); $taskStream.Flush($true) }
    finally { $taskStream.Dispose() }
    [IO.File]::Replace($taskTemporary, $taskTarget, (Join-Path $taskDirectory 'google-key.previous.dat'))
    if ((Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash -ne $taskSourceHash) { throw 'The import source changed during recovery; inspect its state.' }
    $taskVerification = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskTarget), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        if ([Text.Encoding]::UTF8.GetString($taskVerification) -ne $taskKey.Trim()) { throw 'Protected credential verification failed.' }
    }
    finally { [Array]::Clear($taskVerification, 0, $taskVerification.Length) }
    if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
        $taskResult = [IO.Path]::GetFullPath($ResultPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskResult)) | Out-Null
        [IO.File]::WriteAllText($taskResult, '{"OwnerAuthorizedImport":true,"SourceUnchanged":true,"ProtectedReadBack":true,"RealAuthentication":"Pending"}', [Text.UTF8Encoding]::new($false))
    }
    Write-Output 'Approved local Gemini credential restored to protected storage; source unchanged. Real provider validation is still required.'
}
finally {
    if ($null -ne $taskCurrentBytes) { [Array]::Clear($taskCurrentBytes, 0, $taskCurrentBytes.Length) }
    if ($null -ne $taskPlainBytes) { [Array]::Clear($taskPlainBytes, 0, $taskPlainBytes.Length) }
    if ($null -ne $taskTemporary -and [IO.File]::Exists($taskTemporary)) { [IO.File]::Delete($taskTemporary) }
    $taskKey = $null; $taskConfig = $null
}
