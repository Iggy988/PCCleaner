<#
.SYNOPSIS
    Generates the Ultimate PC Cleaner app assets from the master logo (Assets\ultimate_cleaner.png).
.DESCRIPTION
    Writes, using System.Drawing (Windows PowerShell 5.1 or PowerShell 7 on Windows):
      Assets\app.ico            multi-size icon (16-256 px), rounded dark tile
      Assets\app.png            256 px square version of the icon
      Assets\logo.png           768x512 downscaled logo for the dashboard (keeps the exe small)
      installer\wizard-*.bmp    Inno Setup wizard images (100/125/150 % DPI)
    The logo is 3:2 on black, so the square icon is a crop around the artwork's bounding box
    (padded with the logo's own black) rather than a stretch — nothing is distorted.
    Sizes below 256px are stored as classic 32-bit DIBs, the 256px frame as PNG, which every Windows tool accepts.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\PCCleaner\Assets\ultimate_cleaner.png'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\PCCleaner\Assets'),
    [string]$InstallerDir = (Join-Path $PSScriptRoot '..\installer')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Source = (Resolve-Path $Source).Path
New-Item -ItemType Directory -Force -Path $OutDir, $InstallerDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$InstallerDir = (Resolve-Path $InstallerDir).Path

$logo = [System.Drawing.Image]::FromFile($Source)
$background = [System.Drawing.Color]::FromArgb(255, 5, 8, 16)

# Square region around the artwork (monitor + broom + swoosh), in source pixels scaled to the actual image size.
$sx = $logo.Width / 1536.0; $sy = $logo.Height / 1024.0
$side = 1190 * $sx
$cropRect = [System.Drawing.RectangleF]::new(200 * $sx, 505 * $sy - $side / 2, $side, $side)

function New-Graphics([System.Drawing.Bitmap]$Bmp) {
    $g = [System.Drawing.Graphics]::FromImage($Bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    return $g
}

function New-RoundedRect([System.Drawing.RectangleF]$Rect, [float]$Radius) {
    $d = $Radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $p.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $p.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size
    $pad = if ($Size -ge 48) { $s * 0.03 } else { 0 }
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, $s - 2 * $pad, $s - 2 * $pad)
    $tile = New-RoundedRect $rect ($rect.Width * 0.2)

    $brush = New-Object System.Drawing.SolidBrush $background
    $g.FillPath($brush, $tile)
    $g.SetClip($tile)
    $g.DrawImage($logo, $rect, $cropRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.ResetClip()

    $brush.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
}

function Save-Resized([int]$Width, [int]$Height, [string]$Path, [System.Drawing.Imaging.ImageFormat]$Format) {
    # Fit the whole logo (uniform scale) and fill the rest with the logo's black.
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = New-Graphics $bmp
    $g.Clear($background)
    $scale = [Math]::Min($Width / $logo.Width, $Height / $logo.Height)
    $w = $logo.Width * $scale; $h = $logo.Height * $scale
    $g.DrawImage($logo, [System.Drawing.RectangleF]::new(($Width - $w) / 2, ($Height - $h) / 2, $w, $h))
    $g.Dispose()
    $bmp.Save($Path, $Format)
    $bmp.Dispose()
}

function Save-Cropped([int]$Width, [int]$Height, [string]$Path) {
    # Tall wizard image: fill the height with the square artwork crop, centered, black around it.
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = New-Graphics $bmp
    $g.Clear($background)
    $side = [Math]::Min($Width, $Height) * 0.96
    $g.DrawImage($logo, [System.Drawing.RectangleF]::new(($Width - $side) / 2, ($Height - $side) / 2, $side, $side), $cropRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
}

function ConvertTo-Dib([System.Drawing.Bitmap]$Bmp) {
    $w = $Bmp.Width; $h = $Bmp.Height
    $data = $Bmp.LockBits([System.Drawing.Rectangle]::new(0, 0, $w, $h),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $Bmp.UnlockBits($data)

    $maskStride = [int][math]::Ceiling($w / 32.0) * 4
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms

    # BITMAPINFOHEADER (height is doubled: XOR bitmap + AND mask)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * 4 * $h + $maskStride * $h))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    # XOR bitmap: bottom-up BGRA rows (GDI+ ARGB memory layout is already B,G,R,A)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $w * 4) }

    # AND mask: 1 = transparent pixel (for legacy renderers)
    $mask = New-Object byte[] ($maskStride * $h)
    for ($y = 0; $y -lt $h; $y++) {
        $srcRow = $h - 1 - $y
        for ($x = 0; $x -lt $w; $x++) {
            if ($pixels[$srcRow * $stride + $x * 4 + 3] -lt 128) {
                $mask[$y * $maskStride + [math]::Floor($x / 8)] = $mask[$y * $maskStride + [math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8))
            }
        }
    }
    $bw.Write($mask)
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $bmp.Save((Join-Path $OutDir 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        $bytes = ConvertTo-Dib $bmp
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = [byte[]]$bytes }
}

$icoPath = Join-Path $OutDir 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$f.Bytes.Length); $bw.Write([int]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }
$bw.Dispose(); $fs.Dispose()

Save-Resized 768 512 (Join-Path $OutDir 'logo.png') ([System.Drawing.Imaging.ImageFormat]::Png)

# Inno Setup wizard images at 100 / 125 / 150 % DPI.
foreach ($size in @(@(164, 314), @(192, 386), @(246, 459))) {
    Save-Cropped $size[0] $size[1] (Join-Path $InstallerDir "wizard-$($size[0]).bmp")
}
foreach ($size in @(@(55, 55), @(64, 68), @(83, 80))) {
    Save-Cropped $size[0] $size[1] (Join-Path $InstallerDir "wizard-small-$($size[0]).bmp")
}

$logo.Dispose()
Write-Host "Assets written to $OutDir and $InstallerDir"
