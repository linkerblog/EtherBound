$ErrorActionPreference = "SilentlyContinue"

# Stop only development processes started by the EtherBound launcher.
$roots = Get-CimInstance Win32_Process |
    Where-Object {
        $_.CommandLine -match "uvicorn etherbound\.app:app" -or
        $_.CommandLine -match "[\\/]EtherBound[\\/]web[\\/].*vite"
    }

$roots | ForEach-Object {
    & taskkill.exe /PID $_.ProcessId /T /F *> $null
}

# Release the default development ports if a child process escaped its launcher.
Get-NetTCPConnection -LocalPort 8000,5173 -State Listen |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force }
