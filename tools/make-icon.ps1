# Renders the app icon (Assets/app.ico, gauge-64.png, gauge-160.png) or a preview of the three candidate icons.
# Usage: powershell -STA -File tools/make-icon.ps1 -OutDir src/ClaudeUsageMeter/Assets -Only 1
#        powershell -STA -File tools/make-icon.ps1 -OutDir <folder>   (writes icon-options.png)
param([string]$OutDir, [string]$Only = "")
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'
$M = [System.Windows.Media.Colors]

function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function B([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush((C $hex)); $b.Freeze(); $b }
function P($brush, [double]$w, [string]$cap = 'Round') { $p = New-Object System.Windows.Media.Pen($brush, $w); $p.StartLineCap = $cap; $p.EndLineCap = $cap; $p.Freeze(); $p }
function Pt([double]$cx, [double]$cy, [double]$deg, [double]$r) { $a = $deg * [Math]::PI / 180; New-Object System.Windows.Point(($cx + $r * [Math]::Cos($a)), ($cy - $r * [Math]::Sin($a))) }
function Arc([double]$cx, [double]$cy, [double]$from, [double]$to, [double]$r) {
    $g = New-Object System.Windows.Media.StreamGeometry; $c = $g.Open()
    $c.BeginFigure((Pt $cx $cy $from $r), $false, $false)
    $c.ArcTo((Pt $cx $cy $to $r), (New-Object System.Windows.Size($r, $r)), 0, ([Math]::Abs($from - $to) -gt 180), [System.Windows.Media.SweepDirection]::Clockwise, $true, $true)
    $c.Close(); $g.Freeze(); $g
}
function Wedge([double]$cx, [double]$cy, [double]$from, [double]$to, [double]$r) {
    $g = New-Object System.Windows.Media.StreamGeometry; $c = $g.Open()
    $c.BeginFigure((New-Object System.Windows.Point($cx, $cy)), $true, $true)
    $c.LineTo((Pt $cx $cy $from $r), $true, $true)
    $c.ArcTo((Pt $cx $cy $to $r), (New-Object System.Windows.Size($r, $r)), 0, ([Math]::Abs($from - $to) -gt 180), [System.Windows.Media.SweepDirection]::Clockwise, $true, $true)
    $c.Close(); $g.Freeze(); $g
}
function Tile($dc, [string]$top, [string]$bottom) {
    $grad = New-Object System.Windows.Media.LinearGradientBrush((C $top), (C $bottom), 90.0); $grad.Freeze()
    $dc.DrawRoundedRectangle($grad, $null, (New-Object System.Windows.Rect(12, 12, 232, 232)), 54, 54)
}

# Option 1: progress ring. A thick donut, about two thirds filled, with a flat start cap.
function Draw1($dc) {
    Tile $dc '#FF223047' '#FF121925'
    $dc.DrawEllipse($null, (P (B '#33FFFFFF') 40), (New-Object System.Windows.Point(128, 128)), 72, 72)
    $dc.DrawGeometry($null, (P (B '#FF3FB8AF') 40), (Arc 128 128 90 -150 72))
    $dc.DrawEllipse((B '#FFF4F6FB'), $null, (New-Object System.Windows.Point(128, 128)), 18, 18)
}

# Option 2: level bars. Three rising bars, coloured calm, amber, red, the last one only part full.
function Draw2($dc) {
    Tile $dc '#FF263041' '#FF151A24'
    $w = 46; $gap = 20; $x0 = 128 - (3 * $w + 2 * $gap) / 2; $base = 204
    $hs = 70, 112, 154; $cols = '#FF4C9BE8', '#FFF7B12E', '#FFFF5F66'
    for ($i = 0; $i -lt 3; $i++) {
        $x = $x0 + $i * ($w + $gap); $h = $hs[$i]
        $dc.DrawRoundedRectangle((B '#2EFFFFFF'), $null, (New-Object System.Windows.Rect($x, ($base - $h), $w, $h)), 12, 12)
        $fill = if ($i -eq 2) { $h * 0.45 } else { $h }
        $dc.DrawRoundedRectangle((B $cols[$i]), $null, (New-Object System.Windows.Rect($x, ($base - $fill), $w, $fill)), 12, 12)
    }
}

# Option 3: dial clock. A light face with a usage wedge and one hand, for "used so far, resets later".
function Draw3($dc) {
    Tile $dc '#FFFDFDFE' '#FFE3E8F0'
    $dc.DrawEllipse((B '#FFFFFFFF'), (P (B '#FF1B2433') 14), (New-Object System.Windows.Point(128, 128)), 86, 86)
    $dc.DrawGeometry((B '#FF3A7BD5'), $null, (Wedge 128 128 90 -40 70))
    $dc.DrawLine((P (B '#FF1B2433') 14), (New-Object System.Windows.Point(128, 128)), (Pt 128 128 -40 74))
    $dc.DrawEllipse((B '#FF1B2433'), $null, (New-Object System.Windows.Point(128, 128)), 14, 14)
}

function RenderBmp([int]$n, [int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual; $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))
    & "Draw$n" $dc
    $dc.Pop(); $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv); $bmp.Freeze(); $bmp
}
function SavePng($bmp, [string]$path) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Close()
}
function PngBytes($bmp) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}

