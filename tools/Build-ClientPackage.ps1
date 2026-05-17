#requires -Version 5.1
<#
.SYNOPSIS
  Build Release + Obfuscar, copy Launcher, tao thu muc zip gui khach dung thu.

.EXAMPLE
  .\tools\Build-ClientPackage.ps1 -ApiBaseUrl "https://bodyexporter-api.bodyexporter.workers.dev" -Version "0.7.5" -CreateZip
#>
param(
    [string]$ApiBaseUrl = "https://bodyexporter-api.bodyexporter.workers.dev",
    [string]$Version = "0.7.5",
    [switch]$SkipBuild,
    [switch]$CreateZip
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $repoRoot "SolidWorksBodyExporter.sln"))) {
    throw "Cannot find SolidWorksBodyExporter.sln (run from repo tools folder)."
}
$sln = Join-Path $repoRoot "SolidWorksBodyExporter.sln"
$addinProj = Join-Path $repoRoot "src\SolidWorksBodyExporter.AddIn\SolidWorksBodyExporter.AddIn.csproj"
$launcherProj = Join-Path $repoRoot "src\SolidWorksBodyExporter.Launcher\SolidWorksBodyExporter.Launcher.csproj"
$outName = "BodyExporter-v$Version-client"
$distRoot = Join-Path $repoRoot "dist"
$stage = Join-Path $distRoot $outName
$obfDir = Join-Path $repoRoot "src\SolidWorksBodyExporter.AddIn\bin\Release\net48\obfuscated"
$launcherDir = Join-Path $repoRoot "src\SolidWorksBodyExporter.Launcher\bin\Release\net48"

Write-Host "=== Build client package: $outName ===" -ForegroundColor Cyan

