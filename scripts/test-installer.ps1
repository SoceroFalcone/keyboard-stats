param([string]$ReportDirectory = 'artifacts/distribution')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskReport = [IO.Path]::GetFullPath((Join-Path $taskRoot $ReportDirectory))
if (-not $taskReport.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Reports must remain in the project.' }
$taskBuild = Get-Content -LiteralPath (Join-Path $taskReport 'build.json') -Raw | ConvertFrom-Json
$taskManifest = @(Get-Content -LiteralPath (Join-Path $taskReport 'payload-manifest.json') -Raw | ConvertFrom-Json)
$taskUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6FC3BA63-C891-4EB5-89EF-3CE67955CDD7}_is1'
if (Test-Path -LiteralPath $taskUninstallKey) { throw 'An existing installed copy must not be overwritten by an isolated packaging test.' }
if ((Get-FileHash -LiteralPath $taskBuild.Installer).Hash -ne $taskBuild.InstallerSha256) { throw 'Installer hash changed since compilation.' }
$taskTest = Join-Path $taskReport ('install-test-' + [Guid]::NewGuid().ToString('N'))
$taskInstall = [IO.Path]::GetFullPath((Join-Path $taskTest '中文 安装目录'))
if (-not $taskInstall.StartsWith($taskReport + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test installation must remain in the report directory.' }
New-Item -ItemType Directory -Path $taskTest | Out-Null
$taskExe = Join-Path $taskInstall '键盘统计器.exe'
$taskUninstaller = Join-Path $taskInstall 'unins000.exe'
$taskLivePath = Join-Path $taskRoot 'dist\KeyTrail\KeyTrail.exe'
$taskLivePids = @(Get-Process -Name KeyTrail -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskLivePath } | Select-Object -ExpandProperty Id)
$taskShortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '键盘统计器.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) '键盘统计器.lnk')
)
$taskShortcutHashes = @{}
foreach ($taskShortcut in $taskShortcutPaths) {
    $taskShortcutHashes[$taskShortcut] = if (Test-Path -LiteralPath $taskShortcut) { (Get-FileHash -LiteralPath $taskShortcut).Hash } else { '' }
}
$taskRunPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskStartupBefore = Get-ItemPropertyValue -LiteralPath $taskRunPath -Name KeyTrail -ErrorAction SilentlyContinue

function Invoke-InstallerProcess([string]$File, [string[]]$Arguments) {
    $taskChild = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $taskChild.WaitForExit(60000)) { throw ('Installer did not finish within 60 seconds: PID ' + $taskChild.Id) }
    return $taskChild.ExitCode
}
function Assert-InstalledPayload {
    foreach ($taskFile in $taskManifest) {
        $taskInstalledFile = Join-Path $taskInstall $taskFile.Path
        if (-not (Test-Path -LiteralPath $taskInstalledFile) -or (Get-FileHash -LiteralPath $taskInstalledFile).Hash -ne $taskFile.Sha256) { throw ('Installed payload mismatch: ' + $taskFile.Path) }
    }
    $taskRegistration = Get-ItemProperty -LiteralPath $taskUninstallKey
    if ($taskRegistration.DisplayName -ne '键盘统计器' -or $taskRegistration.DisplayVersion -ne $taskBuild.Version -or $taskRegistration.InstallLocation.TrimEnd('\') -ne $taskInstall) { throw 'Incorrect Windows uninstall registration.' }
}
function Assert-TestDataPreserved {
    foreach ($taskSavedPath in $taskDataHashes.Keys) {
        if (-not (Test-Path -LiteralPath $taskSavedPath) -or (Get-FileHash -LiteralPath $taskSavedPath).Hash -ne $taskDataHashes[$taskSavedPath]) { throw 'A test statistics file changed during setup or uninstall.' }
    }
}

$taskCommonArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', '/NOICONS', '/TASKS=""', ('/DIR="' + $taskInstall + '"'))
$taskFreshExit = Invoke-InstallerProcess $taskBuild.Installer ($taskCommonArgs + ('/LOG="' + (Join-Path $taskTest 'fresh-install.log') + '"'))
if ($taskFreshExit -ne 0) { throw ('Fresh install failed: ' + $taskFreshExit) }
Assert-InstalledPayload
Write-Output 'PASS fresh per-user installation in a path containing Chinese characters and spaces.'

$taskPackageReport = Join-Path $taskTest 'app-smoke'
& (Join-Path $PSScriptRoot 'test-package.ps1') -Executable ([IO.Path]::GetRelativePath($taskRoot, $taskExe)) -ExpectedVersion $taskBuild.Version -ReportDirectory ([IO.Path]::GetRelativePath($taskRoot, $taskPackageReport)) -RequireInstalledRuntime
$taskSmoke = Get-Content -LiteralPath (Join-Path $taskPackageReport 'final-package-test.json') -Raw | ConvertFrom-Json
$taskDataHashes = @{}
foreach ($taskData in (Get-ChildItem -LiteralPath $taskSmoke.Fixture -File -Recurse)) { $taskDataHashes[$taskData.FullName] = (Get-FileHash -LiteralPath $taskData.FullName).Hash }

# A live demo exercises the installer guard without touching real statistics.
$taskDemoArgs = @('--demo', '--background', '--data-dir', ('"' + $taskSmoke.Fixture + '"'))
$taskDemo = Start-Process -FilePath $taskExe -ArgumentList $taskDemoArgs -WindowStyle Hidden -PassThru
try {
    if (-not $taskDemo.WaitForInputIdle(20000)) { throw 'Installed demo did not become ready.' }
    $taskBlockedInstallExit = Invoke-InstallerProcess $taskBuild.Installer ($taskCommonArgs + ('/LOG="' + (Join-Path $taskTest 'running-install.log') + '"'))
    if ($taskBlockedInstallExit -eq 0) { throw 'Setup should require the running statistics application to exit first.' }
    $taskBlockedUninstallExit = Invoke-InstallerProcess $taskUninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $taskTest 'running-uninstall.log') + '"'))
    $taskDemo.Refresh()
    if ($taskDemo.HasExited) { throw 'The installer must not force-stop the live statistics process.' }
    Assert-InstalledPayload
    Write-Output 'PASS install/uninstall guard keeps a running statistics process and its files intact.'
} finally {
    $taskDemo.Refresh()
    if (-not $taskDemo.HasExited) {
        $taskQuit = Start-Process -FilePath $taskExe -ArgumentList (@('--exit') + $taskDemoArgs) -WindowStyle Hidden -PassThru
        if (-not $taskQuit.WaitForExit(10000) -or -not $taskDemo.WaitForExit(10000)) { throw 'The isolated demo did not exit normally.' }
    }
}
Assert-TestDataPreserved
$taskReinstallExit = Invoke-InstallerProcess $taskBuild.Installer ($taskCommonArgs + ('/LOG="' + (Join-Path $taskTest 'reinstall.log') + '"'))
if ($taskReinstallExit -ne 0) { throw ('Reinstall failed: ' + $taskReinstallExit) }
Assert-InstalledPayload
Assert-TestDataPreserved
Write-Output 'PASS repeat installation preserves the isolated history and settings.'

