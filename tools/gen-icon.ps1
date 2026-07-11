# Renders icon/logo.svg into src/TClient/Assets/tclient.ico as a multi-resolution
# ICO (16, 32, 48, 256) using WPF's Geometry.Parse (which speaks the same path syntax
# as SVG) + a LinearGradientBrush that matches the SVG's gradient stops.
# Run: pwsh -File tools/gen-icon.ps1

Add-Type -AssemblyName PresentationCore, WindowsBase

$OutPath  = Join-Path $PSScriptRoot "..\src\TClient\Assets\tclient.ico"
$OutPath  = [System.IO.Path]::GetFullPath($OutPath)
$SvgPath  = Join-Path $PSScriptRoot "..\icon\logo.svg"
$SvgPath  = [System.IO.Path]::GetFullPath($SvgPath)
$Sizes    = @(16, 32, 48, 256)

if (-not (Test-Path $SvgPath)) { throw "SVG source not found: $SvgPath" }

# Parse the SVG to extract the single path's `d` attribute and the viewBox extent.
$svg = [xml](Get-Content $SvgPath -Raw)
$ns  = New-Object System.Xml.XmlNamespaceManager $svg.NameTable
$ns.AddNamespace('s', 'http://www.w3.org/2000/svg')
$pathNode = $svg.SelectSingleNode('//s:path', $ns)
if (-not $pathNode) { throw "SVG has no <path> element" }
$d = $pathNode.d

# ViewBox: "min-x min-y width height"
$viewBox = ($svg.svg.viewBox -split '\s+') | ForEach-Object { [double]$_ }
$svgW    = $viewBox[2]
$svgH    = $viewBox[3]

Write-Host "SVG viewBox: $svgW x $svgH; path length: $($d.Length) chars"

# Gradient stops picked from logo.svg's <linearGradient id="id0">
$stopColorTop    = [System.Windows.Media.Color]::FromRgb(0xE5, 0x2E, 0x71)  # pink
$stopColorBottom = [System.Windows.Media.Color]::FromRgb(0xFF, 0x8A, 0x00)  # orange

function New-IconBitmap([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc     = $visual.RenderOpen()

    # Vertical gradient — StartPoint at top, EndPoint at bottom (relative coords).
    $brush = New-Object System.Windows.Media.LinearGradientBrush
    $null  = $brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop $stopColorTop,    0))
    $null  = $brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop $stopColorBottom, 1))
    $brush.StartPoint = New-Object System.Windows.Point 0.5, 0
    $brush.EndPoint   = New-Object System.Windows.Point 0.5, 1

    # Scale the drawing context from SVG user units to icon pixels.
    $scale = $size / $svgW
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))

    # Clip to a rounded rectangle so the icon has Fluent-style rounded corners.
    # Radius = ~18% of the icon's short side — matches Win11 default squircle look.
    $radius = $svgW * 0.18
    $rect   = New-Object System.Windows.Rect 0, 0, $svgW, $svgH
    $clip   = New-Object System.Windows.Media.RectangleGeometry $rect, $radius, $radius
    $dc.PushClip($clip)

    # Two-layer fill to guarantee explicit white arrows regardless of what surface
    # displays the icon (dark/light theme, taskbar acrylic, etc):
    # 1) Paint the whole viewport WHITE — this becomes the arrow color.
    # 2) Paint the SVG path with the gradient on top. The path uses evenodd fill-rule
    #    so the outer square is filled with gradient but the arrow subpath stays as
    #    transparent holes → the white underneath shows through as white arrows.
    $whiteBrush = [System.Windows.Media.Brushes]::White
    $dc.DrawRectangle($whiteBrush, $null, $rect)

    # Parse the SVG path — WPF's geometry parser understands SVG's path syntax.
    # PathGeometry uses evenodd by default when Geometry.Parse builds a StreamGeometry;
    # to be safe, coerce fill-rule via manual PathGeometry construction below if needed.
    $geom = [System.Windows.Media.Geometry]::Parse($d)
    # Convert to PathGeometry so we can set FillRule to EvenOdd explicitly (matches SVG source).
    $pathGeom = New-Object System.Windows.Media.PathGeometry
    $pathGeom.FillRule = [System.Windows.Media.FillRule]::EvenOdd
    foreach ($fig in $geom.GetFlattenedPathGeometry().Figures) { $pathGeom.Figures.Add($fig) }
    $dc.DrawGeometry($brush, $null, $pathGeom)

    $dc.Pop()   # pop clip
    $dc.Pop()   # pop transform
    $dc.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
        $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()
    return $bitmap
}

function Get-PngBytes([System.Windows.Media.Imaging.RenderTargetBitmap]$bmp) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $null    = $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms      = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return , $ms.ToArray()
}

# Build all sizes first.
$entries = @()
foreach ($size in $Sizes) {
    $bmp = New-IconBitmap $size
    $png = Get-PngBytes $bmp
    $entries += [pscustomobject]@{ Size = $size; Png = $png }
    Write-Host ("  {0}x{0}: {1} bytes PNG" -f $size, $png.Length)
}

# ICO layout: 6-byte ICONDIR + (16-byte ICONDIRENTRY * N) + PNG blobs.
$dirSize = 6 + (16 * $entries.Count)
$out     = New-Object System.IO.FileStream $OutPath, ([System.IO.FileMode]::Create)
$w       = New-Object System.IO.BinaryWriter $out

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
foreach ($e in $entries) { $w.Write($e.Png) }
$w.Close()
$out.Close()

Write-Host "Wrote $OutPath ($((Get-Item $OutPath).Length) bytes)"
