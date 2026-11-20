$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetPath = Join-Path $projectRoot 'dist\KeyTrail\KeyTrail.exe'
if (-not (Test-Path -LiteralPath $targetPath)) { throw 'Build the application before creating its shortcut.' }
$desktopPath = [Environment]::GetFolderPath('DesktopDirectory')
$shortcutPath = Join-Path $desktopPath '键盘统计器.lnk'
$legacyShortcutPath = Join-Path $desktopPath '键迹.lnk'
# IShellLinkW + IPersistFile explicitly preserve Unicode names and target paths.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
namespace KeyTrail.Setup {
 [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
 internal class ShellLink { }
 [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 internal interface IShellLinkW {
  void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr findData, uint flags);
  void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
  void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
  void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
  void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
  void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
  void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
  void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
  void GetHotkey(out short key); void SetHotkey(short key);
  void GetShowCmd(out int command); void SetShowCmd(int command);
  void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size, out int index);
  void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string value, int index);
  void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string value, uint reserved);
  void Resolve(IntPtr window, uint flags);
  void SetPath([MarshalAs(UnmanagedType.LPWStr)] string value);
 }
 public static class Shortcut {
  public static string ReadTarget(string linkPath) {
   var instance = new ShellLink();
   try {
    ((IPersistFile)instance).Load(linkPath, 0);
    var actual = new StringBuilder(32768);
    ((IShellLinkW)instance).GetPath(actual, actual.Capacity, IntPtr.Zero, 0);
    return actual.ToString();
   } finally { Marshal.FinalReleaseComObject(instance); }
  }
  public static string Create(string linkPath, string target, string directory) {
   var instance = new ShellLink();
   try {
    var link = (IShellLinkW)instance;
    link.SetPath(target); link.SetWorkingDirectory(directory); link.SetIconLocation(target, 0);
    link.SetDescription("键盘统计器 · 每日键盘与鼠标使用统计"); link.SetShowCmd(1);
    ((IPersistFile)instance).Save(linkPath, true);
    ((IPersistFile)instance).Load(linkPath, 0);
    var actual = new StringBuilder(32768); link.GetPath(actual, actual.Capacity, IntPtr.Zero, 0);
    if (!String.Equals(actual.ToString(), target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Shortcut target verification failed.");
    return linkPath;
   } finally { Marshal.FinalReleaseComObject(instance); }
  }
 }
}
'@
if ((Test-Path -LiteralPath $shortcutPath) -and [KeyTrail.Setup.Shortcut]::ReadTarget($shortcutPath) -ne $targetPath) {
    throw 'A desktop shortcut with this name points to another application.'
}
$created = [KeyTrail.Setup.Shortcut]::Create($shortcutPath, $targetPath, (Split-Path -Parent $targetPath))
$legacyRemoved = $false
if ((Test-Path -LiteralPath $legacyShortcutPath) -and [KeyTrail.Setup.Shortcut]::ReadTarget($legacyShortcutPath) -eq $targetPath) {
    Remove-Item -LiteralPath $legacyShortcutPath
    $legacyRemoved = $true
}
[PSCustomObject]@{ Shortcut = $created; Target = $targetPath; Verified = (Test-Path -LiteralPath $created); PreviousShortcutRemoved = $legacyRemoved } | ConvertTo-Json -Compress
