# Creates the profile folder of a test account by logging it on and off (run as SYSTEM/admin on the test VM).
# Usage: powershell -ExecutionPolicy Bypass -File make-test-profile.ps1 -User test1 -Pass <password> [-Domain dc01.local]
#   -Domain <name>     log on a domain account; the default "." means a local account of this computer.
#   -HoldUntil <file>  keeps the profile LOADED (no desktop session) until that file appears or 10 minutes pass,
#                      then unloads it cleanly - used to test how the tool treats an in-use profile.
param([Parameter(Mandatory)][string]$User, [Parameter(Mandatory)][string]$Pass, [string]$HoldUntil, [string]$Domain = ".")
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.ComponentModel;
public static class Lg {
  [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool LogonUser(string u, string d, string p, int type, int prov, out IntPtr tok);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct PROFILEINFO { public int dwSize; public int dwFlags; public string lpUserName; public string lpProfilePath; public string lpDefaultPath; public string lpServerName; public string lpPolicyPath; public IntPtr hProfile; }
  [DllImport("userenv.dll", SetLastError=true, CharSet=CharSet.Unicode)] static extern bool LoadUserProfile(IntPtr tok, ref PROFILEINFO pi);
  [DllImport("userenv.dll", SetLastError=true)] static extern bool UnloadUserProfile(IntPtr tok, IntPtr h);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static string Logon(string user, string domain, string pass, string holdUntil) {
    IntPtr tok;
    if (!LogonUser(user, domain, pass, 2, 0, out tok)) return user + ": LogonUser failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
    var pi = new PROFILEINFO(); pi.dwSize = Marshal.SizeOf(typeof(PROFILEINFO)); pi.dwFlags = 1; pi.lpUserName = user;
    if (!LoadUserProfile(tok, ref pi)) { var m = new Win32Exception(Marshal.GetLastWin32Error()).Message; CloseHandle(tok); return user + ": LoadUserProfile failed: " + m; }
    if (!string.IsNullOrEmpty(holdUntil))
      for (int i = 0; i < 600 && !System.IO.File.Exists(holdUntil); i++) System.Threading.Thread.Sleep(1000);
    UnloadUserProfile(tok, pi.hProfile); CloseHandle(tok);
    return user + ": logged on, profile loaded and unloaded";
  }
}
'@
[Lg]::Logon($User, $Domain, $Pass, $HoldUntil)
