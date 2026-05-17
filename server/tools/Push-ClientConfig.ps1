# Push client-config to Cloudflare KV (UTF-8 JSON — avoids mojibake on Vietnamese text).
# Usage: .\tools\Push-ClientConfig.ps1
# Or: $env:BODYEXPORTER_ADMIN_TOKEN = "..."; .\server\tools\Push-ClientConfig.ps1 -AmountVnd 1590000

param(
    [string]$ApiUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$AdminToken = $env:BODYEXPORTER_ADMIN_TOKEN,
    [long]$AmountVnd = 1590000,
    [string]$ConfigPath = (Join-Path $PSScriptRoot "..\config\client-config.json")
)

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Write-Error "Set BODYEXPORTER_ADMIN_TOKEN or pass -AdminToken (same as ADMIN_TOKEN on Worker)."
    exit 1
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    Write-Error "Missing $ConfigPath"
    exit 1
}

$cfg = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$cfg.paymentVnSepayUrl = "https://qr.sepay.vn/img?bank=ACB&acc=4518527&amount=$AmountVnd&des=Body%20Export%20License"
$cfg.updateManifestUrl = "$ApiUrl/v1/update-manifest"
if (-not $cfg.latestVersion) { $cfg.latestVersion = "0.7.5" }

$json = $cfg | ConvertTo-Json -Depth 6 -Compress
$utf8 = New-Object System.Text.UTF8Encoding $false
$bytes = $utf8.GetBytes($json)

Invoke-RestMethod -Uri "$ApiUrl/admin/client-config" -Method PUT `
    -Headers @{
        Authorization  = "Bearer $AdminToken"
        "Content-Type" = "application/json; charset=utf-8"
    } `
    -Body $bytes | Out-Null

$live = Invoke-RestMethod -Uri "$ApiUrl/v1/client-config"
Write-Host "OK. authorName = $($live.authorName)"
Write-Host "OK. supportEmail = $($live.supportEmail)"
Write-Host "OK. paymentWebUrl = $($live.paymentWebUrl)"
Write-Host "Delete app cache: %APPDATA%\SolidWorksBodyExporter\client-config-cache.json then reopen License."
