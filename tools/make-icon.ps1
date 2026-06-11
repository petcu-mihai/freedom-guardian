<#
.SYNOPSIS
  Generates assets\FreedomGuardian.ico (a padlock-on-shield motif) at multiple
  resolutions, using System.Drawing. Re-run only if you want to change the art;
  the committed .ico is what the build embeds.
#>
[CmdletBinding()]
param(
    [string]$OutPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
if ([string]::IsNullOrEmpty($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
$repo = Split-Path -Parent $root
if ([string]::IsNullOrEmpty($OutPath)) { $OutPath = Join-Path $repo 'assets\FreedomGuardian.ico' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutPath) | Out-Null

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-FramePng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Shield background with a blue -> indigo gradient.
    $shield = New-RoundedRect (40*$s) (24*$s) (176*$s) (208*$s) (44*$s)
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $c1 = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)   # blue
    $c2 = [System.Drawing.Color]::FromArgb(255, 67, 56, 202)   # indigo
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 60.0)
    $g.FillPath($brush, $shield)

    # Padlock (white).
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    # Body
    $body = New-RoundedRect (88*$s) (120*$s) (80*$s) (72*$s) (12*$s)
    $g.FillPath($white, $body)
    # Shackle (arc)
    $penW = [single](16 * $s)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penW
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, (98*$s), (78*$s), (60*$s), (72*$s), 180, 180)
    # Keyhole
    $hole = New-Object System.Drawing.SolidBrush ($c2)
    $g.FillEllipse($hole, (120*$s), (140*$s), (16*$s), (16*$s))
    $g.FillRectangle($hole, (125*$s), (150*$s), (6*$s), (22*$s))

    $pen.Dispose(); $brush.Dispose(); $white.Dispose(); $hole.Dispose(); $shield.Dispose(); $body.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($sz in $sizes) { $frames += , (New-FramePng $sz) }

$fs = [System.IO.File]::Create($OutPath)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR
    $bw.Write([uint16]0)           # reserved
    $bw.Write([uint16]1)           # type = icon
    $bw.Write([uint16]$sizes.Count)

    # ICONDIRENTRY[] (16 bytes each) precede all image data.
    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $data = $frames[$i]
        $dim = [byte]($(if ($sz -ge 256) { 0 } else { $sz }))
        $bw.Write([byte]$dim)      # width  (0 => 256)
        $bw.Write([byte]$dim)      # height (0 => 256)
        $bw.Write([byte]0)         # palette
        $bw.Write([byte]0)         # reserved
        $bw.Write([uint16]1)       # color planes
        $bw.Write([uint16]32)      # bits per pixel
        $bw.Write([uint32]$data.Length)
        $bw.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $frames) { $bw.Write($data) }
}
finally {
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

Write-Host "Wrote $OutPath ($($sizes.Count) sizes)." -ForegroundColor Green
