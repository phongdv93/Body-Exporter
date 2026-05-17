<#
.SYNOPSIS
    1-click installer cho khách hàng. Copy DLL + EXE vào LocalAppData, đăng ký
    COM, tạo shortcut desktop. Khách double-click file này (chạy với quyền admin)
    là xong.

.DESCRIPTION
    Lý do cần admin: regasm.exe sửa HKLM\SOFTWARE\Classes để đăng ký COM. Không
    có cách nào quanh chuyện này cho COM in-process server. Nếu khách không có
    quyền admin trên máy công ty -> dùng portable mode (Launcher EXE chạy không
    cần COM, nhưng SolidWorks cũng không load addin trực tiếp được, chỉ qua IPC
    từ Launcher).

    Script này KHÔNG dùng MSI để giữ ngưỡng cài đặt thấp - khách không quen Wix
    Toolset chỉ cần download .ps1 + 2 file binary là cài được.

.PARAMETER InstallRoot
    Thư mục cài đặt. Default: %LOCALAPPDATA%\SolidWorksBodyExporter\

.PARAMETER ApiBaseUrl
    URL Cloudflare Worker của mày. Hard-code đây trước khi gửi cho khách để
    settings.json mặc định trỏ đúng server.
#>
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'SolidWorksBodyExporter'),
    [string]$ApiBaseUrl = '__API_BASE_URL__'
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator
{
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-ForKey
{
    param([string]$Message = 'Bam Enter de dong cua so...')
    try { Read-Host $Message } catch { Start-Sleep -Seconds 30 }
}

if (-not (Test-IsAdministrator)) {
    Write-Host ''
    Write-Host 'LOI: Can quyen Administrator (de dang ky COM).' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Cach dung:' -ForegroundColor Yellow
    Write-Host '  Double-click Install-BodyExporter.cmd (trong cung thu muc).'
    Write-Host '  Khong chay file _install.ps1 bang tay.'
    Write-Host ''
    Wait-ForKey
    exit 1
}

try {

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  SolidWorks Body Exporter - Installer" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""

# Source = thư mục chứa script này (assume Installer ship cùng DLL+EXE).
$source = $PSScriptRoot
$addinDll  = Join-Path $source 'SolidWorksBodyExporter.AddIn.dll'
$launcher  = Join-Path $source 'SolidWorksBodyExporter.Launcher.exe'

if (-not (Test-Path $addinDll)) {
    throw "Khong tim thay $addinDll. Hay copy ca DLL va EXE vao cung thu muc voi installer."
}
if (-not (Test-Path $launcher)) {
    throw "Khong tim thay $launcher. Hay copy ca DLL va EXE vao cung thu muc voi installer."
}

# 1. Tao install root + copy binary.
Write-Host "[1/5] Copy binary toi $InstallRoot..." -ForegroundColor Yellow
if (-not (Test-Path $InstallRoot)) {
    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
}
Copy-Item -Path $addinDll -Destination $InstallRoot -Force
Copy-Item -Path $launcher -Destination $InstallRoot -Force

# Copy them cac DLL phu thuoc neu co (Newtonsoft.Json, DocumentFormat.OpenXml...).
Get-ChildItem -Path $source -Filter '*.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'SolidWorksBodyExporter.AddIn.dll' } |
    ForEach-Object { Copy-Item $_.FullName -Destination $InstallRoot -Force }

$installedDll = Join-Path $InstallRoot 'SolidWorksBodyExporter.AddIn.dll'
$installedExe = Join-Path $InstallRoot 'SolidWorksBodyExporter.Launcher.exe'

# 2. Dang ky COM (regasm /codebase).
Write-Host "[2/5] Dang ky COM trong registry..." -ForegroundColor Yellow
$regasm = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\regasm.exe'
if (-not (Test-Path $regasm)) {
    $regasm = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\regasm.exe'
}
if (-not (Test-Path $regasm)) {
    throw "Khong tim thay regasm.exe. Khach can cai .NET Framework 4.8."
}
& $regasm /codebase /tlb $installedDll | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "regasm.exe loi exit code $LASTEXITCODE. Hay chac chan script chay voi quyen Administrator."
}

# 3. Tao settings.json mac dinh.
Write-Host "[3/5] Ghi settings.json mac dinh..." -ForegroundColor Yellow
$settingsDir = Join-Path $env:APPDATA 'SolidWorksBodyExporter'
if (-not (Test-Path $settingsDir)) { New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null }
$settingsPath = Join-Path $settingsDir 'settings.json'
if (-not (Test-Path $settingsPath)) {
    # Chi ghi mac dinh khi chua co. Neu khach da cau hinh roi (re-install) thi giu.
    $settings = @{
        ApiBaseUrl = $ApiBaseUrl
        LicenseKey = ''
        CachedToken = ''
        CachedTokenExpiresUtc = $null
        TokenBoundMachineHash = ''
    } | ConvertTo-Json
    Set-Content -Path $settingsPath -Value $settings -Encoding UTF8
}

# 4. Tao desktop shortcut.
Write-Host "[4/5] Tao desktop shortcut..." -ForegroundColor Yellow
$desktop = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktop)) { $desktop = Join-Path $env:USERPROFILE 'Desktop' }
$lnkPath = Join-Path $desktop 'Body Exporter.lnk'
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath       = $installedExe
$lnk.WorkingDirectory = $InstallRoot
$lnk.IconLocation     = "$installedExe,0"
$lnk.Description      = 'Open the SolidWorks Body Exporter window.'
$lnk.Save()

# 5. Done.
Write-Host "[5/5] Xong!" -ForegroundColor Green
Write-Host ""
Write-Host "Cai dat thanh cong:" -ForegroundColor Green
Write-Host "  DLL:      $installedDll"
Write-Host "  EXE:      $installedExe"
Write-Host "  Settings: $settingsPath"
Write-Host "  Shortcut: $lnkPath"
Write-Host ""
Write-Host "Khoi dong SolidWorks. Mo Tools -> Add-Ins de bat 'SolidWorks Body Exporter'."
Write-Host "Lan dau bam icon Body Exporter, dan license key vao."
Write-Host ""
Wait-ForKey

}
catch {
    Write-Host ''
    Write-Host 'CAI DAT THAT BAI:' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host 'Thu lai: dong SolidWorks, chay Install-BodyExporter.cmd (Admin).' -ForegroundColor Yellow
    Write-Host ''
    Wait-ForKey
    exit 1
}
