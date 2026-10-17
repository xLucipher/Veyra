<#
.SYNOPSIS
    Erzeugt alle Grafiken aus den Original-Logos in assets/branding.

.DESCRIPTION
    Quellen:  assets/branding/logo.png       (Symbol "V")
              assets/branding/logo_name.png  (Symbol + Schriftzug "Veyra")
    Ergebnis: src/Veyra/Assets/Veyra.ico           App-Icon (16-256 px, abgerundet)
              src/Veyra/Assets/logo.png            Logo fuer Fenster (256 px, abgerundet)
              assets/branding/logo_name_banner.png Banner fuer README
              installer/wizard-*.bmp               Bilder fuer den Installer-Assistenten
    Nur noetig, wenn sich die Original-Logos aendern.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\branding\logo.png'))
$logoName = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\branding\logo_name.png'))
# Hintergrundfarbe direkt aus dem Logo (Ecke), damit Uebergaenge nahtlos sind.
$background = ([System.Drawing.Bitmap]$logo).GetPixel(4, 4)

# Quadratischer Ausschnitt um das "V" (Originalbild 1254x1254).
$iconSource = New-Object System.Drawing.Rectangle 197, 200, 860, 860

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    return @($bmp, $g)
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Abgerundetes Symbol in beliebiger Groesse (transparente Ecken).
function New-RoundedIcon([int]$size) {
    $bmp, $g = New-Canvas $size $size
    $path = New-RoundedPath 0 0 $size $size ([Math]::Max(2, $size * 0.2))
    $g.SetClip($path)
    $g.DrawImage($logo, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), $iconSource, 'Pixel')
    $g.Dispose()
    return $bmp
}

# Zeichnet einen Bildausschnitt und laesst die Raender weich in die Hintergrundfarbe auslaufen.
function Draw-Feathered($g, $image, $dest, $src, [float]$feather) {
    $g.DrawImage($image, $dest, $src, 'Pixel')
    $solid = $background
    $clear = [System.Drawing.Color]::FromArgb(0, $background.R, $background.G, $background.B)
    $fx = [Math]::Max(1, $dest.Width * $feather); $fy = [Math]::Max(1, $dest.Height * $feather)
    $edges = @(
        @((New-Object System.Drawing.RectangleF $dest.X, $dest.Y, $fx, $dest.Height), 0),
        @((New-Object System.Drawing.RectangleF ($dest.Right - $fx), $dest.Y, $fx, $dest.Height), 180),
        @((New-Object System.Drawing.RectangleF $dest.X, $dest.Y, $dest.Width, $fy), 90),
        @((New-Object System.Drawing.RectangleF $dest.X, ($dest.Bottom - $fy), $dest.Width, $fy), 270)
    )
    foreach ($e in $edges) {
        $rect = $e[0]
        $grow = New-Object System.Drawing.RectangleF ($rect.X - 1), ($rect.Y - 1), ($rect.Width + 2), ($rect.Height + 2)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $grow, $solid, $clear, ([float]$e[1])
        $g.FillRectangle($brush, $rect)
        $brush.Dispose()
    }
}

function Save-Bmp24($bitmap, [string]$file) {
    $flat = New-Object System.Drawing.Bitmap $bitmap.Width, $bitmap.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($flat)
    $g.Clear($background)
    $g.DrawImage($bitmap, 0, 0, $bitmap.Width, $bitmap.Height)
    $g.Dispose()
    $flat.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $flat.Dispose()
}

$assets = Join-Path $root 'src\Veyra\Assets'
$installer = Join-Path $root 'installer'
New-Item -ItemType Directory -Force $assets, $installer | Out-Null

# --- App-Icon: ICO mit PNG-Eintraegen (Vista+) -------------------------------------------
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-RoundedIcon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Veyra.ico'), $ico.ToArray())

# --- Logo fuer Fenster --------------------------------------------------------------------
$win = New-RoundedIcon 256
$win.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$win.Dispose()

# --- Banner fuer README (Symbol + Schriftzug, breit) ---------------------------------------
$bannerSource = New-Object System.Drawing.Rectangle 100, 400, 1060, 400
$banner, $g = New-Canvas 1060 400
$g.DrawImage($logoName, (New-Object System.Drawing.Rectangle 0, 0, 1060, 400), $bannerSource, 'Pixel')
$g.Dispose()
$banner.Save((Join-Path $root 'assets\branding\logo_name_banner.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$banner.Dispose()

# --- Installer: grosses Seitenbild (164x314 und 2x) und kleines Bild (55x55 und 2x) ---------
$textSource = New-Object System.Drawing.Rectangle 520, 520, 600, 210   # Schriftzug "Veyra"
foreach ($scale in 1, 2) {
    $w0 = 164 * $scale; $h0 = 314 * $scale
    $side, $g = New-Canvas $w0 $h0
    $g.Clear($background)
    $iconSize = [int](120 * $scale)
    Draw-Feathered $g $logo (New-Object System.Drawing.Rectangle ([int](($w0 - $iconSize) / 2)), ([int](70 * $scale)), $iconSize, $iconSize) $iconSource 0.12
    $textW = [int](130 * $scale); $textH = [int]($textW * $textSource.Height / $textSource.Width)
    Draw-Feathered $g $logoName (New-Object System.Drawing.Rectangle ([int](($w0 - $textW) / 2)), ([int](205 * $scale)), $textW, $textH) $textSource 0.08
    $g.Dispose()
    Save-Bmp24 $side (Join-Path $installer ("wizard-large-{0}x.bmp" -f $scale))
    $side.Dispose()

    $small = New-RoundedIcon (55 * $scale)
    Save-Bmp24 $small (Join-Path $installer ("wizard-small-{0}x.bmp" -f $scale))
    $small.Dispose()
}

$logo.Dispose(); $logoName.Dispose()
Write-Host 'Branding erzeugt.' -ForegroundColor Green
