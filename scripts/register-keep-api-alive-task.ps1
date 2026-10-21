# Registers a Windows Scheduled Task that pings API /health every 1 minute.
# Keep-alive both warms IIS and triggers order sync when Settings interval is due.
# Run once as Administrator on the VPS:
#   powershell -NoProfile -ExecutionPolicy Bypass -File ".\register-keep-api-alive-task.ps1"
#
# Unregister:
#   Unregister-ScheduledTask -TaskName "DropKinhbac-KeepApiAlive" -Confirm:$false

param(
    [string]$TaskName = "DropKinhbac-KeepApiAlive",
    [string]$HealthUrl = "https://4seller-api.kbt.global/api/v1/health",
    [int]$IntervalMinutes = 1
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "keep-api-alive.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Missing script: $scriptPath"
}

$principal = New-ScheduledTaskPrincipal `
    -UserId "SYSTEM" `
    -LogonType ServiceAccount `
    -RunLevel Highest

$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument ("-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -HealthUrl `"$HealthUrl`"")

# RepetitionDuration ~10 years (MaxValue breaks on some Windows Server builds).
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 2) `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
      -Description "Ping Dropship API /health every minute: keep IIS worker warm + schedule order sync/Telegram when due." `
    -Force | Out-Null

Write-Host "Registered task '$TaskName' every $IntervalMinutes min -> $HealthUrl"
Write-Host "Script: $scriptPath"
Write-Host "Health also schedules order sync when Settings interval is due."
Write-Host "Test now: Start-ScheduledTask -TaskName '$TaskName'"
Write-Host "Verify: Invoke-WebRequest $HealthUrl | Select-Object -ExpandProperty Content"
