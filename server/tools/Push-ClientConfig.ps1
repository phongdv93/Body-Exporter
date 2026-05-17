# Push client-config to Cloudflare KV (updates QR amount in License window).
# Usage: .\tools\Push-ClientConfig.ps1
# Or from repo root: .\server\tools\Push-ClientConfig.ps1

param(
    [string]$ApiUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$AdminToken = $env:BODYEXPORTER_ADMIN_TOKEN,
    [long]$AmountVnd = 1590000
)

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    Write-Error "Set BODYEXPORTER_ADMIN_TOKEN or pass -AdminToken (same as ADMIN_TOKEN on Worker)."
    exit 1
}

$cfg = @{
    authorName                   = "Gió"
    supportEmail                 = "hotro@bodyexporter.com"
    supportUrl                   = "https://bodyexporter.com"
    paymentWebUrl                = "https://bodyexporter.com/buy"
    paymentWebTitle              = "Mở trang thanh toán"
    paymentWebBody               = "Chọn QR chuyển khoản hoặc thẻ trên web. Nhập email để nhận license tự động."
    paymentVnTitle              = "Thanh toán Việt Nam (Sepay)"
    paymentVnBody               = "Quét QR bên dưới và chuyển đúng số tiền. Ghi email trong nội dung CK (vd. BE email@ban.com) để nhận license tự động."
    paymentVnSepayUrl           = "https://qr.sepay.vn/img?bank=ACB&acc=4518527&amount=$AmountVnd&des=Body%20Export%20License"
    paymentIntlTitle            = ""
    paymentIntlBody             = ""
    paymentIntlLemonsqueezyUrl  = ""
    paymentIntlTripleUrl        = ""
    latestVersion               = "0.7.3"
    updateManifestUrl           = "$ApiUrl/v1/update-manifest"
    downloadPageUrl             = ""
    entitlementPolicy           = @{
        mode    = "normal"
        capDays = 14
        message = ""
    }
} | ConvertTo-Json -Depth 6 -Compress

$utf8 = New-Object System.Text.UTF8Encoding $false
$bytes = $utf8.GetBytes($cfg)
Invoke-RestMethod -Uri "$ApiUrl/admin/client-config" -Method PUT `
    -Headers @{
        Authorization = "Bearer $AdminToken"
        "Content-Type" = "application/json; charset=utf-8"
    } `
    -Body $bytes | Out-Null

$live = Invoke-RestMethod -Uri "$ApiUrl/v1/client-config"
Write-Host "OK. paymentWebUrl = $($live.paymentWebUrl)"
Write-Host "OK. paymentVnSepayUrl = $($live.paymentVnSepayUrl)"
Write-Host "Delete app cache: %APPDATA%\SolidWorksBodyExporter\client-config-cache.json then reopen License."
