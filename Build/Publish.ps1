# Build Laura's self-contained Windows package and embedded offline installer.
[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [string]$VcRedistDirectory = (Join-Path $env:ProgramFiles 'Microsoft Visual Studio/18/Community/VC/Redist/MSVC/14.51.36231/x64')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$distribution = Join-Path $root '_Output/Distribution'
$publish = Join-Path $distribution 'Laura-win-x64'
$payload = Join-Path $distribution 'InstallerPayload'
if (Test-Path -LiteralPath $publish) {
    $resolvedPublish = [IO.Path]::GetFullPath($publish)
    $resolvedDistribution = [IO.Path]::GetFullPath($distribution).TrimEnd('\') + '\'
    if (!$resolvedPublish.StartsWith($resolvedDistribution, [StringComparison]::OrdinalIgnoreCase) -or
        ((Get-Item -LiteralPath $publish -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe publish directory.' }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publish, $payload | Out-Null
Push-Location $root
try {
    & dotnet restore Laura.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    & dotnet publish Laura.App/Laura.App.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true --no-restore -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    # App-local CRT/OpenMP avoids a separate system-wide prerequisite installation.
    # The builder must have redistribution rights under the supplied Microsoft terms.
    $crtInventory = Get-Content -LiteralPath (Join-Path $root 'licenses/vc-runtime-inventory.json') -Raw | ConvertFrom-Json
    foreach ($component in $crtInventory) {
        $source = Join-Path $VcRedistDirectory $component.RelativePath
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $component.Sha256) { throw "VC runtime version/hash mismatch: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $publish $component.File)
    }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md'), (Join-Path $root 'README.md') -Destination $publish
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $publish -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $publish -Recurse -Force
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
TargetName=$installer
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
SourceFiles0=$payload\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@
        $sedPath = Join-Path $distribution 'LauraSetup.sed'
        Set-Content -LiteralPath $sedPath -Value $sed -Encoding ascii
        $process = Start-Process -FilePath "$env:WINDIR/System32/iexpress.exe" -ArgumentList '/N', '/Q', $sedPath -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $installer)) { throw 'Installer packaging failed.' }
        $installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        Add-Content -LiteralPath (Join-Path $distribution 'SHA256SUMS.txt') -Value "$installerHash  LauraSetup.exe" -Encoding ascii
    }
    Write-Output "Distribution ready: $distribution"
} finally { Pop-Location }
