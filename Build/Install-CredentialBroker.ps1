# Installs the optional local credential service. Run elevated after reviewing the packaged component.
[CmdletBinding()]
param([string]$PayloadDirectory = '')
$ErrorActionPreference = 'Stop'
if (!$PayloadDirectory) { $PayloadDirectory = Join-Path $PSScriptRoot 'CredentialBroker' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator approval is required to install the credential service.' }
$serviceName = 'LauraCredentialBroker'
$target = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'LauraCredentialBroker'))
$data = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'LauraCredentialBroker'))
$payload = [IO.Path]::GetFullPath($PayloadDirectory)
function Assert-LocalTree([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if ($absolute.StartsWith('\\') -or $absolute.Substring([IO.Path]::GetPathRoot($absolute).Length).Contains(':')) { throw 'Only local ordinary deployment paths are supported.' }
    for ($current = $absolute; $current; $current = Split-Path $current -Parent) {
        $entry = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Credential service paths cannot traverse redirected entries.' }
    }
    if (Test-Path -LiteralPath $absolute) {
        if (Get-ChildItem -LiteralPath $absolute -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Credential service trees cannot contain redirected entries.' }
    }
}
function Protect-Directory([string]$Path, [bool]$DataStore) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $acl.SetOwner($admins)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    $rights = if ($DataStore) { 'Modify' } else { 'ReadAndExecute' }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-19'), $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    if (!$DataStore) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
Assert-LocalTree $payload; Assert-LocalTree $target; Assert-LocalTree $data
$manifest = Get-Content -LiteralPath (Join-Path $payload 'broker-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.Count -lt 3 -or $manifest.Count -gt 2000) { throw 'Credential service manifest is invalid.' }
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest) {
    if (!$names.Add($entry.File) -or $entry.File -notmatch '^[A-Za-z0-9_.-]+$' -or $entry.File -in @('.', '..') -or $entry.Sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Credential service manifest contains an unsafe path or hash.' }
    $source = Join-Path $payload $entry.File
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Credential service payload was modified.' }
}
if (!$names.Contains('Laura.CredentialBroker.exe')) { throw 'Credential service executable is missing.' }
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    $running = Get-CimInstance -ClassName Win32_Service -Filter "Name='LauraCredentialBroker'"
    $serviceProcess = if ($running.ProcessId -gt 0) { Get-Process -Id $running.ProcessId -ErrorAction SilentlyContinue } else { $null }
    try {
        Stop-Service -Name $serviceName -Force; $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        # SCM can report Stopped before the CLR finishes releasing loaded runtime files.
        if ($serviceProcess -and !$serviceProcess.WaitForExit(30000)) { throw 'Credential service process did not finish shutdown.' }
    } finally { if ($serviceProcess) { $serviceProcess.Dispose() } }
}
Protect-Directory $target $false; Protect-Directory $data $true
foreach ($entry in Get-ChildItem -LiteralPath $data -Force -Recurse) {
    $acl = if ($entry.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $acl.SetAccessRuleProtection($false, $false); $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    Set-Acl -LiteralPath $entry.FullName -AclObject $acl
}
foreach ($entry in $manifest) {
    $destination = Join-Path $target $entry.File
    if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.Sha256) {
        Copy-Item -LiteralPath (Join-Path $payload $entry.File) -Destination $destination -Force
    }
    # Replace preexisting explicit file ACLs; the administrator-protected parent is the only inherited policy.
    $fileAcl = [Security.AccessControl.FileSecurity]::new(); $fileAcl.SetAccessRuleProtection($false, $false)
    $fileAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')); Set-Acl -LiteralPath $destination -AclObject $fileAcl
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.Sha256) { throw 'Installed credential service hash verification failed.' }
}
$binary = '"' + (Join-Path $target 'Laura.CredentialBroker.exe') + '"'
if ($existing) {
    & sc.exe config $serviceName binPath= $binary obj= 'NT AUTHORITY\LocalService' start= auto | Out-Null
} else {
    & sc.exe create $serviceName binPath= $binary obj= 'NT AUTHORITY\LocalService' start= auto DisplayName= 'Laura ChatGPT Credential Broker' | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'Windows service registration failed.' }
# Windows PowerShell native argument marshalling can strip embedded quotes passed to sc.exe.
# CIM uses a typed string so the SCM image path retains quotes around Program Files.
$service = Get-CimInstance -ClassName Win32_Service -Filter "Name='LauraCredentialBroker'"
$configured = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{ PathName = $binary }
if ($configured.ReturnValue -ne 0) { throw 'Quoted Windows service path configuration failed.' }
# Only administrators and SYSTEM may change, stop or reconfigure the privileged boundary.
& sc.exe sdset $serviceName 'D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Windows service control ACL update failed.' }
& sc.exe description $serviceName 'Isolates Laura ChatGPT OAuth tokens from user command processes. Provides local authentication and inference only.' | Out-Null
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Write-Output 'Credential broker installed under LocalService. Tokens remain in service-only ProgramData storage. No command executor was installed.'
