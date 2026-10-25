<#
.SYNOPSIS
    Generates the PC Cleaner app icon (Assets\app.ico with multiple sizes + Assets\app.png).
.DESCRIPTION
    Draws a rounded gradient tile with "sparkle" stars using System.Drawing (Windows PowerShell 5.1 or PowerShell 7 on Windows).
    Sizes below 256px are stored as classic 32-bit DIBs, the 256px frame as PNG, which every Windows tool accepts.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\PCCleaner\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Add-Sparkle([System.Drawing.Drawing2D.GraphicsPath]$Path, [float]$Cx, [float]$Cy, [float]$R) {
    $k = $R * 0.16
    $top    = [System.Drawing.PointF]::new($Cx, $Cy - $R)
    $right  = [System.Drawing.PointF]::new($Cx + $R, $Cy)
    $bottom = [System.Drawing.PointF]::new($Cx, $Cy + $R)
    $left   = [System.Drawing.PointF]::new($Cx - $R, $Cy)
    $q1 = [System.Drawing.PointF]::new($Cx + $k, $Cy - $k)
    $q2 = [System.Drawing.PointF]::new($Cx + $k, $Cy + $k)
    $q3 = [System.Drawing.PointF]::new($Cx - $k, $Cy + $k)
    $q4 = [System.Drawing.PointF]::new($Cx - $k, $Cy - $k)

    $Path.StartFigure()
    $Path.AddBezier($top, $q1, $q1, $right)
    $Path.AddBezier($right, $q2, $q2, $bottom)
    $Path.AddBezier($bottom, $q3, $q3, $left)
    $Path.AddBezier($left, $q4, $q4, $top)
    $Path.CloseFigure()
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
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$Size
    $pad = if ($Size -ge 48) { $s * 0.04 } else { 0 }
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, $s - 2 * $pad, $s - 2 * $pad)
    $tile = New-RoundedRect $rect ($rect.Width * 0.24)

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 99, 102, 241),   # indigo
        [System.Drawing.Color]::FromArgb(255, 6, 182, 212),    # cyan
        [single]45)
    $g.FillPath($gradient, $tile)

    # Soft glossy highlight across the top half.
    $g.SetClip($tile)
    $shine = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shine.AddEllipse($rect.X - $rect.Width * 0.25, $rect.Y - $rect.Height * 0.65, $rect.Width * 1.5, $rect.Height * 1.05)
    $shineBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(34, 255, 255, 255))
    $g.FillPath($shineBrush, $shine)
    $g.ResetClip()

    $stars = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Sparkle $stars ($s * 0.43) ($s * 0.56) ($s * 0.31)
    Add-Sparkle $stars ($s * 0.73) ($s * 0.27) ($s * 0.13)
    if ($Size -ge 32) { Add-Sparkle $stars ($s * 0.75) ($s * 0.76) ($s * 0.08) }
    $g.FillPath([System.Drawing.Brushes]::White, $stars)

    $gradient.Dispose(); $shineBrush.Dispose(); $shine.Dispose(); $stars.Dispose(); $tile.Dispose(); $g.Dispose()
    return $bmp
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

Write-Host "Icon written to $icoPath"
