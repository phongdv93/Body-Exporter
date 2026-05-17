#requires -RunAsAdministrator
<#
.SYNOPSIS
  Copy DLL moi tu build Debug/Release vao LocalAppData va regasm lai.
  Dung sau khi dotnet build - SolidWorks khong tu load DLL tu bin\Debug.

.EXAMPLE
  dotnet build src\SolidWorksBodyExporter.AddIn\SolidWorksBodyExporter.AddIn.csproj -c Debug
  .\tools\Update-DevAddIn.ps1
  # Dong het SolidWorks, mo lai, bat add-in.
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$srcDir = Join-Path $repoRoot "src\SolidWorksBodyExporter.AddIn\bin\$Configuration\net48"
$installRoot = Join-Path $env:LOCALAPPDATA "SolidWorksBodyExporter"
$addinDll = Join-Path $srcDir "SolidWorksBodyExporter.AddIn.dll"

if (-not (Test-Path $addinDll)) {
    throw "Khong thay $addinDll - hay build truoc: dotnet build ... -c $Configuration"
}

$swProgFiles = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorksBodyExporter.AddIn.dll"
if (Test-Path $swProgFiles) {
    Write-Warning "Tim thay DLL cu trong Program Files (SW co the load file nay thay vi ban moi):"
    Write-Warning "  $swProgFiles"
    Write-Warning "  Nen xoa hoac doi ten file do, roi khoi dong lai SolidWorks."
}

Write-Host "Copy $Configuration -> $installRoot" -ForegroundColor Cyan
if (-not (Test-Path $installRoot)) {
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
}
Get-ChildItem -Path $srcDir -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination $installRoot -Force
}
$launcher = Join-Path $repoRoot "src\SolidWorksBodyExporter.Launcher\bin\$Configuration\net48\SolidWorksBodyExporter.Launcher.exe"
if (Test-Path $launcher) {
    Copy-Item $launcher -Destination $installRoot -Force
}

$installedDll = Join-Path $installRoot "SolidWorksBodyExporter.AddIn.dll"
$ver = (Get-Item $installedDll).VersionInfo.FileVersion
Write-Host "Installed file version: $ver" -ForegroundColor Green

$regasm = Join-Path $env:SystemRoot "Microsoft.NET\Framework64\v4.0.30319\regasm.exe"
if (-not (Test-Path $regasm)) {
    $regasm = Join-Path $env:SystemRoot "Microsoft.NET\Framework\v4.0.30319\regasm.exe"
}
& $regasm /codebase /tlb $installedDll | Out-Null
if ($LASTEXITCODE -ne 0) { throw "regasm failed ($LASTEXITCODE)" }

Write-Host ""
Write-Host "Xong. DONG HET SolidWorks roi mo lai de nap DLL moi." -ForegroundColor Yellow
$logPath = Join-Path $env:APPDATA "SolidWorksBodyExporter\addin.log"
Write-Host "Log: $logPath" -ForegroundColor DarkGray