# Uninstall only the verified test installation inside this workspace.
$taskRegistered = Get-ItemProperty -LiteralPath $taskUninstallKey
if ($taskRegistered.InstallLocation.TrimEnd('\') -ne $taskInstall -or -not $taskUninstaller.StartsWith($taskReport + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstall target is not the isolated test installation.' }
$taskUninstallExit = Invoke-InstallerProcess $taskUninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $taskTest 'uninstall.log') + '"'))
if ($taskUninstallExit -ne 0) { throw ('Uninstall failed: ' + $taskUninstallExit) }
foreach ($taskFile in $taskManifest) {
    if (Test-Path -LiteralPath (Join-Path $taskInstall $taskFile.Path)) { throw ('Uninstall left an installed file: ' + $taskFile.Path) }
}
if (Test-Path -LiteralPath $taskUninstallKey) { throw 'Uninstall did not remove the Windows registration.' }
Assert-TestDataPreserved
foreach ($taskShortcut in $taskShortcutPaths) {
    $taskShortcutAfter = if (Test-Path -LiteralPath $taskShortcut) { (Get-FileHash -LiteralPath $taskShortcut).Hash } else { '' }
    if ($taskShortcutAfter -ne $taskShortcutHashes[$taskShortcut]) { throw 'An existing shortcut was changed by the isolated test.' }
}
$taskStartupAfter = Get-ItemPropertyValue -LiteralPath $taskRunPath -Name KeyTrail -ErrorAction SilentlyContinue
if ($taskStartupAfter -ne $taskStartupBefore) { throw 'An unrelated autostart setting was changed.' }
$taskLiveAfter = @(Get-Process -Name KeyTrail -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskLivePath } | Select-Object -ExpandProperty Id)
if ((($taskLiveAfter | Sort-Object) -join ',') -ne (($taskLivePids | Sort-Object) -join ',')) { throw 'The user application was interrupted by the test.' }

$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskBuild.ShareZip)
try {
    $taskEntries = @($taskArchive.Entries | Where-Object { $_.Name })
    $taskExpectedNames = @([IO.Path]::GetFileName($taskBuild.Installer), '使用说明.txt', 'SHA256.txt')
    if ($taskEntries.Count -ne 3 -or @($taskEntries | Where-Object { $_.Name -notin $taskExpectedNames }).Count -gt 0) { throw 'The share archive contains unexpected files.' }
} finally { $taskArchive.Dispose() }
$taskExtract = Join-Path $taskTest 'share-extracted'
Expand-Archive -LiteralPath $taskBuild.ShareZip -DestinationPath $taskExtract
$taskSharedInstaller = @(Get-ChildItem -LiteralPath $taskExtract -Filter '*.exe' -Recurse -File)
if ($taskSharedInstaller.Count -ne 1 -or (Get-FileHash -LiteralPath $taskSharedInstaller[0].FullName).Hash -ne $taskBuild.InstallerSha256) { throw 'Share archive installer does not match the tested installer.' }
$taskResult = [pscustomobject]@{
    Version=$taskBuild.Version; InstallerSha256=$taskBuild.InstallerSha256
    FreshInstall=$true; PayloadFilesVerified=$taskManifest.Count; PackagedAppStarted=$taskSmoke.DashboardStarted
    OpenedFromBackgroundWithoutExtraction=$taskSmoke.OpenedFromBackgroundWithoutExtraction
    SingleInstanceVerified=($taskSmoke.SecondLaunchExitCode -eq 0); CooperativeExitVerified=($taskSmoke.CooperativeExitCode -eq 0)
    RunningInstallBlocked=$true; RunningUninstallBlocked=$true; RunningInstallExitCode=$taskBlockedInstallExit; RunningUninstallExitCode=$taskBlockedUninstallExit
    Reinstall=$true; Uninstall=$true; HistoryFilesPreserved=$taskSmoke.DemoDays
    ExistingUserProcessPreserved=$true; ExistingShortcutsPreserved=$true; ExistingAutostartPreserved=$true; ShareArchiveVerified=$true
    TestDirectory=$taskTest
}
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskReport 'installer-test.json') -Encoding UTF8
$taskResult | ConvertTo-Json
