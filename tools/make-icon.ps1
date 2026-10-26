# Generates assets\app.ico (16..256 px, PNG-compressed entries) and assets\icon-preview.png.
# Icon: a database cylinder filled green -> yellow -> red, matching the heat colors in the list.
# Run from PowerShell 7 on Windows: .\tools\make-icon.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function Hex([string]$h) { [System.Drawing.ColorTranslator]::FromHtml($h) }

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pw = [Math]::Max(1.0, $s / 28.0)          # outline width
    $x = [single]($s * 0.14); $w = [single]($s * 0.72)
    $eh = [single]($s * 0.24)                   # ellipse height
    $top = [single]($s * 0.08 + $eh / 2)        # center line of the top cap
    $bottom = [single]($s * 0.92 - $eh / 2)     # center line of the bottom
    $outline = New-Object System.Drawing.Pen (Hex '#263238'), $pw

    # bands from bottom (green) to top (red); each is a rectangle plus its bottom ellipse
    $bands = @(
        @{ Color = '#43A047'; Dark = '#2E7D32' },
        @{ Color = '#FDD835'; Dark = '#F9A825' },
        @{ Color = '#E53935'; Dark = '#C62828' }
    )
    $bandH = ($bottom - $top) / $bands.Count
    for ($i = 0; $i -lt $bands.Count; $i++) {
        $yBottom = [single]($bottom - $i * $bandH)
        $yTop = [single]($yBottom - $bandH)
        $rect = New-Object System.Drawing.RectangleF $x, $yTop, $w, ($yBottom - $yTop)
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Hex $bands[$i].Color), (Hex $bands[$i].Dark), 0.0
        $blend = New-Object System.Drawing.Drawing2D.Blend 3
        $blend.Positions = [single[]]@(0, 0.45, 1); $blend.Factors = [single[]]@(0.15, 0, 1)
        $grad.Blend = $blend
        $g.FillEllipse($grad, $x, $yBottom - $eh / 2, $w, $eh)
        $g.FillRectangle($grad, $rect)
        if ($i -gt 0 -and $s -ge 24) {
            # seam between bands
            $seam = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 0, 0, 0)), ([Math]::Max(1.0, $pw * 0.7))
            $g.DrawArc($seam, $x, $yBottom - $eh / 2, $w, $eh, 0, 180)
            $seam.Dispose()
        }
        $grad.Dispose()
    }

    # top cap
    $capBrush = New-Object System.Drawing.SolidBrush (Hex '#FFCDD2')
    $g.FillEllipse($capBrush, $x, $top - $eh / 2, $w, $eh)
    $capBrush.Dispose()

    # outline: cap, sides, bottom arc
    $g.DrawEllipse($outline, $x, $top - $eh / 2, $w, $eh)
    $g.DrawLine($outline, $x, $top, $x, $bottom)
    $g.DrawLine($outline, $x + $w, $top, $x + $w, $bottom)
    $g.DrawArc($outline, $x, $bottom - $eh / 2, $w, $eh, 0, 180)

    # highlight on the left side
    if ($s -ge 32) {
        $hl = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
        $g.FillRectangle($hl, [single]($x + $w * 0.12), $top + $eh * 0.4, [single]($w * 0.08), $bottom - $top - $eh * 0.2)
        $hl.Dispose()
    }

    $outline.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container: header, directory, then PNG images
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $ico.ToArray())

# preview: all sizes side by side
$previewWidth = [int](($sizes | Measure-Object -Sum).Sum + 10 * ($sizes.Count + 1))
$preview = New-Object System.Drawing.Bitmap $previewWidth, 276
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::White)
$px = 10
foreach ($s in $sizes) {
    $b = Draw-Icon $s
    $pg.DrawImageUnscaled($b, $px, 266 - $s)
    $b.Dispose()
    $px += $s + 10
}
$pg.Dispose()
$preview.Save((Join-Path $assets 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

"Written: $(Join-Path $assets 'app.ico')"
