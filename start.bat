@echo off
setlocal
cd /d "%~dp0"
set "VERSION=0.0.1"
title EtherBound Launcher v%VERSION%
color 0B
mode con cols=100 lines=32 >nul 2>&1

if not exist "%~dp0logs" mkdir "%~dp0logs"

echo.
echo  ------------------------------------------------------------------------------
echo    E T H E R B O U N D  //  DEV LAUNCHER
echo    Version %VERSION%  //  A systemic life sandbox
echo  ------------------------------------------------------------------------------
echo.
echo  [BOOT] Initializing local services...
echo.
set "ETHERBOUND_VERSION=%VERSION%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launcher.ps1"
echo.
endlocal
exit /b 0
