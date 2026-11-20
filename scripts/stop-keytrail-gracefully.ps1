param([Parameter(Mandatory=$true)][int]$ProcessId, [Parameter(Mandatory=$true)][string]$ExpectedExecutable)
$ErrorActionPreference = 'Stop'
$process = Get-Process -Id $ProcessId -ErrorAction Stop
$expected = [IO.Path]::GetFullPath($ExpectedExecutable)
if ($process.Path -ne $expected) { throw 'The target process does not match the explicitly named KeyTrail executable.' }
# Restart Manager notifies this exact process to save and exit. No force flag is used.
Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace KeyTrail.Update {
 public static class GracefulStop {
  [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public int Pid; public FILETIME Started; }
  [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] private static extern int RmStartSession(out uint handle, uint flags, StringBuilder key);
  [DllImport("rstrtmgr.dll", CharSet=CharSet.Unicode)] private static extern int RmRegisterResources(uint handle, uint files, string[] fileNames, uint apps, UniqueProcess[] processes, uint services, string[] serviceNames);
  [DllImport("rstrtmgr.dll")] private static extern int RmShutdown(uint handle, uint flags, IntPtr progress);
  [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);
  public static void Stop(int pid, string expected) {
   using (var process = Process.GetProcessById(pid)) {
    if (!String.Equals(process.MainModule.FileName, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Executable changed before shutdown.");
    var time = process.StartTime.ToUniversalTime().ToFileTimeUtc();
    var target = new UniqueProcess { Pid = pid, Started = new FILETIME { dwLowDateTime = unchecked((int)time), dwHighDateTime = unchecked((int)(time >> 32)) } };
    uint session; var status = RmStartSession(out session, 0, new StringBuilder(33));
    if (status != 0) throw new InvalidOperationException("RmStartSession: " + status);
    try {
     status = RmRegisterResources(session, 0, null, 1, new [] { target }, 0, null);
     if (status != 0) throw new InvalidOperationException("RmRegisterResources: " + status);
     status = RmShutdown(session, 0, IntPtr.Zero);
     if (status != 0) throw new InvalidOperationException("Graceful shutdown was not completed: " + status);
     if (!process.WaitForExit(10000)) throw new InvalidOperationException("Application has not exited; no force termination was attempted.");
    } finally { RmEndSession(session); }
   }
  }
 }
}
'@
[KeyTrail.Update.GracefulStop]::Stop($ProcessId, $expected)
[PSCustomObject]@{ ProcessId = $ProcessId; GracefulExit = $true } | ConvertTo-Json -Compress
