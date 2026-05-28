# windows/build/icon/make-icon.ps1
# Port of Tools/makeicon.swift to PowerShell + WPF.
# Renders the Token Spendie app icon — dark rounded tile with a faint
# ring track and a 62% orange progress arc — at multiple resolutions, then
# bundles all sizes into a single AppIcon.ico plus a standalone AppIcon-256.png.
#
# Run once locally on Windows after this file changes. Outputs are committed
# alongside the script.
#
#   pwsh windows/build/icon/make-icon.ps1
#
# Reproducible: deterministic output. No network, no external tools.

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$icoPath = Join-Path $here 'AppIcon.ico'
$png256Path = Join-Path $here 'AppIcon-256.png'

# Design constants (from Tools/makeicon.swift).
# All values are expressed as fractions of canvas size so each rendered size
# stays visually proportional.
$bgR = 0.12; $bgG = 0.12; $bgB = 0.16
$arcR = 0.85; $arcG = 0.47; $arcB = 0.34
$cornerFrac = 180.0 / 1024.0   # 0.176
$radiusFrac = 280.0 / 1024.0   # 0.273
$strokeFrac = 110.0 / 1024.0   # 0.107
$progressFraction = 0.62

function Render-IconPng([int]$px) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    $size = [double]$px
    $cornerRadius = $size * $cornerFrac
    $center = New-Object System.Windows.Point ($size / 2.0), ($size / 2.0)
    $radius = $size * $radiusFrac
    $stroke = $size * $strokeFrac

    # Background — dark rounded tile.
    $bgColor = [System.Windows.Media.Color]::FromRgb(
        [byte][int]($bgR * 255), [byte][int]($bgG * 255), [byte][int]($bgB * 255))
    $bgBrush = New-Object System.Windows.Media.SolidColorBrush $bgColor
    $bgBrush.Freeze()
    $rect = New-Object System.Windows.Rect 0, 0, $size, $size
    $dc.DrawRoundedRectangle($bgBrush, $null, $rect, $cornerRadius, $cornerRadius)

    # Faint ring track.
    $trackColor = [System.Windows.Media.Color]::FromArgb([byte]([int](0.16 * 255)), [byte]255, [byte]255, [byte]255)
    $trackBrush = New-Object System.Windows.Media.SolidColorBrush $trackColor
    $trackBrush.Freeze()
    $trackPen = New-Object System.Windows.Media.Pen $trackBrush, $stroke
    $dc.DrawEllipse($null, $trackPen, $center, $radius, $radius)

    # Progress arc.
    $arcColor = [System.Windows.Media.Color]::FromRgb(
        [byte][int]($arcR * 255), [byte][int]($arcG * 255), [byte][int]($arcB * 255))
    $arcBrush = New-Object System.Windows.Media.SolidColorBrush $arcColor
    $arcBrush.Freeze()
    $arcPen = New-Object System.Windows.Media.Pen $arcBrush, $stroke
    $arcPen.StartLineCap = 'Round'
    $arcPen.EndLineCap = 'Round'

    $startPoint = New-Object System.Windows.Point $center.X, ($center.Y - $radius)
    $endAngleDeg = -90.0 + 360.0 * $progressFraction
    $endRad = $endAngleDeg * [Math]::PI / 180.0
    $endPoint = New-Object System.Windows.Point ($center.X + $radius * [Math]::Cos($endRad)), ($center.Y + $radius * [Math]::Sin($endRad))

    $isLargeArc = $progressFraction -gt 0.5
    $arcSize = New-Object System.Windows.Size $radius, $radius
    $arcSeg = New-Object System.Windows.Media.ArcSegment $endPoint, $arcSize, 0.0, $isLargeArc, ([System.Windows.Media.SweepDirection]::Clockwise), $true
    $figure = New-Object System.Windows.Media.PathFigure
    $figure.StartPoint = $startPoint
    $figure.Segments.Add($arcSeg) | Out-Null
    $geometry = New-Object System.Windows.Media.PathGeometry
    $geometry.Figures.Add($figure) | Out-Null
    $dc.DrawGeometry($null, $arcPen, $geometry)

    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $px, $px, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $rtb.Freeze()

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create($rtb)
    $encoder.Frames.Add($frame) | Out-Null
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return $ms.ToArray()
}

function Write-MultiIcon([string]$path, [int[]]$sizes) {
    $pngs = @{}
    foreach ($s in $sizes) { $pngs[$s] = Render-IconPng $s }

    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms

    # ICONDIR.
    $w.Write([uint16]0)                  # reserved
    $w.Write([uint16]1)                  # type = icon
    $w.Write([uint16]$sizes.Count)       # image count

    # ICONDIRENTRY for each image.
    $headerBytes = 6 + ($sizes.Count * 16)
    $offset = $headerBytes
    foreach ($s in $sizes) {
        $bytes = $pngs[$s]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim)             # width (0 means 256)
        $w.Write([byte]$dim)             # height
        $w.Write([byte]0)                # palette count
        $w.Write([byte]0)                # reserved
        $w.Write([uint16]1)              # planes
        $w.Write([uint16]32)             # bits per pixel
        $w.Write([uint32]$bytes.Length)  # size in bytes
        $w.Write([uint32]$offset)        # offset
        $offset += $bytes.Length
    }

    foreach ($s in $sizes) {
        $bytes = [byte[]]$pngs[$s]
        $w.Write($bytes, 0, $bytes.Length)
    }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $ms.ToArray())
}

# 256 first so AppIcon-256.png matches the largest frame in the ICO.
[System.IO.File]::WriteAllBytes($png256Path, (Render-IconPng 256))
Write-Host "wrote $png256Path"

Write-MultiIcon -path $icoPath -sizes @(256, 128, 64, 48, 32, 16)
Write-Host "wrote $icoPath"
