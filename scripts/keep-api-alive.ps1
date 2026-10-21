# Keep Dropship API worker warm AND pulse automatic order sync (Telegram).
# GET /api/v1/health schedules sync when fourseller.order_sync.interval_minutes has elapsed.
# Use with Windows Task Scheduler (every 1 minute recommended; must be <= Settings interval).
#
# Correct URL (do NOT use /healthy alone without deploying latest API — prefer full path):
#   https://4seller-api.kbt.global/api/v1/health
#
# Example Task Scheduler action:
#   Program: powershell.exe
#   Arguments:
#     -NoProfile -ExecutionPolicy Bypass -File "C:\Apps\drop.kinhbac.ai\scripts\keep-api-alive.ps1"
#
# Optional: override URL
#   -File "...\keep-api-alive.ps1" -HealthUrl "https://4seller-api.kbt.global/api/v1/health"

param(
    [string]$HealthUrl = "https://4seller-api.kbt.global/api/v1/health",
    # Sync-on-due runs inside /health (can take >30s with many sellers / slow proxy).
    [int]$TimeoutSec = 120,
    [string]$LogDir = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($LogDir)) {
    $LogDir = Join-Path $PSScriptRoot "logs"
}

if (-not (Test-Path -LiteralPath $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$logFile = Join-Path $LogDir ("keep-api-alive-{0:yyyyMM}.log" -f (Get-Date))
$stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

try {
    $response = Invoke-WebRequest -Uri $HealthUrl -Method Get -UseBasicParsing -TimeoutSec $TimeoutSec
    $line = "[$stamp] OK  HTTP $($response.StatusCode)  $HealthUrl"
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
    Write-Output $line
    exit 0
}
catch {
    $line = "[$stamp] FAIL  $HealthUrl  $($_.Exception.Message)"
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
    Write-Error $line
    exit 1
}
