# make-icon.ps1 - draws the Odyssey Mouse icon and writes a multi-size .ico
# (16/32/48 as 32bpp BMP entries, 256 as PNG). No external tools required.
param(
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 64.0

    $r = 14.0 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, [single]($r * 2), [single]($r * 2), 180, 90)
    $path.AddArc([single]($s - $r * 2), 0, [single]($r * 2), [single]($r * 2), 270, 90)
    $path.AddArc([single]($s - $r * 2), [single]($s - $r * 2), [single]($r * 2), [single]($r * 2), 0, 90)
    $path.AddArc(0, [single]($s - $r * 2), [single]($r * 2), [single]($r * 2), 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $c1 = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
    $c2 = [System.Drawing.Color]::FromArgb(255, 124, 58, 237)
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, [single]45)
    $g.FillPath($br, $path)
    $br.Dispose()
    $path.Dispose()

    $pts = @(
        @(22.0, 14.0), @(22.0, 48.0), @(30.5, 39.5), @(36.5, 52.0),
        @(42.0, 49.5), @(36.0, 37.5), @(46.0, 36.5)
    )
    $ptA = New-Object 'System.Drawing.PointF[]' $pts.Count
    for ($i = 0; $i -lt $pts.Count; $i++) {
        $ptA[$i] = New-Object System.Drawing.PointF([single]($pts[$i][0] * $k), [single]($pts[$i][1] * $k))
    }
    $g.FillPolygon([System.Drawing.Brushes]::White, $ptA)
    $g.Dispose()
    return $bmp
}

# returns byte[] (as a single object, not unrolled)
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $xor = New-Object byte[] ($w * $h * 4)
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $h - 1 - $y)
            $o = ($y * $w + $x) * 4
            $xor[$o] = $c.B; $xor[$o + 1] = $c.G; $xor[$o + 2] = $c.R; $xor[$o + 3] = $c.A
        }
    }
    $maskRow = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $mask = New-Object byte[] ($maskRow * $h)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$w)
    $bw.Write([Int32]($h * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($xor.Length + $mask.Length))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    $bw.Write($xor)
    $bw.Write($mask)
    $bw.Flush()
    $out = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return , ([byte[]]$out)
}

$sizes = @(16, 32, 48, 256)
$parts = New-Object 'System.Collections.Generic.List[byte[]]'
$bitmaps = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'

foreach ($s in $sizes) {
    $b = New-IconBitmap $s
    $bitmaps.Add($b)
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $parts.Add([byte[]]$ms.ToArray())
        $ms.Dispose()
    }
    else {
        $parts.Add([byte[]](Get-DibBytes $b))
    }
}

# ---- assemble the file deterministically ----
$dirSize = 6 + 16 * $sizes.Count
$total = $dirSize
foreach ($p in $parts) { $total += $p.Length }
$all = New-Object byte[] $total

$hs = New-Object System.IO.MemoryStream
$hb = New-Object System.IO.BinaryWriter($hs)
$hb.Write([UInt16]0)
$hb.Write([UInt16]1)
$hb.Write([UInt16]$sizes.Count)
$offset = $dirSize
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $hb.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $hb.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $hb.Write([Byte]0)
    $hb.Write([Byte]0)
    $hb.Write([UInt16]1)
    $hb.Write([UInt16]32)
    $hb.Write([UInt32]$parts[$i].Length)
    $hb.Write([UInt32]$offset)
    $offset += $parts[$i].Length
}
$hb.Flush()
$head = $hs.ToArray()
$hb.Dispose(); $hs.Dispose()

[Array]::Copy($head, 0, $all, 0, $head.Length)
$at = $dirSize
foreach ($p in $parts) {
    [Array]::Copy($p, 0, $all, $at, $p.Length)
    $at += $p.Length
}
[System.IO.File]::WriteAllBytes($Out, $all)

foreach ($b in $bitmaps) { $b.Dispose() }
Write-Host ("icon written: {0} ({1:N0} bytes, {2} images)" -f $Out, $all.Length, $sizes.Count)
