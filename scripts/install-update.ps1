param([string]$DataRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KeyTrail'), [string]$ExpectedVersion = '1.6.1', [string]$ReportDirectory = 'artifacts/rename-1.6.1')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$target = Join-Path $projectRoot 'dist\KeyTrail\KeyTrail.exe'
$stage = Join-Path $projectRoot 'dist\KeyTrail-update\KeyTrail.exe'
$pending = $target + '.pending'
$previous = $target + '.previous'
$data = [IO.Path]::GetFullPath($DataRoot)
if (-not (Test-Path -LiteralPath $stage)) { throw 'The tested update is missing.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($stage).ProductVersion -ne $ExpectedVersion) { throw 'Unexpected update version.' }
if (Test-Path -LiteralPath (Join-Path $data '.demo-data')) { throw 'This installer expects the real local statistics directory.' }
$running = @(Get-Process -Name KeyTrail -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })
if ($running.Count -gt 1) { throw 'Multiple matching instances require a separate update.' }
$visible = $false
if ($running.Count -eq 1) {
    $visible = $running[0].MainWindowHandle -ne [IntPtr]::Zero
    if ([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion) -ge [version]'1.1.0.0') {
        $exitRequest = Start-Process -FilePath $target -ArgumentList @('--exit', '--data-dir', ('"' + $data + '"')) -WindowStyle Hidden -PassThru
        if (-not $exitRequest.WaitForExit(10000) -or $exitRequest.ExitCode -ne 0 -or -not $running[0].WaitForExit(10000)) { throw 'The app has not saved and exited; the current executable was not replaced.' }
    } else {
        & (Join-Path $PSScriptRoot 'stop-keytrail-gracefully.ps1') -ProcessId $running[0].Id -ExpectedExecutable $target
    }
}

$backup = Join-Path $data ('backups\before-' + $ExpectedVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$savedFiles = @()
if (Test-Path -LiteralPath $data) {
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $days = Join-Path $data 'days'
    if (Test-Path -LiteralPath $days) {
        Copy-Item -LiteralPath $days -Destination $backup -Recurse
        $savedFiles += @(Get-ChildItem -LiteralPath $days -File -Recurse)
    }
    $settings = @(Get-ChildItem -LiteralPath $data -Filter 'settings.json*' -File)
    foreach ($file in $settings) { Copy-Item -LiteralPath $file.FullName -Destination $backup }
    $savedFiles += $settings
    foreach ($file in $savedFiles) {
        $relative = $file.FullName.Substring($data.Length).TrimStart([char]'\')
        $copy = Join-Path $backup $relative
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) { throw 'Data backup verification failed.' }
    }
}

# An atomic replacement keeps the old executable available for rollback.
Copy-Item -LiteralPath $stage -Destination $pending -Force
if (Test-Path -LiteralPath $target) { [IO.File]::Replace($pending, $target, $previous, $true) }
else { [IO.File]::Move($pending, $target) }
$launchArgs = @('--data-dir', ('"' + $data + '"'))
if (-not $visible) { $launchArgs += '--background' }
$updated = Start-Process -FilePath $target -ArgumentList $launchArgs -WindowStyle Hidden -PassThru
if (-not $updated.WaitForInputIdle(20000)) { throw 'The updated application did not become ready.' }
Start-Sleep -Seconds 2
$updated.Refresh()
if ($updated.HasExited) { throw 'The updated application exited unexpectedly; the previous executable and data backup are retained.' }
$result = [PSCustomObject]@{
    InstalledVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($target).ProductVersion
    ProcessId = $updated.Id
    Background = -not $visible
    BackupPath = $backup
    VerifiedBackupFiles = $savedFiles.Count
    ExecutableMatchesTestedPackage = ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $stage).Hash)
    DataRoot = $data
}
$report = [IO.Path]::GetFullPath((Join-Path $projectRoot $ReportDirectory))
if (-not $report.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Report directory must stay inside the project.' }
New-Item -ItemType Directory -Force -Path $report | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'installation.json') -Encoding UTF8
$result | ConvertTo-Json
