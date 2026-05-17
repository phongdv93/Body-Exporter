# Publish /v1/update-manifest (client tu check khi mo app + nut Check for updates).

param(
    [string]$ApiUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$AdminToken = $env:BODYEXPORTER_ADMIN_TOKEN,
    [string]$Version = "0.7.3",
    [string]$DownloadUrl = "",
    [string]$ReleaseNotes = "Tai zip moi, giai nen, chay Install-BodyExporter.cmd (Admin), khoi dong lai SolidWorks."
)

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Write-Error "Set BODYEXPORTER_ADMIN_TOKEN"
    exit 1
}

$body = @{
    version      = $Version
    downloadUrl  = $DownloadUrl
    sha256       = ""
    releaseNotes = $ReleaseNotes
} | ConvertTo-Json -Compress

$utf8 = New-Object System.Text.UTF8Encoding $false
Invoke-RestMethod -Uri "$ApiUrl/admin/update-manifest" -Method PUT `
    -Headers @{
        Authorization = "Bearer $AdminToken"
        "Content-Type" = "application/json; charset=utf-8"
    } -Body $utf8.GetBytes($body) | Out-Null

# Cap nhat latestVersion trong client-config (fallback neu manifest loi)
$cfg = Invoke-RestMethod -Uri "$ApiUrl/v1/client-config"
$cfg.latestVersion = $Version
if ([string]::IsNullOrWhiteSpace($cfg.updateManifestUrl)) {
    $cfg.updateManifestUrl = "$ApiUrl/v1/update-manifest"
}
$cfgJson = $cfg | ConvertTo-Json -Depth 6 -Compress
Invoke-RestMethod -Uri "$ApiUrl/admin/client-config" -Method PUT `
    -Headers @{
        Authorization = "Bearer $AdminToken"
        "Content-Type" = "application/json; charset=utf-8"
    } -Body $utf8.GetBytes($cfgJson) | Out-Null

Write-Host "Published update manifest version $Version"
Write-Host "GET $ApiUrl/v1/update-manifest"
