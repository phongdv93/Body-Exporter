#requires -Version 5.1
<#
.SYNOPSIS
    Creates a "Body Exporter" shortcut on the user's *real* Desktop, even when the
    Desktop folder is redirected by OneDrive.

.DESCRIPTION
    The previous script used "$env:USERPROFILE\Desktop" which fails on machines
    where OneDrive has redirected the Desktop shell folder to
    "$env:USERPROFILE\OneDrive\Desktop". We instead use the Shell API's
    GetFolderPath('Desktop'), which honours redirection and returns the path
    Windows Explorer actually paints the desktop from. The script also:

      - Falls back to a non-redirected path when GetFolderPath returns empty.
      - Uses the EXE's embedded icon (index 0) so the shortcut reflects the
        same BodyExporter square the Launcher draws in the taskbar - no separate
        .ico file shipping required.
      - Replaces any existing shortcut (overwrite) so re-running the script after
        a version bump just refreshes the link without duplicating it.

.PARAMETER ExePath
    Full path to SolidWorksBodyExporter.Launcher.exe. Defaults to the Debug build
    output next to this script.

.PARAMETER ShortcutName
    Display name. Defaults to "Body Exporter" (the .lnk extension is appended
    automatically).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Install-DesktopShortcut.ps1
#>
param(
    [string]$ExePath,
    [string]$ShortcutName = 'Body Exporter'
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $ExePath  = Join-Path $repoRoot 'src\SolidWorksBodyExporter.Launcher\bin\Debug\net48\SolidWorksBodyExporter.Launcher.exe'
}

if (-not (Test-Path $ExePath)) {
    throw "Launcher EXE not found: $ExePath. Build the Launcher project first."
}

# GetFolderPath('Desktop') = SpecialFolder.Desktop in .NET, which respects OneDrive
# redirection. The previous "$env:USERPROFILE\Desktop" approach silently created
# shortcuts in a folder the user could not see because their Desktop was actually
# OneDrive-redirected.
$desktop = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktop)) {
    # Last-resort fallback for unusual configurations where the shell returns empty.
    $desktop = Join-Path $env:USERPROFILE 'Desktop'
}
if (-not (Test-Path $desktop)) {
    New-Item -ItemType Directory -Path $desktop -Force | Out-Null
}

$lnkPath = Join-Path $desktop ("{0}.lnk" -f $ShortcutName)

$shell    = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath       = $ExePath
$shortcut.WorkingDirectory = Split-Path -Parent $ExePath
# IconLocation '<exe>,0' tells Windows to pull the first icon resource baked into
# the EXE, which Build-LauncherIco.ps1 already populated. No external .png/.ico
# file needs to ship alongside.
$shortcut.IconLocation     = "$ExePath,0"
$shortcut.Description      = 'Open the SolidWorks Body Exporter window without the SolidWorks ribbon.'
$shortcut.Save()

Write-Host "Shortcut created at:" -ForegroundColor Green
Write-Host "  $lnkPath"
Write-Host "Target:  $ExePath"
Write-Host "Icon:    embedded in target EXE (no external .ico needed)"
