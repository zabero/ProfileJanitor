@echo off
rem Uruchamia pelny test na maszynie testowej i zapisuje wynik do vm-test.log (test trwa kilka minut).
rem Uzycie: run-vm-test.cmd C:\Tools\ProfileJanitor\ProfileJanitor.exe
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0vm-test.ps1" -Exe %1 > "%~dp0vm-test.log" 2>&1
echo DONE exit=%errorlevel%>> "%~dp0vm-test.log"
