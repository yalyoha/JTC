# Generates src/TClient/Assets/tclient.ico with "TC" letters over an indigo background,
# packing four resolutions (16, 32, 48, 256) into a single ICO container.
# Run: pwsh -File tools/gen-icon.ps1

Add-Type -AssemblyName System.Drawing

$OutPath = Join-Path $PSScriptRoot "..\src\TClient\Assets\tclient.ico"
$OutPath = [System.IO.Path]::GetFullPath($OutPath)
$Sizes   = @(16, 32, 48, 256)
$BgColor = [System.Drawing.Color]::FromArgb(255, 91, 95, 199)   # #5B5FC7 indigo
$Fg      = [System.Drawing.Brushes]::White

function New-TcBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Rounded-corner background (except at 16px where corners disappear anyway)
    $radius = [int]($size * 0.18)
    $rect   = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    if ($radius -ge 2) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2
        $path.AddArc($rect.X,                     $rect.Y,                      $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d,            $rect.Y,                      $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d,            $rect.Bottom - $d,            $d, $d,   0, 90)
        $path.AddArc($rect.X,                     $rect.Bottom - $d,            $d, $d,  90, 90)
        $path.CloseFigure()
        $brush = New-Object System.Drawing.SolidBrush $BgColor
        $g.FillPath($brush, $path)
        $brush.Dispose()
        $path.Dispose()
    } else {
        $brush = New-Object System.Drawing.SolidBrush $BgColor
        $g.FillRectangle($brush, $rect)
        $brush.Dispose()
    }

    # "TC" text — smaller factor because two glyphs need to fit width-wise
    $fontSize = [Math]::Max(6.0, [double]$size * 0.44)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment     = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $g.DrawString("TC", $font, $Fg, $textRect, $format)

    $font.Dispose()
    $format.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Build all bitmaps and PNGs first (need lengths to compute directory offsets)
$entries = @()
foreach ($size in $Sizes) {
    $bmp = New-TcBitmap $size
    $png = Get-PngBytes $bmp
    $bmp.Dispose()
    $entries += [pscustomobject]@{ Size = $size; Png = $png }
}

# ICO layout: 6-byte ICONDIR + (16-byte ICONDIRENTRY * N) + image data blobs
$dirSize  = 6 + (16 * $entries.Count)
$out      = New-Object System.IO.FileStream $OutPath, ([System.IO.FileMode]::Create)
$w        = New-Object System.IO.BinaryWriter $out

# ICONDIR
$w.Write([uint16]0)              # Reserved
$w.Write([uint16]1)              # Type: 1 = icon
$w.Write([uint16]$entries.Count) # Count

$offset = $dirSize
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { [byte]0 } else { [byte]$e.Size }  # 0 means 256
    $w.Write($dim)                       # Width
    $w.Write($dim)                       # Height
    $w.Write([byte]0)                    # ColorCount (0 for >=256 colors)
    $w.Write([byte]0)                    # Reserved
    $w.Write([uint16]1)                  # ColorPlanes
    $w.Write([uint16]32)                 # BitCount
    $w.Write([uint32]$e.Png.Length)      # Image data size
    $w.Write([uint32]$offset)            # Image data offset
    $offset += $e.Png.Length
}

foreach ($e in $entries) {
    $w.Write($e.Png)
}

$w.Close()
$out.Close()

Write-Host "Wrote $OutPath ($((Get-Item $OutPath).Length) bytes)"