function Get-MsBuildExe {
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    return $null
}

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        dotnet tool restore 2>$null | Out-Null
        $msbuild = Get-MsBuildExe
        if (-not $msbuild) {
            throw "MSBuild (.NET Framework) not found. Install Visual Studio 2022 Build Tools."
        }
        Write-Host "Using MSBuild: $msbuild" -ForegroundColor DarkGray
        & $msbuild $addinProj /restore /p:Configuration=Release /p:Obfuscate=true /v:m
        if ($LASTEXITCODE -ne 0) { throw "AddIn Release+Obfuscate build failed." }
        & $msbuild $launcherProj /restore /p:Configuration=Release /v:m
        if ($LASTEXITCODE -ne 0) { throw "Launcher Release build failed." }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path (Join-Path $obfDir "SolidWorksBodyExporter.AddIn.dll"))) {
    throw "Missing obfuscated DLL. Run: dotnet build `"$addinProj`" -c Release -p:Obfuscate=true"
}
if (-not (Test-Path (Join-Path $launcherDir "SolidWorksBodyExporter.Launcher.exe"))) {
    throw "Missing Launcher.exe. Build Launcher Release first."
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# Binaries + dependencies (obfuscated folder already has Newtonsoft, OpenXml, etc.)
Copy-Item -Path (Join-Path $obfDir "*") -Destination $stage -Recurse -Force
Copy-Item -Path (Join-Path $launcherDir "SolidWorksBodyExporter.Launcher.exe") -Destination $stage -Force
if (Test-Path (Join-Path $launcherDir "SolidWorksBodyExporter.Launcher.exe.config")) {
    Copy-Item -Path (Join-Path $launcherDir "SolidWorksBodyExporter.Launcher.exe.config") -Destination $stage -Force
}

# Remove dev-only artefacts (client brings their own Excel template — never ship .xlsx)
@("Mapping.txt", "*.pdb", "*.xml", "*.xlsx") | ForEach-Object {
    Get-ChildItem -Path $stage -Filter $_ -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# Installer (patch ApiBaseUrl)
$installer = Join-Path $PSScriptRoot "Install-Client.ps1"
# Internal script (customers use Install-BodyExporter.cmd only).
$installerDest = Join-Path $stage "_install.ps1"
$installText = Get-Content $installer -Raw -Encoding UTF8
$installText = $installText.Replace('__API_BASE_URL__', $ApiBaseUrl)
Set-Content -Path $installerDest -Value $installText -Encoding UTF8
Get-ChildItem -Path $stage -Filter "Install-BodyExporter.ps1" -ErrorAction SilentlyContinue | Remove-Item -Force
$installCmd = Join-Path $PSScriptRoot "Install-BodyExporter.cmd"
if (Test-Path $installCmd) {
    Copy-Item -Path $installCmd -Destination (Join-Path $stage "Install-BodyExporter.cmd") -Force
}

$readme = @"
SolidWorks Body Exporter v$Version (dung thu)
============================================

Yeu cau:
  - Windows 10/11, SolidWorks 2024 (hoac ban ban ho tro)
  - .NET Framework 4.8
  - Quyen Administrator khi cai (chi lan dau)

Cai dat:
  1. Giai nen thu muc nay.
  2. Double-click Install-BodyExporter.cmd (chon Yes khi Windows hoi Admin).
  3. Mo SolidWorks -> Tools -> Add-Ins -> bat "SolidWorks Body Exporter".
  4. Bam shortcut "Body Exporter" tren Desktop (hoac icon trong SW neu hien).

Dung thu:
  - Lan dau mo: tu dong 14 ngay trial (luu tren may, gan fingerprint).
  - Hoac dan license key (UUID) trong License -> Activate / help.

Excel mau (template):
  - Goi cai KHONG kem file .xlsx - khach tu chuan bi file cong ty (co {{Body Name}}, {{Length}}, ...).
  - Export -> Choose default Excel template... (luu duong dan tren may).
  - Export -> Open Excel template / Fill Excel template / Open last filled export.

Bao mat (v0.7.5+):
  - DLL Release da obfuscate (Obfuscar): ten method private bi doi ten; class public COM/XAML giu nguyen.
  - Trial 14 ngay: server /v1/trial/start (can deploy Worker).
  - settings.json: file settings.seal (DPAPI, gan user + may) - khong sua tay ApiBaseUrl/expiry.
  - License online re-check moi 7 ngay (offline toi da 7 ngay neu JWT hop le).

KHONG gui lai cho khach:
  - Thu muc server/, drapf, source code, file .pdb
  - Admin token, JWT private key, Sepay secret

Ho tro: hotro@bodyexporter.com
Mua license: https://bodyexporter.com/buy
"@
Set-Content -Path (Join-Path $stage "HUONG-DAN-CAI-DAT.txt") -Value $readme -Encoding UTF8

Write-Host ""
Write-Host "Package ready:" -ForegroundColor Green
Write-Host "  $stage"
Write-Host ""
Write-Host "Gui khach: zip ca thu muc '$outName' (chi file trong do)." -ForegroundColor Yellow
Get-ChildItem $stage | Format-Table Name, Length -AutoSize

# Sanity check: obfuscated private method names should not appear as plain text in the shipped DLL.
$shippedDll = Join-Path $stage "SolidWorksBodyExporter.AddIn.dll"
$probeBytes = [System.IO.File]::ReadAllBytes($shippedDll)
$probeText = [System.Text.Encoding]::UTF8.GetString($probeBytes)
foreach ($needle in @("ReadTrialFile", "WriteTrialFile", "RepairSettingsIfTampered")) {
    if ($probeText.Contains($needle)) {
        Write-Warning "Obfuscation check: '$needle' still found as literal in shipped DLL. Rebuild with Obfuscate=true and ensure LicenseManager is not [Obfuscation(Exclude=true)]."
    }
}

if ($CreateZip) {
    $zipPath = Join-Path $distRoot "$outName.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $stage -DestinationPath $zipPath -Force
    Write-Host "Zip: $zipPath" -ForegroundColor Green
}
