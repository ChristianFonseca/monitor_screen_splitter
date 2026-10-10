<#
.SYNOPSIS
    Generates src/VscSplitter.App/Assets/app.ico (multi-size: 16-48 as 32-bit DIB, 64-256 as PNG).
#>
param(
    [string]$OutFile = (Join-Path $PSScriptRoot "..\src\VscSplitter.App\Assets\app.ico"),
    [string]$PreviewPng
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.5, $radius * 2)
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $bg = New-RoundedPath $rect ([float]($size * 0.22))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
        [System.Drawing.Color]::FromArgb(255, 124, 58, 237),
        45.0)
    $g.FillPath($brush, $bg)

    if ($size -le 24) { $cols = 2; $rows = 2 } else { $cols = 3; $rows = 2 }
    $pad = [Math]::Max(2.5, $size * 0.17)
    $gap = [Math]::Max(1.0, $size * 0.05)
    $cw = ($size - 2 * $pad - ($cols - 1) * $gap) / $cols
    $ch = ($size - 2 * $pad - ($rows - 1) * $gap) / $rows
    $radius = [float][Math]::Max(0.6, $size * 0.035)
    for ($r = 0; $r -lt $rows; $r++) {
        for ($c = 0; $c -lt $cols; $c++) {
            $alpha = if ($r -eq 0 -and $c -eq 0) { 255 } else { 215 }
            $cell = New-Object System.Drawing.RectangleF(([float]($pad + $c * ($cw + $gap))), ([float]($pad + $r * ($ch + $gap))), ([float]$cw), ([float]$ch))
            $cellBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255))
            $g.FillPath($cellBrush, (New-RoundedPath $cell $radius))
            $cellBrush.Dispose()
        }
    }

    $brush.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $lock = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $lock.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($lock.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($lock)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (height doubled: XOR bitmap + AND mask)
    $bw.Write([int]40); $bw.Write([int]$size); $bw.Write([int]($size * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($size * $size * 4 + $maskStride * $size))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $stride, $size * 4) }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($pixels[$y * $stride + $x * 4 + 3] -eq 0) {
                $i = [int][Math]::Floor($x / 8)
                $row[$i] = [byte]($row[$i] -bor (0x80 -shr ($x % 8)))
            }
        }
        $bw.Write($row)
    }
    $bw.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -le 48) {
        $bytes = Get-DibBytes $bmp
    }
    else {
        $png = New-Object System.IO.MemoryStream
        $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $png.ToArray()
        if ($s -eq 256 -and $PreviewPng) { [System.IO.File]::WriteAllBytes($PreviewPng, $bytes) }
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = [byte[]]$bytes }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Bytes.Length); $w.Write([int]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Flush()

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Output "Icon written: $OutFile ($($out.Length) bytes)"
