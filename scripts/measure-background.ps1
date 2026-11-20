param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$Fixture, [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
$exe = [System.IO.Path]::GetFullPath($Executable)
$data = [System.IO.Path]::GetFullPath($Fixture)
$outputPath = [System.IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath (Join-Path $data '.demo-data'))) { throw 'An isolated demo fixture is required.' }
$probe = Start-Process -FilePath $exe -ArgumentList @('--demo', '--background', '--data-dir', ('"' + $data + '"')) -WindowStyle Hidden -PassThru
try {
    if (-not $probe.WaitForInputIdle(20000)) { throw 'Application startup timed out.' }
    Start-Sleep -Seconds 4
    $probe.Refresh()
    $startCpu = $probe.TotalProcessorTime.TotalMilliseconds
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $samples = @()
    for ($i = 0; $i -lt 5; $i++) {
        Start-Sleep -Seconds 2
        $probe.Refresh()
        if ($probe.HasExited) { throw 'Application stopped during measurement.' }
        $samples += [PSCustomObject]@{ PrivateBytes = $probe.PrivateMemorySize64; WorkingSetBytes = $probe.WorkingSet64 }
    }
    $clock.Stop()
    $result = [PSCustomObject]@{
        Scenario = 'Preseeded identical 14-day demo, background startup, five samples after four-second warmup'
        AveragePrivateBytes = [math]::Round(($samples | Measure-Object PrivateBytes -Average).Average)
        AverageWorkingSetBytes = [math]::Round(($samples | Measure-Object WorkingSetBytes -Average).Average)
        CpuMilliseconds = $probe.TotalProcessorTime.TotalMilliseconds - $startCpu
        ElapsedMilliseconds = $clock.Elapsed.TotalMilliseconds
        Samples = $samples
    }
    $json = $result | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($outputPath, $json)
    Write-Output $json
} finally {
    $probe.Refresh()
    if (-not $probe.HasExited) { Stop-Process -Id $probe.Id }
}
