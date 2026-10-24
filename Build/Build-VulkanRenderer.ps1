# Build the optional native Vulkan child-surface renderer using the installed MSVC toolchain.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'Laura.VulkanRenderer/Reactor.cpp'
$output = Join-Path $root '_Output/VulkanRenderer'
New-Item -ItemType Directory -Force $output | Out-Null
$binary = Join-Path $output 'Laura.VulkanRenderer.dll'
$inputs = @((Get-Item $source)) + @(Get-ChildItem (Join-Path $root 'Laura.VulkanRenderer/Include') -Recurse -File)
if ((Test-Path $binary) -and !( $inputs | Where-Object LastWriteTimeUtc -gt (Get-Item $binary).LastWriteTimeUtc )) { return }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'MSVC x64 tools are required to build the optional Vulkan renderer.' }
$environment = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$functions = [regex]::Matches((Get-Content $source -Raw), '\b(vk[A-Z]\w*)\(') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$definition = Join-Path $output 'vulkan-1.def'
@('LIBRARY vulkan-1.dll', 'EXPORTS') + $functions | Set-Content $definition -Encoding ascii
$include = Join-Path $root 'Laura.VulkanRenderer/Include'
$build = Join-Path $output 'compile.cmd'
@('@echo off', ('call "{0}" >nul' -f $environment), ('cd /d "{0}"' -f $output),
    ('lib /nologo /machine:x64 /def:"{0}" /out:vulkan-1.lib >nul' -f $definition), 'if errorlevel 1 exit /b 1',
    ('cl /nologo /std:c++20 /O2 /MT /EHsc /W3 /LD /I"{0}" "{1}" /link /OUT:"{2}" vulkan-1.lib user32.lib' -f $include, $source, $binary)) | Set-Content $build -Encoding ascii
& $env:ComSpec /d /c $build
if ($LASTEXITCODE -ne 0) { throw 'Vulkan renderer compilation failed.' }
