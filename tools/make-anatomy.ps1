# Draws docs\anatomy.png: the weather header and today's row at 2x, with labelled callouts.
param([string]$Source, [string]$Out)
Add-Type -AssemblyName System.Drawing
$src = New-Object Drawing.Bitmap $Source
$cropY = 220; $cropH = 71; $scale = 2; $ox = 25; $oy = 118
$W = 960; $H = 430
$bmp = New-Object Drawing.Bitmap $W, $H
$g = [Drawing.Graphics]::FromImage($bmp)
$g.Clear([Drawing.Color]::FromArgb(23, 23, 26))
$g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
$g.DrawImage($src, (New-Object Drawing.Rectangle $ox, $oy, ($src.Width * $scale), ($cropH * $scale)), (New-Object Drawing.Rectangle 0, $cropY, $src.Width, $cropH), [Drawing.GraphicsUnit]::Pixel)
$g.PixelOffsetMode = 'Default'; $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'ClearTypeGridFit'

$font = New-Object Drawing.Font 'Segoe UI', 13, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$bold = New-Object Drawing.Font 'Segoe UI Semibold', 13, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
$text = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(225, 226, 232))
$dim = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(150, 152, 164))
$pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(120, 124, 140)), 1
$dot = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(200, 204, 220))
$fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'

# x, y are positions in the original 1x screenshot (y relative to the crop); side: top or bottom label
function Callout($x, $y, $side, $labelX, $labelY, $width, $title, $body) {
    $tx = $ox + $x * $scale; $ty = $oy + $y * $scale
    $rect = New-Object Drawing.RectangleF ($labelX - $width / 2), $labelY, $width, 80
    $titleH = [int]$g.MeasureString($title, $bold, $width, $fmt).Height
    $bodyH = if ($body) { [int]$g.MeasureString($body, $font, $width, $fmt).Height } else { 0 }
    if ($side -eq 'top') { $g.DrawLine($pen, $labelX, $labelY + $titleH + $bodyH + 3, $tx, $ty - 12) }
    else { $g.DrawLine($pen, $labelX, $labelY - 3, $tx, $ty + 14) }
    $g.FillEllipse($dot, $tx - 2, $(if ($side -eq 'top') { $ty - 14 } else { $ty + 12 }), 4, 4)
    $g.DrawString($title, $bold, $text, $rect, $fmt)
    if ($body) { $g.DrawString($body, $font, $dim, (New-Object Drawing.RectangleF ($labelX - $width / 2), ($labelY + $titleH), $width, 80), $fmt) }
}

# header items, labelled above
Callout 28 17 'top' 80 28 150 'Now' 'Current temperature'
Callout 112 17 'top' 290 28 200 'Place' 'Set by typing a city name'
Callout 433 17 'top' 800 12 290 'Rain scale, shown once' 'The amount that fills the top half of every chart. One value for the whole week, so rows compare directly.'

# today's row, labelled below
Callout 28 52 'bottom' 60 290 110 'Low' $null
Callout 98 52 'bottom' 205 330 250 'Low-to-high bar' "Placed on the week's coldest-to-warmest range and coloured by real temperature. White notch: now."
Callout 150 52 'bottom' 345 290 90 'High' $null
Callout 240 52 'bottom' 480 290 150 'Chance of rain' 'for the day'
Callout 335 52 'bottom' 690 330 300 '24 hours, midnight to midnight' 'Chance of rain grows up from the bottom, amount falls from the top. Each stops at the middle, so they never overlap.'
Callout 374 52 'bottom' 890 290 120 'This hour' 'Earlier hours dim'

$bmp.Save($Out); $g.Dispose(); $src.Dispose()
"saved $Out"