if ($Only) {
    # Export one option as the app's assets.
    $n = [int]$Only
    SavePng (RenderBmp $n 64) (Join-Path $OutDir 'gauge-64.png')
    SavePng (RenderBmp $n 160) (Join-Path $OutDir 'gauge-160.png')
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $imgs = foreach ($z in $sizes) { , (PngBytes (RenderBmp $n $z)) }
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ms)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $off = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $z = $sizes[$i]; $d = $imgs[$i]; $b = [byte]($(if ($z -ge 256) { 0 } else { $z }))
        $bw.Write($b); $bw.Write($b); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$off); $off += $d.Length
    }
    foreach ($d in $imgs) { $bw.Write($d) }; $bw.Flush()
    [IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $ms.ToArray())
    "exported option $n"; return
}

# Preview sheet: three columns, each with the big icon and real-size taskbar samples (dark and light).
$W = 1080; $H = 620; $colW = 360
$dv = New-Object System.Windows.Media.DrawingVisual; $dc = $dv.RenderOpen()
$dc.DrawRectangle((B '#FF1E1F22'), $null, (New-Object System.Windows.Rect(0, 0, $W, $H)))
$face = New-Object System.Windows.Media.Typeface('Segoe UI Variable Display')
$faceB = New-Object System.Windows.Media.Typeface((New-Object System.Windows.Media.FontFamily('Segoe UI Variable Display')), 'Normal', 'SemiBold', 'Normal')
function Text($s, [double]$x, [double]$y, [double]$size, $brush, $tf, [switch]$Center) {
    $ft = New-Object System.Windows.Media.FormattedText($s, [Globalization.CultureInfo]::InvariantCulture, 'LeftToRight', $tf, $size, $brush, 1.0)
    if ($Center) { $x -= $ft.Width / 2 }
    $dc.DrawText($ft, (New-Object System.Windows.Point($x, $y)))
}
Text 'Pick an icon for Claude Usage Meter' 40 26 26 (B '#FFF4F6FB') $faceB
Text 'Large, then at real tray size (16, 20, 24 and 32 px) on a dark and a light taskbar.' 40 64 15 (B '#B3F4F6FB') $face
$names = 'Option 1: Ring', 'Option 2: Level bars', 'Option 3: Dial clock'
for ($n = 1; $n -le 3; $n++) {
    $cx = ($n - 1) * $colW + $colW / 2
    Text $names[$n - 1] $cx 112 19 (B '#FFF4F6FB') $faceB -Center
    $dc.DrawImage((RenderBmp $n 200), (New-Object System.Windows.Rect(($cx - 100), 150, 200, 200)))
    foreach ($row in @(@{ Y = 390; Bg = '#FF1C1C1C' }, @{ Y = 470; Bg = '#FFEEEEEE' })) {
        $dc.DrawRoundedRectangle((B $row.Bg), $null, (New-Object System.Windows.Rect(($cx - 130), $row.Y, 260, 56)), 8, 8)
        $x = $cx - 110
        foreach ($z in 16, 20, 24, 32) {
            $dc.DrawImage((RenderBmp $n $z), (New-Object System.Windows.Rect($x, ($row.Y + 28 - $z / 2), $z, $z)))
            $x += $z + 34
        }
    }
}
Text 'All three are original shapes, not based on any company logo. Option 1 is the one in use.' 40 570 15 (B '#B3F4F6FB') $face
$dc.Close()
$bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($W, $H, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$bmp.Render($dv)
SavePng $bmp (Join-Path $OutDir 'icon-options.png')
"preview saved"
