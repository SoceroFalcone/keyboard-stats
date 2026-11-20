param([string]$ReportDirectory = 'artifacts/rename-1.6.1')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskReportRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot $ReportDirectory))
if (-not $taskReportRoot.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Report directory must remain inside the project.' }
$taskInstall = Get-Content -LiteralPath (Join-Path $taskReportRoot 'installation.json') -Raw | ConvertFrom-Json
$taskDayFiles = @(Get-ChildItem -LiteralPath (Join-Path $taskInstall.BackupPath 'days') -Filter '*.json' -File)
$taskKeysVerified = 0
foreach ($taskDayFile in $taskDayFiles) {
    $taskSaved = Get-Content -LiteralPath $taskDayFile.FullName -Raw | ConvertFrom-Json -AsHashtable
    $taskLive = Get-Content -LiteralPath (Join-Path $taskInstall.DataRoot ('days/' + $taskDayFile.Name)) -Raw | ConvertFrom-Json -AsHashtable
    if ($taskSaved['Date'] -ne $taskLive['Date'] -or $taskSaved['SchemaVersion'] -ne $taskLive['SchemaVersion']) { throw 'An existing day identity changed.' }
    foreach ($taskKey in $taskSaved['Keys'].Keys) {
        if (-not $taskLive['Keys'].ContainsKey($taskKey) -or $taskLive['Keys'][$taskKey] -lt $taskSaved['Keys'][$taskKey]) { throw ('An existing counter was lost: ' + $taskDayFile.Name) }
        $taskKeysVerified++
    }
    for ($taskHour = 0; $taskHour -lt 24; $taskHour++) {
        if ($taskLive['Hours'][$taskHour] -lt $taskSaved['Hours'][$taskHour]) { throw 'An hourly count was lost.' }
    }
    foreach ($taskMinute in $taskSaved['ActiveMinutes']) {
        if ($taskLive['ActiveMinutes'] -notcontains $taskMinute) { throw 'An active minute was lost.' }
    }
}
$taskSettings = Join-Path $taskInstall.DataRoot 'settings.json'
$taskSavedSettings = Join-Path $taskInstall.BackupPath 'settings.json'
$taskHasSettings = Test-Path -LiteralPath $taskSettings
$taskHadSettings = Test-Path -LiteralPath $taskSavedSettings
$taskSettingsPreserved = $taskHasSettings -eq $taskHadSettings
if ($taskHasSettings -and $taskHadSettings) { $taskSettingsPreserved = (Get-FileHash -LiteralPath $taskSettings).Hash -eq (Get-FileHash -LiteralPath $taskSavedSettings).Hash }
if (-not $taskSettingsPreserved) { throw 'Settings changed during update.' }
if (-not ('KeyTrail.Verify.Shortcut' -as [type])) {
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Runtime.InteropServices.ComTypes;
namespace KeyTrail.Verify {
 [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink {}
 [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 internal interface IShellLinkW { void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int size,IntPtr findData,uint flags); }
 public static class Shortcut {
 public static string Read(string file) {
 var instance=new ShellLink();
 try { ((IPersistFile)instance).Load(file,0); var path=new StringBuilder(32768); ((IShellLinkW)instance).GetPath(path,path.Capacity,IntPtr.Zero,0); return path.ToString();}
 finally {Marshal.FinalReleaseComObject(instance);}
 }}}
'@
}
$taskShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '键盘统计器.lnk'
$taskTarget = Join-Path $taskRoot 'dist/KeyTrail/KeyTrail.exe'
if ([KeyTrail.Verify.Shortcut]::Read($taskShortcut) -ne $taskTarget) { throw 'Desktop shortcut does not point to the updated app.' }
$taskRunning = @(Get-Process -Name KeyTrail -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskTarget })
if ($taskRunning.Count -ne 1 -or $taskRunning[0].Id -ne $taskInstall.ProcessId) { throw 'Expected updated process is not the sole installed instance.' }
$taskPackageHash = (Get-FileHash -LiteralPath $taskTarget).Hash
$taskTest = Get-Content -LiteralPath (Join-Path $taskReportRoot 'final-package-test.json') -Raw | ConvertFrom-Json
if ($taskPackageHash -ne $taskTest.Sha256) { throw 'Installed package differs from tested package.' }
$taskVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskTarget).ProductVersion
if ($taskVersion -ne $taskInstall.InstalledVersion -or $taskVersion -ne $taskTest.Version) { throw 'Installed version differs from the tested version.' }
[pscustomobject]@{InstalledVersion=$taskVersion;ProcessId=$taskRunning[0].Id;DesktopShortcutVerified=$true;ExistingDayFilesVerified=$taskDayFiles.Count;ExistingCountersVerified=$taskKeysVerified;ExistingCountsPreserved=$true;HourlyCountsAndActiveMinutesPreserved=$true;SettingsPreserved=$taskSettingsPreserved;SettingsFilePresent=$taskHasSettings;MatchesTestedPackage=$true;Sha256=$taskPackageHash;Background=$taskInstall.Background} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskReportRoot 'installation-verified.json')
