# Dat entitlementPolicy tren Worker (kill switch / cap 14 ngay) — khong can DLL moi.
# Usage:
#   .\Set-EntitlementPolicy.ps1 -Mode trial_only -CapDays 14
#   .\Set-EntitlementPolicy.ps1 -Mode normal
#   .\Set-EntitlementPolicy.ps1 -Mode blocked -Message "Bao tri he thong"

param(
    [string]$ApiUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$AdminToken = $env:BODYEXPORTER_ADMIN_TOKEN,
    [ValidateSet("normal", "cap_days", "trial_only", "blocked")]
    [string]$Mode = "normal",
    [int]$CapDays = 14,
    [string]$Message = ""
)

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Write-Error "Set BODYEXPORTER_ADMIN_TOKEN hoac -AdminToken"
    exit 1
}

$live = Invoke-RestMethod -Uri "$ApiUrl/v1/client-config"
if (-not $live) { $live = @{} }

$live | Add-Member -NotePropertyName entitlementPolicy -NotePropertyValue @{
    mode    = $Mode
    capDays = $CapDays
    message = $Message
} -Force

$json = $live | ConvertTo-Json -Depth 6 -Compress
$utf8 = New-Object System.Text.UTF8Encoding $false
$bytes = $utf8.GetBytes($json)

Invoke-RestMethod -Uri "$ApiUrl/admin/client-config" -Method PUT `
    -Headers @{
        Authorization = "Bearer $AdminToken"
        "Content-Type" = "application/json; charset=utf-8"
    } -Body $bytes | Out-Null

Write-Host "entitlementPolicy.mode = $Mode capDays = $CapDays"
Write-Host "Xoa cache client: %APPDATA%\SolidWorksBodyExporter\client-config-cache.json"
