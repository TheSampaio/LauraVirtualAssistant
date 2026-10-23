param(
    [string]$Models = [IO.Path]::Combine([Environment]::GetFolderPath('MyDocuments'), 'Laura Virtual Assistant', 'Models'),
    [string]$Assets = [IO.Path]::Combine([Environment]::GetFolderPath('MyDocuments'), 'Laura Virtual Assistant', 'Runtime', 'Kokoro-v1.0', 'kokoro-multi-lang-v1_0', 'espeak-ng-data')
)

$worker = Join-Path $PSScriptRoot '../../Laura.VoiceWorker/bin/Debug/net10.0/win-x64/Laura.VoiceWorker.exe'
if (-not (Test-Path -LiteralPath $worker) -or -not (Test-Path -LiteralPath $Models) -or -not (Test-Path -LiteralPath $Assets)) {
    throw 'The local voice worker or model assets are missing.'
}

$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Resolve-Path -LiteralPath $worker).Path
$start.UseShellExecute = $false
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.CreateNoWindow = $true
$process = [Diagnostics.Process]::Start($start)
try {
    foreach ($case in @(
        @{ Culture = 'en-US'; Voice = 'gb_cori'; Text = 'Please open Lossless Scaling and show my tasks for today.' },
        @{ Culture = 'pt-BR'; Voice = 'pt_dii'; Text = 'Hoje eu tenho uma reunião às duas da tarde e quero o resumo do dia.' }
    )) {
        $request = @{ Operation = 'synthesize'; Directory = $Models; AssetsDirectory = $Assets; Culture = $case.Culture;
            Threads = 2; Text = $case.Text; Voice = $case.Voice; Speed = 1 }
        $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress))
        $pcm = [Collections.Generic.List[byte]]::new()
        $events = [Collections.Generic.List[string]]::new()
        while (($line = $process.StandardOutput.ReadLine())) {
            $eventData = $line | ConvertFrom-Json
            $events.Add([string]$eventData.Type)
            if ($eventData.Type -eq 'error') { throw "Synthesis failed for $($case.Voice): $($eventData.Data)" }
            if ($eventData.Type -eq 'pcm') { $pcm.AddRange([Convert]::FromBase64String($eventData.Data)) }
            if ($eventData.Type -eq 'done') { break }
        }
        if ($pcm.Count -lt 3200) { throw "No synthesized speech for $($case.Voice). Events: $($events -join ','); exited: $($process.HasExited)" }
        $samples = [Math]::Floor($pcm.Count / 3)
        $resampled = [byte[]]::new($samples * 2)
        for ($index = 0; $index -lt $samples; $index++) {
            $source = [int][Math]::Floor($index * 1.5) * 2
            $resampled[$index * 2] = $pcm[$source]
            $resampled[$index * 2 + 1] = $pcm[$source + 1]
        }
        $request = @{ Operation = 'recognize'; Directory = $Models; Culture = $case.Culture; Threads = 2;
            Pcm = [Convert]::ToBase64String($resampled); DetectLanguage = $true }
        $process.StandardInput.WriteLine(($request | ConvertTo-Json -Compress))
        $transcript = ''
        while (($line = $process.StandardOutput.ReadLine())) {
            $eventData = $line | ConvertFrom-Json
            if ($eventData.Type -eq 'error') { throw "Recognition failed for $($case.Culture): $($eventData.Data)" }
            if ($eventData.Type -eq 'transcript') { $transcript = $eventData.Data }
            if ($eventData.Type -eq 'done') { break }
        }
        if ([string]::IsNullOrWhiteSpace($transcript)) { throw "No transcript for $($case.Culture)." }
        [pscustomobject]@{ Culture = $case.Culture; Voice = $case.Voice; PcmBytes = $pcm.Count; Transcript = $transcript }
    }
}
finally {
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) { $process.Kill() }
    $process.Dispose()
}
