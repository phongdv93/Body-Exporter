#requires -Version 5.1
<#
.SYNOPSIS
    Packages the procedural BodyExporter PNGs (sizes 20/32/40/64/96/128) into a single
    multi-resolution Windows .ico container so the Launcher EXE can embed it.

.DESCRIPTION
    Windows uses the .ico container at:
      - shell extraction time (taskbar, Start menu, file explorer)
      - shortcut display
      - EXE's "default icon" resource block

    A multi-size .ico lets the shell pick the best resolution for the context (16 px
    on the taskbar overflow flyout, 256 px on the jump list peek). We embed the PNGs
    directly (Vista+ format) rather than re-encoding to BMP, so the alpha channel and
    file size both stay clean.

    Run this once after editing AddInIcons.cs, then commit the resulting .ico. The
    Launcher.csproj has ApplicationIcon=assets\BodyExporter.ico so the next build
    bakes it into the EXE.

.PARAMETER PngFolder
    Folder containing BodyExporter_*.png files. Defaults to the runtime cache
    populated by AddInIcons.EnsurePngs(): %LOCALAPPDATA%\SolidWorksBodyExporter\icons

.PARAMETER OutputPath
    Destination .ico path. Defaults to <repo-root>\assets\BodyExporter.ico relative
    to this script's location.
#>
param(
    [string]$PngFolder = (Join-Path $env:LOCALAPPDATA 'SolidWorksBodyExporter\icons'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

# Resolve default output path relative to script location so the script behaves
# the same regardless of the caller's current directory.
if (-not $OutputPath) {
    $repoRoot   = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $repoRoot 'assets\BodyExporter.ico'
}

if (-not (Test-Path $PngFolder)) {
    throw "PNG folder not found: $PngFolder. Run SolidWorks once with the addin loaded to populate it, or pass -PngFolder."
}

# Use the standard ICO size set. ICO supports up to 256 px per entry; we cap at 128
# because that's the largest size AddInIcons generates. Order does not matter to
# Windows but ascending feels right.
$wantedSizes = 16, 20, 32, 40, 48, 64, 96, 128
$pngPaths = New-Object System.Collections.Generic.List[string]
$sizes    = New-Object System.Collections.Generic.List[int]

Add-Type -AssemblyName System.Drawing

foreach ($size in $wantedSizes) {
    $candidate = Join-Path $PngFolder "BodyExporter_$size.png"
    if (Test-Path $candidate) {
        $pngPaths.Add($candidate) | Out-Null
        $sizes.Add($size)        | Out-Null
        continue
    }

    # Size missing - resample from the largest available PNG so the .ico stays
    # complete. This is the case for 16 / 48 which AddInIcons does not emit.
    $largest = Get-ChildItem -Path $PngFolder -Filter 'BodyExporter_*.png' |
               Sort-Object { [int]([regex]::Match($_.Name, '_(\d+)\.png').Groups[1].Value) } -Descending |
               Select-Object -First 1
    if ($null -eq $largest) {
        Write-Warning "No source PNG to resample for size $size; skipping."
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($largest.FullName)
    $stream = New-Object System.IO.MemoryStream(,$bytes)
    $source = [System.Drawing.Image]::FromStream($stream)
    $resized = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($resized)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $source.Dispose()
    $stream.Dispose()

    $tmp = Join-Path $env:TEMP "BodyExporter_resample_$size.png"
    $resized.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()
    $pngPaths.Add($tmp) | Out-Null
    $sizes.Add($size)   | Out-Null
}

if ($pngPaths.Count -eq 0) {
    throw "No PNGs collected. Aborting."
}

# Build the ICO. Format reference:
#   ICONDIR  (6 bytes)
#     Reserved        : 2 bytes (=0)
#     Type            : 2 bytes (=1 for icon)
#     Count           : 2 bytes (number of images)
#   ICONDIRENTRY[Count] (16 bytes each)
#     Width           : 1 byte  (0 means 256)
#     Height          : 1 byte  (0 means 256)
#     ColorCount      : 1 byte  (0 for >=256 colors)
#     Reserved        : 1 byte  (=0)
#     Planes          : 2 bytes (color planes, =1)
#     BitCount        : 2 bytes (bits per pixel, =32 for ARGB)
#     BytesInRes      : 4 bytes (image data size)
#     ImageOffset     : 4 bytes (offset from start of .ico file)
#   <image data>      (raw PNG bytes for each entry; Vista+ supports embedded PNG)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([UInt16]0)              # Reserved
$bw.Write([UInt16]1)              # Type=icon
$bw.Write([UInt16]$pngPaths.Count) # image count

$headerLength = 6 + 16 * $pngPaths.Count
$dataOffset = $headerLength
$pngDataList = New-Object System.Collections.Generic.List[byte[]]

for ($i = 0; $i -lt $pngPaths.Count; $i++) {
    $data = [System.IO.File]::ReadAllBytes($pngPaths[$i])
    $pngDataList.Add($data) | Out-Null

    $size = $sizes[$i]
    $w = if ($size -ge 256) { 0 } else { $size }
    $h = if ($size -ge 256) { 0 } else { $size }

    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)          # ColorCount (0 for >=256 colors)
    $bw.Write([byte]0)          # Reserved
    $bw.Write([UInt16]1)        # Planes
    $bw.Write([UInt16]32)       # BitCount
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$dataOffset)

    $dataOffset += $data.Length
}

foreach ($data in $pngDataList) {
    $bw.Write($data)
}

$bw.Flush()
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
[System.IO.File]::WriteAllBytes($OutputPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host "Wrote $OutputPath" -ForegroundColor Green
Write-Host "  Sizes embedded: $($sizes -join ', ')"
Write-Host "  Total bytes:   $((Get-Item $OutputPath).Length)"
