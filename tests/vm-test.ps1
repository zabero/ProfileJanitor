# Integration + safety test - run ONLY on the test VM, as admin/SYSTEM (see run-vm-test.cmd).
# It creates its own local accounts (zzpj_a..e), profiles and fake leftovers (zz_pj_* / S-1-5-21-1-2-3-*), really deletes
# them with the tool, and at the end proves that nothing that was on the machine before has changed.
# Usage: powershell -ExecutionPolicy Bypass -File vm-test.ps1 -Exe C:\Tools\ProfileJanitor\ProfileJanitor.exe
param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
$fail = 0
function Check($name, $ok) { if ($ok) { "PASS  $name" } else { "FAIL  $name"; $script:fail++ } }
function Run([string[]]$a) {
    if ($a -notcontains '--lang') { $a += '--lang', 'pl' }  # checks below match Polish texts
    $a = $a | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    # GUI-subsystem exe: Start-Process -Wait is the reliable way to get its exit code and output
    $o = [IO.Path]::GetTempFileName()
    $p = Start-Process $Exe -ArgumentList $a -Wait -PassThru -NoNewWindow -RedirectStandardOutput $o
    $script:out = [string](Get-Content $o -Raw -Encoding UTF8)
    Remove-Item $o
    $p.ExitCode
}
function Profiles { Get-CimInstance Win32_UserProfile | Where-Object { -not $_.Special } }
function Line($a, $b) { [bool]($out -split "`n" | Where-Object { $_ -match $a -and $_ -match [regex]::Escape($b) }) }

$here = Split-Path $Exe
$nt = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$pl = "$nt\ProfileList"
$users = (Get-ItemProperty $pl).ProfilesDirectory
$excl = [IO.Path]::ChangeExtension($Exe, $null).TrimEnd('.') + '.exclude.txt'
$exclOld = if (Test-Path $excl) { Get-Content $excl -Raw }
$pw = 'Zz' + [guid]::NewGuid().ToString('N').Substring(0, 14) + '!9a'
$names = 'zzpj_a', 'zzpj_b', 'zzpj_c', 'zzpj_d', 'zzpj_e'
$lf = "$users\zz_pj_leftover"
$odd = "$users\zz pj o'brien %TEMP% " + [char]0x105 + [char]0x119 + ' &'   # spaces, quote, percent signs, Polish letters, ampersand
$jl = "$users\zz_pj_junction"
$bakKey = 'S-1-5-21-1-2-3-4444.bak'
$goneSid = 'S-1-5-21-1-2-3-5555'
$guid = "$nt\ProfileGuid\{11111111-2222-3333-4444-555555555555}"
$bk = "$here\zz_pj_backup"
$sent = "$here\zz_pj_sentinel"      # must survive everything: junctions inside test profiles point here
$flag = "$here\zz_pj_release.flag"
$crash = Join-Path ([IO.Path]::GetTempPath()) 'ProfileJanitor-crash.txt'
$locker = $null; $holder = $null

# What was on the machine before the test and must be identical afterwards: registry profile keys, folders in the
# profiles directory, and file count + total size of every profile nobody is logged on to.
function Inventory {
    $k = (Get-ChildItem $pl).PSChildName | Sort-Object
    $d = (Get-ChildItem -LiteralPath $users -Force).Name | Sort-Object
    $f = foreach ($p in (Profiles | Where-Object { -not $_.Loaded } | Sort-Object LocalPath)) {
        $m = Get-ChildItem -LiteralPath $p.LocalPath -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum
        '{0}|{1}|{2}' -f $p.LocalPath, $m.Count, $m.Sum
    }
    (@($k) + '--' + @($d) + '--' + @($f)) -join "`n"
}
function NewUser($name) {
    New-LocalUser -Name $name -Password (ConvertTo-SecureString $pw -AsPlainText -Force) -AccountNeverExpires -PasswordNeverExpires | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $name
    $r = & "$here\make-test-profile.ps1" -User $name -Pass $pw
    if ($r -notmatch 'loaded and unloaded') { throw "cannot create test profile: $r" }
    (Get-LocalUser $name).SID.Value
}

