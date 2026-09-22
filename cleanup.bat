@echo off
setlocal
cd /d "%~dp0"

echo Cleaning up EtherBound processes...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop.ps1"
echo EtherBound processes cleaned up.
endlocal
