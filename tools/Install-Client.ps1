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

# Must match AddInIntegration.AddInGuid
$AddInGuid = 'D61E8EAA-B7F1-4EE3-8B8A-9D6C673A7E1F'

function Get-RegAsmPath {
    $regasm = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\regasm.exe'
    if (-not (Test-Path $regasm)) {
        $regasm = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\regasm.exe'
    }
    if (-not (Test-Path $regasm)) {
        throw 'Khong tim thay regasm.exe. Khach can cai .NET Framework 4.8.'
    }
    return $regasm
}

function Test-SolidWorksRunning {
    return $null -ne (Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Copy-SolidWorksInteropDeps {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$TargetDir
    )
    if (-not (Test-Path -LiteralPath $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $SourceDir -Filter 'SolidWorks.Interop.*.dll' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $TargetDir -Force
        }
}

function Remove-BodyExporterComRegistration {
  param([string]$Guid = $AddInGuid)
    $clsid = "{$Guid}"
    $progId = 'SolidWorksBodyExporter.AddIn'
    $roots = @(
        'HKLM:\SOFTWARE\Classes',
        'HKLM:\SOFTWARE\WOW6432Node\Classes'
    )
    foreach ($root in $roots) {
        foreach ($sub in @("CLSID\$clsid", $progId)) {
            $key = Join-Path $root $sub
            if (Test-Path -LiteralPath $key) {
                Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  Xoa registry: $key" -ForegroundColor DarkGray
            }
        }
    }
    $swKey = "HKLM:\SOFTWARE\SolidWorks\Addins\$clsid"
    if (Test-Path -LiteralPath $swKey) {
        Remove-Item -LiteralPath $swKey -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Xoa registry: $swKey" -ForegroundColor DarkGray
    }
}

function Unregister-AddInDll {
    param(
        [Parameter(Mandatory = $true)][string]$DllPath,
        [Parameter(Mandatory = $true)][string]$RegAsm,
        [string]$InteropSourceDir
    )
    if (-not (Test-Path -LiteralPath $DllPath)) { return }

    $dllDir = Split-Path -Parent $DllPath
    if ($InteropSourceDir) {
        Copy-SolidWorksInteropDeps -SourceDir $InteropSourceDir -TargetDir $dllDir
    }

    Write-Host "  Go dang ky COM: $DllPath" -ForegroundColor DarkGray
    $prevNative = $null
    if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
        $prevNative = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }
    $prevEap = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $RegAsm /unregister $DllPath 2>&1 | Out-Null
    }
    finally {
        $ErrorActionPreference = $prevEap
        if ($null -ne $prevNative) {
            $PSNativeCommandUseErrorActionPreference = $prevNative
        }
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host '  RegAsm khong go duoc COM (thieu DLL phu thuoc?) — xoa registry...' -ForegroundColor Yellow
        Remove-BodyExporterComRegistration
    }
}

function Clear-BodyExporterCaches {
    param([string]$AppDataDir, [string]$LocalAppDataDir)
    $files = @(
        (Join-Path $AppDataDir 'client-config-cache.json')
    )
    foreach ($path in $files) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
            Write-Host "  Xoa cache: $(Split-Path $path -Leaf)" -ForegroundColor DarkGray
        }
    }
    $iconDir = Join-Path $LocalAppDataDir 'icons'
    if (Test-Path -LiteralPath $iconDir) {
        Remove-Item -LiteralPath $iconDir -Recurse -Force
        Write-Host '  Xoa cache: icons\' -ForegroundColor DarkGray
    }
    $tempDir = Join-Path $env:TEMP 'SolidWorksBodyExporter'
    if (Test-Path -LiteralPath $tempDir) {
        Get-ChildItem -LiteralPath $tempDir -Filter 'addin.log' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        Write-Host '  Xoa log tam: %TEMP%\SolidWorksBodyExporter\addin.log' -ForegroundColor DarkGray
    }
}

function Remove-InstallRootBinaries {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return }
    Get-ChildItem -LiteralPath $Root -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -match '^\.(dll|exe|tlb)$' } |
        ForEach-Object {
            Write-Host "  Xoa file cu: $($_.Name)" -ForegroundColor DarkGray
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
        }
}

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

