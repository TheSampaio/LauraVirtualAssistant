# Downloads public text only. Never invokes Node, npm, REA, repository scripts or hooks.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = [IO.Path]::GetFullPath($Destination)
$allowed = [IO.Path]::GetFullPath((Join-Path $workspace '_Output')) + [IO.Path]::DirectorySeparatorChar
if (!$fixture.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $fixture)) {
    throw 'Use a new fixture directory within the workspace _Output directory.'
}
$revision = 'c7105d9723c835b5e0122f5fc6cfbea86265c349'
$files = [ordered]@{
    'README.md' = 'CD8C277DAB0D8AA76492D74AB228DE7203326204C13972B8F8490F3E3C19506B'
    'package.json' = 'BD63783AEDFF9ABAFF4DD4EA1F57B512D8DF922DA5B202CA0318DF3BFFB8720A'
    'docs/index.md' = '9D1B34D692149DDF50CF377EDEA4272969B174D7F37D91F2A16FBBFE3DE0F02B'
    'docs/reverse-engineering-tools.md' = '770E385A4B17A92B5DCEBA994EB367E8C75A5BED6287006DE2FFB8D2C183E59F'
    'docs/managed-code-analysis.md' = '5ACDD808250C8B21BCABA66B698A13830DAEA834A62AE726BB3D52D018DD7077'
    'docs/mcp-contracts.md' = '9368E35752183ECF1AA84FAE3DE27318EF37E7835D2070EE65103450A619D3F9'
    'src/main.ts' = '5B180172A0319CF26E33F3DC305AEE4F25982F92C4E0C070932B140894CC9BB8'
    'src/cli.ts' = '01CA684463EBE53A602C3475E22901FC077538D120CD990D0D5A8CE976560322'
    'src/mcpStartupPolicy.ts' = '9ACE86260BE51F7B0376E9CB3E902BD19BDBC5CDA9A3FC9229FD500B649A65FE'
    'src/serverIdentity.ts' = '169621CE87A031B4D0A5A06BC5187FA928E6DB554C8542B336BABBF9C2B639B1'
}
$sources = Join-Path $fixture 'Sources'
$manifest = foreach ($entry in $files.GetEnumerator()) {
    $target = Join-Path $sources $entry.Key
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    $uri = 'https://raw.githubusercontent.com/morluto/rea/' + $revision + '/' + $entry.Key
    Invoke-WebRequest -Uri $uri -OutFile $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.Value) { throw 'Public source hash differs from the pinned fixture.' }
    [pscustomobject]@{ path=$entry.Key; sha256=$entry.Value; url=$uri; bytes=(Get-Item -LiteralPath $target).Length }
}
Invoke-WebRequest -Uri ('https://raw.githubusercontent.com/morluto/rea/' + $revision + '/LICENSE') -OutFile (Join-Path $fixture 'LICENSE')
@{ repository='https://github.com/morluto/rea'; revision=$revision; files=@($manifest) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture 'manifest.json') -Encoding utf8
Write-Output $sources
