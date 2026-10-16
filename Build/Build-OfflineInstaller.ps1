# Create one self-contained cabinet instead of trusting IExpress's CD-ROM spanning defaults.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory, [Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
function Assert-InstallerOutput([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (!$resolved.StartsWith((Join-Path $repository '_Output') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer paths must stay under owned output.' }
    $ancestor = $resolved
    while ($ancestor) {
        $entry = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installer paths cannot traverse redirected entries.' }
        if ($ancestor -eq $repository) { break }; $ancestor = Split-Path $ancestor -Parent
    }
    return $resolved
}
$payload = Assert-InstallerOutput $PayloadDirectory
$target = Assert-InstallerOutput $InstallerPath
if ([IO.Path]::GetExtension($target) -ine '.exe' -or ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $target -PathType Leaf))) { throw 'Installer target must be an executable file path.' }
$scratch = Assert-InstallerOutput (Join-Path $repository ('_Output/Installer-' + [Guid]::NewGuid().ToString('N').Substring(0,8)))
$stubPayload = Join-Path $scratch 'StubPayload'
New-Item -ItemType Directory -Path $scratch, $stubPayload | Out-Null
$names = @('Laura-win-x64.zip', 'Install.ps1', 'Install.cmd')
foreach ($name in $names) {
    $source = Assert-InstallerOutput (Join-Path $payload $name)
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Required installer payload is missing.' }
    # Size-identical placeholders let IExpress generate accurate disk-space metadata without recompressing the actual ZIP.
    $placeholder = [IO.File]::Open((Join-Path $stubPayload $name), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $placeholder.SetLength((Get-Item -LiteralPath $source).Length) } finally { $placeholder.Dispose() }
}
$stub = Join-Path $scratch 'LauraSetup.exe'
$sedPath = Join-Path $scratch 'stub.sed'
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$stub
FriendlyName=Laura
AppLaunched=cmd.exe /c Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0=Laura-win-x64.zip
FILE1=Install.ps1
FILE2=Install.cmd
[SourceFiles]
SourceFiles0=$stubPayload\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@
Set-Content -LiteralPath $sedPath -Value $sed -Encoding ascii
$builder = Start-Process -FilePath "$env:WINDIR/System32/iexpress.exe" -ArgumentList '/N', '/Q', 'stub.sed' -WorkingDirectory $scratch -WindowStyle Hidden -PassThru -Wait
if ($builder.ExitCode -ne 0 -or !(Test-Path -LiteralPath $stub)) { throw ('Installer stub generation failed; native exit code: ' + $builder.ExitCode) }
$cabinet = Join-Path $scratch 'payload.cab'
$directive = @"
.Set CabinetNameTemplate=$cabinet
.Set DiskDirectoryTemplate=
.Set MaxDiskSize=0
.Set MaxCabinetSize=0
.Set Cabinet=ON
.Set Compress=OFF
.Set InfFileName=$(Join-Path $scratch 'payload.inf')
.Set RptFileName=$(Join-Path $scratch 'payload.rpt')
"@
foreach ($name in $names) { $directive += "`r`n`"$(Join-Path $payload $name)`" `"$name`"" }
$directivePath = Join-Path $scratch 'payload.ddf'
Set-Content -LiteralPath $directivePath -Value $directive -Encoding ascii
$cabinetBuilder = Start-Process -FilePath "$env:WINDIR/System32/makecab.exe" -ArgumentList '/f', 'payload.ddf' -WorkingDirectory $scratch -WindowStyle Hidden -PassThru -Wait
if ($cabinetBuilder.ExitCode -ne 0 -or !(Test-Path -LiteralPath $cabinet)) { throw 'Single installer cabinet generation failed.' }
Add-Type -Path (Join-Path $PSScriptRoot 'InstallerCabinetResource.cs')
[InstallerCabinetResource]::Replace($stub, $cabinet)
$verification = Join-Path $scratch 'Verification'
& (Join-Path $PSScriptRoot 'Verify-Installer.ps1') -Installer $stub -OutputDirectory $verification
Copy-Item -LiteralPath (Join-Path $verification 'installer-verification.json') -Destination (Join-Path (Split-Path $target -Parent) 'installer-verification.json') -Force
Move-Item -LiteralPath (Assert-InstallerOutput $stub) -Destination (Assert-InstallerOutput $target) -Force
$ownedScratch = Assert-InstallerOutput $scratch
if (Get-ChildItem -LiteralPath $ownedScratch -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Cannot clean redirected installer scratch.' }
Remove-Item -LiteralPath $ownedScratch -Recurse -Force
Write-Output 'Offline installer ready: one verified cabinet with the actual embedded ZIP and setup scripts.'