$regasm = Get-RegAsmPath
$settingsDir = Join-Path $env:APPDATA 'SolidWorksBodyExporter'
$oldInstalledDll = Join-Path $InstallRoot 'SolidWorksBodyExporter.AddIn.dll'

if (Test-SolidWorksRunning) {
    Write-Host ''
    Write-Host 'CANH BAO: SolidWorks dang chay.' -ForegroundColor Yellow
    Write-Host '  Dong SolidWorks truoc khi cai de tranh DLL bi khoa va add-in cu con trong bo nho.' -ForegroundColor Yellow
    Write-Host ''
}

# 0. Go phien ban cu + cache (giu license: settings.json, license.lic, trial.dat).
Write-Host '[0/6] Go phien ban cu va cache...' -ForegroundColor Yellow
Unregister-AddInDll -DllPath $oldInstalledDll -RegAsm $regasm -InteropSourceDir $source
$legacyPf = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorksBodyExporter.AddIn.dll'
if (Test-Path -LiteralPath $legacyPf) {
    Write-Host '  Phat hien DLL cu trong Program Files (nen xoa thu cong neu SW van load ban cu):' -ForegroundColor Yellow
    Write-Host "    $legacyPf" -ForegroundColor DarkGray
}
Clear-BodyExporterCaches -AppDataDir $settingsDir -LocalAppDataDir $InstallRoot
Remove-InstallRootBinaries -Root $InstallRoot

# 1. Copy binary moi.
Write-Host "[1/6] Copy binary moi toi $InstallRoot..." -ForegroundColor Yellow
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

# 2. Dang ky COM (regasm /codebase). Interop DLLs da copy o buoc 1.
Write-Host "[2/6] Dang ky COM trong registry..." -ForegroundColor Yellow
$prevNative = $null
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $prevNative = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
}
$prevEap = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & $regasm /codebase /tlb $installedDll 2>&1 | Out-Null
}
finally {
    $ErrorActionPreference = $prevEap
    if ($null -ne $prevNative) {
        $PSNativeCommandUseErrorActionPreference = $prevNative
    }
}
if ($LASTEXITCODE -ne 0) {
    throw "regasm.exe loi exit code $LASTEXITCODE. Hay chac chan script chay voi quyen Administrator."
}

# 3. Tao settings.json mac dinh (neu chua co — giu license khi nang cap).
Write-Host "[3/6] Kiem tra settings.json..." -ForegroundColor Yellow
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

# 3b. Ghi nhan dong y telemetry (khach da chap nhan tren bodyexporter.com/download).
Write-Host "[3b/6] Ghi telemetry-consent.json..." -ForegroundColor Yellow
$consentBundle = Join-Path $source 'telemetry-consent.bundle.json'
$consentPath = Join-Path $settingsDir 'telemetry-consent.json'
if (Test-Path $consentBundle) {
    Copy-Item -Path $consentBundle -Destination $consentPath -Force
} else {
    $consent = @{
        accepted    = $true
        version     = 1
        source      = 'installer'
        acceptedUtc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json
    Set-Content -Path $consentPath -Value $consent -Encoding UTF8
}

# 4. Tao desktop shortcut.
Write-Host "[4/6] Tao desktop shortcut..." -ForegroundColor Yellow
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
Write-Host "[5/6] Kiem tra phien ban..." -ForegroundColor Yellow
$fileVer = (Get-Item $installedDll).VersionInfo.FileVersion
Write-Host "  Phien ban DLL: $fileVer" -ForegroundColor Green

Write-Host "[6/6] Xong!" -ForegroundColor Green
Write-Host ""
Write-Host "Cai dat thanh cong:" -ForegroundColor Green
Write-Host "  DLL:      $installedDll"
Write-Host "  EXE:      $installedExe"
Write-Host "  Settings: $settingsPath"
Write-Host "  Shortcut: $lnkPath"
Write-Host ""
Write-Host "Mo SolidWorks (dong het truoc khi cai neu chua). Tools -> Add-Ins -> bat 'SolidWorks Body Exporter'."
Write-Host "Sau nang cap: cache client-config da xoa; license trong settings.json van giu."
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
