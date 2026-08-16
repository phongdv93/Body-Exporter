# Publish /v1/update-manifest (client tu check khi mo app + nut Check for updates).
#
# DUNG (thay TOKEN bang ADMIN_TOKEN trong drapf / wrangler secret):
#   $env:BODYEXPORTER_ADMIN_TOKEN = "Hlf4cPdTLC81..."
#   .\Publish-UpdateManifest.ps1 -Version "0.7.5" -DownloadUrl "https://..."
#
# Hoac:
#   .\Publish-UpdateManifest.ps1 -AdminToken "..." -Version "0.7.5" -DownloadUrl "..."

param(
    [string]$ApiUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$AdminToken = $env:BODYEXPORTER_ADMIN_TOKEN,
    [string]$Version = "0.7.5",
    [string]$DownloadUrl = "",
    [string]$ReleaseNotes = "Tai zip moi, giai nen, chay Install-BodyExporter.cmd (Admin), khoi dong lai SolidWorks."
)

$ErrorActionPreference = "Stop"
if ($null -eq $DownloadUrl) { $DownloadUrl = "" }
$DownloadUrl = $DownloadUrl.Trim()

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Write-Error @"
Thieu admin token. KHONG dat ten token lam ten bien env.

  Sai:  `$env:Hlf4cPdTLC81... = '...'
  Dung: `$env:BODYEXPORTER_ADMIN_TOKEN = 'Hlf4cPdTLC81...'

Hoac: .\Publish-UpdateManifest.ps1 -AdminToken '...' -Version '0.7.5' -DownloadUrl 'https://...'
"@
    exit 1
}

$headers = @{
    Authorization  = "Bearer $AdminToken"
    "Content-Type" = "application/json; charset=utf-8"
}
$utf8 = New-Object System.Text.UTF8Encoding $false

function Invoke-AdminPut($path, $jsonBody) {
    try {
        Invoke-RestMethod -Uri "$ApiUrl$path" -Method PUT -Headers $headers -Body $utf8.GetBytes($jsonBody)
    }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) { $detail = $_.ErrorDetails.Message }
        throw "PUT $path failed: $detail (kiem tra ADMIN_TOKEN / wrangler secret put ADMIN_TOKEN)"
    }
}

$manifestBody = @{
    version      = $Version
    downloadUrl  = $DownloadUrl
    sha256       = ""
    releaseNotes = $ReleaseNotes
} | ConvertTo-Json -Compress

Invoke-AdminPut "/admin/update-manifest" $manifestBody
Write-Host "OK update-manifest version $Version" -ForegroundColor Green

$cfg = Invoke-RestMethod -Uri "$ApiUrl/v1/client-config"
if ($cfg -is [pscustomobject]) {
    $cfg | Add-Member -NotePropertyName latestVersion -NotePropertyValue $Version -Force
    if ([string]::IsNullOrWhiteSpace($cfg.updateManifestUrl)) {
        $cfg | Add-Member -NotePropertyName updateManifestUrl -NotePropertyValue "$ApiUrl/v1/update-manifest" -Force
    }
}
$cfgJson = $cfg | ConvertTo-Json -Depth 8 -Compress
Invoke-AdminPut "/admin/client-config" $cfgJson
Write-Host "OK client-config latestVersion = $Version" -ForegroundColor Green

Write-Host ""
Write-Host "Verify: GET $ApiUrl/v1/update-manifest (admin Bearer)"
$check = Invoke-RestMethod -Uri "$ApiUrl/v1/update-manifest" -Headers $headers
Write-Host "  version     = $($check.version)"
Write-Host "  downloadUrl = $($check.downloadUrl)"
