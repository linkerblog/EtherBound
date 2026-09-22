$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root "logs"
$cleanup = Join-Path $root "cleanup.bat"

if (-not (Test-Path -LiteralPath $logs)) {
    New-Item -ItemType Directory -Path $logs | Out-Null
}

$serverLog = Join-Path $logs "server.log"
$webLog = Join-Path $logs "web.log"
$server = $null
$web = $null

try {
    $serverCommand = "/d /c call `"$root\scripts\run-server.bat`" > `"$serverLog`" 2>&1"
    $webCommand = "/d /c call `"$root\scripts\run-web.bat`" > `"$webLog`" 2>&1"
    $server = Start-Process -FilePath "cmd.exe" -ArgumentList $serverCommand -WorkingDirectory $root -WindowStyle Hidden -PassThru
    $web = Start-Process -FilePath "cmd.exe" -ArgumentList $webCommand -WorkingDirectory $root -WindowStyle Hidden -PassThru

    Write-Host "  [ OK ] WORLD SERVER   http://127.0.0.1:8000"
    Write-Host "  [ OK ] WEB CLIENT     http://127.0.0.1:5173"
    Write-Host ""
    Write-Host "  ------------------------------------------------------------------------------"
    Write-Host "  VERSION  $env:ETHERBOUND_VERSION        LOGS  logs\server.log  |  logs\web.log"
    Write-Host "  ------------------------------------------------------------------------------"
    Write-Host ""
    Write-Host "  Services are supervised by this window. Close it to clean up all processes."

    while (-not $server.HasExited -or -not $web.HasExited) {
        Start-Sleep -Milliseconds 500
        $server.Refresh()
        $web.Refresh()
    }
}
finally {
    & cmd.exe /d /c "call `"$cleanup`""
}
