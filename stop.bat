@echo off
setlocal
cd /d "%~dp0"

echo Stopping EtherBound processes...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop.ps1"
echo EtherBound processes stopped.
endlocal
