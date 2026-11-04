# Vygeneruje ikonu aplikace (varianta B: barevné kolo s fajfkou) jako .ico s více velikostmi.
# Kresba je v souřadnicích 96×96 a škáluje se na každou velikost.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'src\MtgDraftHelper.App\Resources\app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($size / 96.0, $size / 96.0)

    # Podklad: zaoblený čtverec
    $r = 20.0
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc(96 - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc(96 - 2 * $r, 96 - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, 96 - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush (Color '#1B1F2B')), $path)

    # Pět barev
    $dots = @(
        @(48, 22, '#F3E9C6'), @(73, 40, '#4A90D9'), @(64, 70, '#8E7A99'),
        @(32, 70, '#E0573F'), @(23, 40, '#4FAE5A'))
    foreach ($d in $dots) {
        $g.FillEllipse((New-Object System.Drawing.SolidBrush (Color $d[2])), $d[0] - 9, $d[1] - 9, 18, 18)
    }

    # Fajfka
    $pen = New-Object System.Drawing.Pen (Color '#E0B050'), 6
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 38, 50),
        (New-Object System.Drawing.PointF 46, 58),
        (New-Object System.Drawing.PointF 60, 41)))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO: hlavička, adresář a obrázky jako PNG
$images = foreach ($s in $sizes) { , (Draw-Icon $s) }
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
[System.IO.File]::WriteAllBytes($target, $out.ToArray())

# Náhled 256 px pro kontrolu
[System.IO.File]::WriteAllBytes([System.IO.Path]::ChangeExtension($target, '.png'), $images[-1])
Write-Host "Ikona: $target"
