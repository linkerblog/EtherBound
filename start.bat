@echo off
setlocal
cd /d "%~dp0"
title EtherBound Launcher
color 0A
mode con cols=100 lines=30 >nul 2>&1

if not exist "%~dp0logs" mkdir "%~dp0logs"

echo.
echo ================================================================
echo                         ETHERBOUND
echo ================================================================
echo Starting server and web client...
echo Logs: logs\server.log and logs\web.log
echo.

start "EtherBound Server" /b cmd /c call "%~dp0scripts\run-server.bat" ^> "%~dp0logs\server.log" 2^>^&1
start "EtherBound Web" /b cmd /c call "%~dp0scripts\run-web.bat" ^> "%~dp0logs\web.log" 2^>^&1

echo EtherBound services started.
echo Server: http://127.0.0.1:8000
echo Client: http://127.0.0.1:5173
echo.
echo Use stop.bat to stop all services.
endlocal
exit /b 0