if (Get-LocalUser | Where-Object Name -like 'zzpj_*') { 'FAIL  leftovers of a previous test run exist (zzpj_* accounts) - clean them first'; exit 1 }
$before = Inventory
$explorers = @(Get-Process explorer -ErrorAction SilentlyContinue).Count

try {
    New-Item -ItemType Directory $sent -Force | Out-Null
    Set-Content "$sent\keep.txt" 'must survive'
    $sid = @{}
    foreach ($n in $names) { $sid[$n] = NewUser $n }
    $sidA = $sid.zzpj_a; $pathA = "$users\zzpj_a"
    $sidB = $sid.zzpj_b; $pathB = "$users\zzpj_b"
    Add-LocalGroupMember -SID 'S-1-5-32-544' -Member 'zzpj_b'    # one test account is an administrator
    $others = Profiles | Where-Object SID -ne $sidA
    $loaded = $others | Where-Object Loaded | Select-Object -First 1

    '== basics'
    Check 'selftest passes' ((Run '--selftest') -eq 0)
    Check 'dump lists the test profile' ((Run '--dump') -eq 0 -and $out -match [regex]::Escape($sidA))
    Check 'role: standard account is tagged as user' (Line 'ytkownik' "$users\zzpj_a")
    Check 'role: member of Administrators is tagged as administrator' (Line "`tAdministrator`t" "$users\zzpj_b")
    Check 'system and service profiles are never listed' ($out -notmatch 'S-1-5-18|S-1-5-19|S-1-5-20|\\Users\\Public|\\Users\\Default')
    Check 'polish output' ($out -match 'Lokalne' -and $out -notmatch "`tLocal`t")
    Check 'english output' ((Run '--dump', '--lang', 'en') -eq 0 -and $out -match "`tLocal`t" -and $out -notmatch 'Lokalne')
    Check 'unknown language is a usage error' ((Run '--dump', '--lang', 'de') -eq 2)
    Check 'help prints the usage text' ((Run '--help') -eq 0 -and $out -match '--auto' -and $out -match '--whatif')
    Check 'unknown flag is a usage error' ((Run '--bogus') -eq 2)
    Check 'auto without --days is a usage error' ((Run '--auto') -eq 2)
    Check 'unreachable computer is an error, not a crash' ((Run '--dump', '--computer', 'zz-no-such-host.invalid') -eq 3)

    '== refusing what must not be deleted'
    Check 'unknown SID is rejected' ((Run '--delete', 'S-1-5-21-1-2-3-9999') -eq 1)
    Check 'garbage id is rejected' ((Run '--delete', 'not-a-sid') -eq 1)
    Check 'a path outside the profile list is rejected' ((Run '--delete', 'C:\Windows') -eq 1 -and (Test-Path C:\Windows\System32))
    Check 'the Public folder is rejected' ((Run '--delete', "$users\Public") -eq 1 -and (Test-Path "$users\Public"))
    Check 'a parent-directory trick is rejected' ((Run '--delete', "$users\..\Tools") -eq 1 -and (Test-Path $here))
    Check 'a system SID is rejected' ((Run '--delete', 'S-1-5-18') -eq 1)
    if ($loaded) { Check 'in-use profile is refused' ((Run '--delete', $loaded.SID) -eq 1 -and (Test-Path -LiteralPath $loaded.LocalPath)) }
    else { 'SKIP  in-use profile is refused (nobody is logged on)' }
    Set-Content $excl "# test`r`n$sidA"
    Check 'excluded profile is refused' ((Run '--delete', $sidA) -eq 1 -and (Test-Path -LiteralPath $pathA))
    Check 'auto skips excluded profile' ((Run '--auto', '--days', '0', '--include-local', '--whatif') -eq 0 -and $out -notmatch [regex]::Escape($sidA))
    Remove-Item $excl
    Check 'whatif lists the profile and deletes nothing' ((Run '--auto', '--days', '0', '--include-local', '--whatif') -eq 0 -and $out -match [regex]::Escape($sidA) -and (Test-Path -LiteralPath $pathA))
    Check 'auto skips administrators by default' ($out -notmatch 'zzpj_b')
    Check 'auto --include-admins takes them too' ((Run '--auto', '--days', '0', '--include-local', '--include-admins', '--whatif') -eq 0 -and $out -match 'zzpj_b')
    Check 'auto by default touches no local or unverified profile' ((Run '--auto', '--days', '0', '--whatif') -eq 0 -and $out -notmatch 'Lokalne|Niezweryfikowane')
    $n0 = @(Profiles).Count
    Check 'auto with nothing old enough deletes nothing' ((Run '--auto', '--days', '99999', '--include-local') -eq 0 -and @(Profiles).Count -eq $n0)

    '== leftovers'
    New-Item -ItemType Directory $lf, "$lf\sub", $odd -Force | Out-Null
    Set-Content "$lf\sub\ro.txt" 'x'; Set-ItemProperty "$lf\sub\ro.txt" IsReadOnly $true
    Set-Content -LiteralPath "$odd\f.txt" 'x'
    cmd /c mklink /J "$lf\link" $sent | Out-Null
    cmd /c mklink /J $jl $sent | Out-Null
    New-Item "$pl\$bakKey" -Force | Out-Null
    New-ItemProperty "$pl\$bakKey" ProfileImagePath -Value "$users\zz_pj_bak" -PropertyType ExpandString | Out-Null
    New-Item "$pl\$goneSid" -Force | Out-Null
    New-ItemProperty "$pl\$goneSid" ProfileImagePath -Value "$users\zz_pj_missing" -PropertyType ExpandString | Out-Null
    New-Item $guid -Force | Out-Null
    New-ItemProperty $guid SidString -Value $goneSid | Out-Null

    Run '--dump' | Out-Null
    Check 'detects folder without registry entry' (Line 'folder bez wpisu' $lf)
    Check 'detects folder with an awkward name' (Line 'folder bez wpisu' $odd)
    Check 'detects .bak key' (Line 'klucz \.bak' $bakKey)
    Check 'detects registry entry without folder' (Line 'wpis bez folderu' $goneSid)
    Check 'a junction in the profiles directory is not offered for deletion' ($out -notmatch 'zz_pj_junction')
    Check 'deleting that junction by path is rejected' ((Run '--delete', $jl) -eq 1 -and (Test-Path "$sent\keep.txt"))
    Check 'auto --leftovers picks only leftovers' ((Run '--auto', '--days', '99999', '--leftovers', '--whatif') -eq 0 -and $out -match 'Razem: 4 ' -and $out -notmatch 'zzpj_')
    Check 'delete leftover folder (read-only file, junction inside)' ((Run '--delete', $lf) -eq 0 -and -not (Test-Path -LiteralPath $lf))
    Check 'delete folder with an awkward name' ((Run '--delete', $odd) -eq 0 -and -not (Test-Path -LiteralPath $odd) -and (Test-Path $env:TEMP))
    Check 'junction target survives' (Test-Path "$sent\keep.txt")
    Check 'delete .bak key' ((Run '--delete', $bakKey) -eq 0 -and -not (Test-Path "$pl\$bakKey"))
    Check 'delete entry without folder' ((Run '--delete', $goneSid) -eq 0 -and -not (Test-Path "$pl\$goneSid"))
    Check 'ProfileGuid entry cleaned' (-not (Test-Path $guid))

    '== orphaned profile (account deleted, profile left)'
    Remove-LocalUser zzpj_c
    Run '--dump' | Out-Null
    Check 'detects orphaned local profile' (Line 'Osierocone \(lokalne\)' "$users\zzpj_c")
    Check 'auto --orphans would pick only the orphan' ((Run '--auto', '--days', '99999', '--orphans', '--whatif') -eq 0 -and $out -match 'Razem: 1 ' -and $out -match 'zzpj_c')
    Check 'auto --orphans deletes the orphan' ((Run '--auto', '--days', '99999', '--orphans') -eq 0 -and -not (Test-Path "$users\zzpj_c"))
    Check 'auto --orphans left the other test profiles alone' ((Test-Path $pathA) -and (Test-Path $pathB) -and (Test-Path "$users\zzpj_d") -and (Test-Path "$users\zzpj_e"))

    '== difficult profile: long paths, junction, read-only, sparse and locked files'
    $deep = "$pathB\deep\" + (('d' * 120 + '\') * 3)
    cmd /c "mkdir `"\\?\$deep`"" | Out-Null
    cmd /c "echo x> `"\\?\${deep}f.txt`""
    Check 'test setup: path longer than 260 characters exists' ((cmd /c "if exist `"\\?\${deep}f.txt`" echo yes") -eq 'yes')
    cmd /c mklink /J "$pathB\Desktop\link" $sent | Out-Null
    Set-Content "$pathB\ro.txt" 'x'; (Get-Item "$pathB\ro.txt").Attributes = 'ReadOnly,Hidden,System'
    Set-Content "$pathB\big.bin" 'x'
    fsutil sparse setflag "$pathB\big.bin" | Out-Null
    fsutil file seteof "$pathB\big.bin" 5368709120 | Out-Null    # 5 GB on paper, nothing on disk
    Check 'backup too big for the disk: refused, profile kept' ((Run '--delete', $sidB, '--backup', $bk) -eq 1 -and $out -match 'miejsca' -and (Test-Path $pathB) -and -not (Get-ChildItem $bk -Filter *.zip -ErrorAction SilentlyContinue))
    Remove-Item "$pathB\big.bin" -Force
    Set-Content "$pathB\locked.dat" 'x'
    $locker = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', "`$f=[IO.File]::Open('$pathB\locked.dat','Open','Read','None'); Start-Sleep 600"
    Start-Sleep 4
    Check 'backup hits a locked file: refused, profile kept, no partial zip' ((Run '--delete', $sidB, '--backup', $bk) -eq 1 -and $out -match 'kopia nieudana' -and (Test-Path "$pathB\locked.dat") -and -not (Get-ChildItem $bk -Filter *.zip -ErrorAction SilentlyContinue))
    $rc = Run '--delete', $sidB
    "      (delete with a locked file inside: exit $rc, folder still there: $(Test-Path $pathB))"
    Check 'locked file: never reports success while the folder remains' (-not ($rc -eq 0 -and (Test-Path $pathB)))
    $locker.Kill(); $locker.WaitForExit(); $locker = $null
    if (Test-Path "$pl\$sidB") { Run '--delete', $sidB | Out-Null }
    if (Test-Path $pathB) { Run '--dump' | Out-Null; Run '--delete', $pathB | Out-Null }
    Check 'half-deleted profile can be finished with the tool (long paths included)' (-not (Test-Path $pathB) -and -not (Test-Path "$pl\$sidB"))
    Check 'junction target outside the profile survives' (Test-Path "$sent\keep.txt")

    if ($true) {
        '== window logic (the same handlers the buttons call)'
        $holder = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "$here\make-test-profile.ps1", '-User', 'zzpj_e', '-Pass', $pw, '-HoldUntil', $flag
        for ($i = 0; $i -lt 30 -and -not (Profiles | Where-Object { $_.SID -eq $sid.zzpj_e -and $_.Loaded }); $i++) { Start-Sleep 1 }
        Check 'test setup: profile loaded without a desktop session' ([bool](Profiles | Where-Object { $_.SID -eq $sid.zzpj_e -and $_.Loaded }))
        $rc = Run '--uitest', '--victim', $sid.zzpj_d, '--loaded', $sid.zzpj_e
        $out.Trim() -split "`r?`n" | ForEach-Object { if ($_ -match '^FAIL') { $script:fail++ }; $_ }
        Check 'window test finished cleanly' ($rc -eq 0)
        Check 'no real desktop session was logged off' (@(Get-Process explorer -ErrorAction SilentlyContinue).Count -eq $explorers)
        New-Item $flag -ItemType File -Force | Out-Null
        $holder.WaitForExit(30000) | Out-Null; $holder = $null
        for ($i = 0; $i -lt 20 -and (Profiles | Where-Object { $_.SID -eq $sid.zzpj_e -and $_.Loaded }); $i++) { Start-Sleep 1 }
        Check 'once released, the formerly in-use profile deletes normally' ((Run '--delete', $sid.zzpj_e) -eq 0 -and -not (Test-Path "$users\zzpj_e"))
    }

    '== backup and delete of a normal profile'
    $others = Profiles | Where-Object SID -ne $sidA
    Check 'delete with backup returns 0' ((Run '--delete', $sidA, '--backup', $bk) -eq 0)
    $zip = Get-ChildItem $bk -Filter *.zip -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $entries = if ($zip) { $z = [IO.Compression.ZipFile]::OpenRead($zip.FullName); $z.Entries.FullName; $z.Dispose() }
    Check 'backup zip contains NTUSER.DAT' ($entries -contains 'NTUSER.DAT')
    Check 'profile folder is gone' (-not (Test-Path -LiteralPath $pathA))
    Check 'profile registry key is gone' (-not (Test-Path "$pl\$sidA"))
    Check 'the account itself survives' ([bool](Get-LocalUser | Where-Object { $_.SID.Value -eq $sidA }))
    $after = Profiles
    Check 'other profiles untouched' (-not ($others | Where-Object { $_.SID -notin $after.SID -or -not (Test-Path -LiteralPath $_.LocalPath) }))
    $ev = Get-EventLog Application -Source ProfileJanitor -Newest 1
    Check 'event log entry written' ($ev.Message -match [regex]::Escape($sidA) -and $ev.TimeGenerated -gt (Get-Date).AddMinutes(-2))
    Check 'deleting the same profile twice is an error, not a crash' ((Run '--delete', $sidA) -eq 1)

    '== remote code path over loopback, antivirus, window start'
    $other = Profiles | Select-Object -First 1
    Check 'remote mode (127.0.0.1) lists profiles' ((Run '--dump', '--computer', '127.0.0.1') -eq 0 -and $out -match [regex]::Escape($other.SID))
    Check 'remote mode sees the disk' ($out -notmatch 'Brak dost')
    $mp = "$env:ProgramFiles\Windows Defender\MpCmdRun.exe"
    if (Test-Path $mp) { & $mp -Scan -ScanType 3 -File $Exe -DisableRemediation | Out-Null; Check 'Windows Defender finds nothing in the exe' ($LASTEXITCODE -eq 0) }
    else { 'SKIP  Windows Defender scan (MpCmdRun not found)' }
    Remove-Item $crash -ErrorAction SilentlyContinue
    $g = Start-Process $Exe -PassThru
    Start-Sleep 12
    Check 'window starts and stays up 12 s' (-not $g.HasExited)
    if (-not $g.HasExited) { $g.Kill() }
    Check 'window did not log a crash' (-not (Test-Path $crash))
    if (Test-Path $crash) { Get-Content $crash | Select-Object -First 25 }
}
catch { "FAIL  test script error: $_ (line $($_.InvocationInfo.ScriptLineNumber))"; $fail++ }
finally {
    # leave no test debris behind, whatever happened
    $ErrorActionPreference = 'SilentlyContinue'
    New-Item $flag -ItemType File -Force | Out-Null
    if ($holder) { $holder.WaitForExit(30000) | Out-Null }
    if ($locker) { $locker.Kill() }
    Start-Sleep 2
    Profiles | Where-Object { $_.LocalPath -like "$users\zzpj_*" } | Remove-CimInstance
    foreach ($n in $names) { Remove-LocalUser $n }
    foreach ($j in "$lf\link", $jl) { if (Test-Path -LiteralPath $j) { cmd /c rmdir $j } }
    Get-ChildItem -LiteralPath $users -Force | Where-Object { $_.Name -match '^(zzpj_|zz_pj|zz pj)' } | ForEach-Object { cmd /c rmdir /s /q "\\?\$($_.FullName)" }
    Remove-Item -LiteralPath $bk, $sent, $flag -Recurse -Force
    Remove-Item "$pl\$bakKey", "$pl\$goneSid", $guid -Recurse -Force
    if ($null -ne $exclOld) { Set-Content $excl $exclOld -NoNewline } elseif (Test-Path $excl) { Remove-Item $excl }
    $ErrorActionPreference = 'Stop'
}
'== nothing else on the machine changed'
$now = Inventory
Check 'registry keys, profile folders and files of all pre-existing profiles are identical' ($now -eq $before)
if ($now -ne $before) { Compare-Object ($before -split "`n") ($now -split "`n") | Format-Table -AutoSize | Out-String }
Check 'no test accounts left behind' (-not (Get-LocalUser | Where-Object Name -like 'zzpj_*'))
"FAILED=$fail"
exit $fail
