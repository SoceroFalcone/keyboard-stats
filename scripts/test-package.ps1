param([string]$Executable = 'dist/KeyTrail-update/KeyTrail.exe', [string]$ExpectedVersion = '1.6.1', [string]$ReportDirectory = 'artifacts/rename-1.6.1')
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageExe = [IO.Path]::GetFullPath((Join-Path $packageRoot $Executable))
$packageReport = [IO.Path]::GetFullPath((Join-Path $packageRoot $ReportDirectory))
if (-not $packageExe.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not $packageReport.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package checks must stay in the workspace.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($packageExe).ProductVersion -ne $ExpectedVersion) { throw 'Unexpected package version.' }
$packageFixture = Join-Path $packageReport ('package-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $packageFixture | Out-Null
$packageArguments = @('--demo', '--data-dir', ('"' + $packageFixture + '"'))
$packageProcess = Start-Process -FilePath $packageExe -ArgumentList $packageArguments -WindowStyle Hidden -PassThru
try {
    $packageDeadline = [DateTime]::UtcNow.AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 200
        $packageProcess.Refresh()
        if ($packageProcess.HasExited) { throw 'Packaged app exited before showing its dashboard.' }
    } while (-not $packageProcess.MainWindowTitle.Contains('演示数据') -and [DateTime]::UtcNow -lt $packageDeadline)
    if (-not $packageProcess.MainWindowTitle.Contains('演示数据')) { throw 'Packaged dashboard did not become ready.' }
    $packageDayFiles = @(Get-ChildItem -LiteralPath (Join-Path $packageFixture 'days') -Filter '*.json' -File)
    if ($packageDayFiles.Count -ne 14) { throw 'Demo fixture was not loaded completely.' }
    $packageSecond = Start-Process -FilePath $packageExe -ArgumentList $packageArguments -WindowStyle Hidden -PassThru
    if (-not $packageSecond.WaitForExit(10000) -or $packageSecond.ExitCode -ne 0) { throw 'Second launch did not reuse the running instance.' }
    $packageProcess.Refresh()
    if ($packageProcess.HasExited) { throw 'First instance unexpectedly stopped.' }
    $packageExit = Start-Process -FilePath $packageExe -ArgumentList (@('--exit') + $packageArguments) -WindowStyle Hidden -PassThru
    if (-not $packageExit.WaitForExit(10000) -or $packageExit.ExitCode -ne 0 -or -not $packageProcess.WaitForExit(10000) -or $packageProcess.ExitCode -ne 0) { throw 'Cooperative package exit failed.' }
    $packageResult = [PSCustomObject]@{
        Version = $ExpectedVersion; Sha256 = (Get-FileHash -LiteralPath $packageExe).Hash
        DashboardStarted = $true; DemoDays = $packageDayFiles.Count; SecondLaunchExitCode = $packageSecond.ExitCode
        CooperativeExitCode = $packageProcess.ExitCode; Fixture = $packageFixture
    }
    $packageJson = $packageResult | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $packageReport 'final-package-test.json'), $packageJson)
    $packageJson
} finally {
    $packageProcess.Refresh()
    # Only this newly created isolated demo process may be stopped on a failed check.
    if (-not $packageProcess.HasExited) { Stop-Process -Id $packageProcess.Id }
}
