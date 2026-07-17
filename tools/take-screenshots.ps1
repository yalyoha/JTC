#!/usr/bin/env pwsh
# Captures a JTC main-window screenshot for every built-in preset in
# BuiltInColorPresets.All plus the Dark and Light themes. Rewrites
# %LocalAppData%\JTC\settings.json for each pass, launches JTC, grabs the
# window via GetWindowRect + BitBlt, saves PNG, kills JTC. Original
# settings.json is backed up and restored at the end.

$ErrorActionPreference = 'Stop'

# Prefer the freshly-built Debug binary so unreleased preset additions show up
# even before we ship a new version. Falls back to installed release if Debug
# isn't built.
$debugExe     = 'E:/PROJECTS/JuniorTorrentClient/src/JTC/bin/Debug/net10.0-windows10.0.19041.0/win-x64/JTC.exe'
$installedExe = Join-Path $env:LocalAppData 'Programs\JuniorTorrentClient\JTC.exe'
$jtcExe = if (Test-Path $debugExe) { $debugExe } else { $installedExe }
if (-not (Test-Path $jtcExe)) { throw "JTC not found at $debugExe or $installedExe" }

$dataDir      = Join-Path $env:LocalAppData 'JTC'
$settingsPath = Join-Path $dataDir 'settings.json'
$backup       = "$settingsPath.screenshot-backup"
$outDir       = 'E:/PROJECTS/JuniorTorrentClient/screenshots'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

Get-Process -Name JTC -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $settingsPath) { Copy-Item $settingsPath $backup -Force }

$base = if (Test-Path $settingsPath) {
    Get-Content $settingsPath -Raw | ConvertFrom-Json
} else {
    [pscustomobject]@{ LastDownloadDir = $null; MaxSimultaneousDownloads = 3; CustomPresets = @() }
}

# All 10 built-in presets from BuiltInColorPresets.All + Dark + Light. Order and
# hex values must match src/JTC/Services/AppSettings.cs — if you tweak a preset
# there, mirror the change here so the screenshot filename stays meaningful.
$configs = @(
    # ── Odd presets (1,3,5,7,9) + Dark + Light: SQUARE plashkas, SQUARE buttons,
    #    LEFT-STRIPE status indicator. Even presets (2,4,6,8,10): keep the
    #    default rounded capsule + circle look. Mix intentionally showcases
    #    v0.5.7's new "Оформление" controls in the gallery.
    @{ file = 'theme-preset-01-blue-lime.png';      note = 'Юниор (built-in #1 — default) · square + stripe'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF324166'; ColoredBottomHex='#FF7AB317'
                   PlashkaBgHex='#FFFFFFFF'; PlashkaFgHex='#FF212121'
                   StatusIdleHex='#FF90A4AE'; StatusDownloadingHex='#FFFF9100'
                   StatusSeedingHex='#FF00E676'; StatusHashingHex='#FF2979FF'; StatusErrorHex='#FFFF1744'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-preset-02-pink-orange.png';    note = 'Фламинго (built-in #2)'
       fields = @{ Theme='Colored'; ColoredTopHex='#FFE52E71'; ColoredBottomHex='#FFFF8A00'
                   PlashkaBgHex='#FFFFFFFF'; PlashkaFgHex='#FF212121'
                   StatusIdleHex='#FF90A4AE'; StatusDownloadingHex='#FFFF9100'
                   StatusSeedingHex='#FF00E676'; StatusHashingHex='#FF2979FF'; StatusErrorHex='#FFFF1744' } },
    @{ file = 'theme-preset-03-sunset.png';         note = 'Закат · square + stripe'
       fields = @{ Theme='Colored'; ColoredTopHex='#FFFF6F00'; ColoredBottomHex='#FFB71C1C'
                   PlashkaBgHex='#FF1E1E1E'; PlashkaFgHex='#FFFFEBCD'
                   StatusIdleHex='#FFBCAAA4'; StatusDownloadingHex='#FFFFAB40'
                   StatusSeedingHex='#FFFFEB3B'; StatusHashingHex='#FFFF80AB'; StatusErrorHex='#FFFF1744'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-preset-04-ocean.png';          note = 'Океан'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF01579B'; ColoredBottomHex='#FF00838F'
                   PlashkaBgHex='#FFFFFFFF'; PlashkaFgHex='#FF212121'
                   StatusIdleHex='#FF90A4AE'; StatusDownloadingHex='#FF0091EA'
                   StatusSeedingHex='#FF00E5FF'; StatusHashingHex='#FF00B8D4'; StatusErrorHex='#FFFF1744' } },
    @{ file = 'theme-preset-05-purple-dusk.png';    note = 'Фиолетовый мрак · square + stripe'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF311B92'; ColoredBottomHex='#FFAA00FF'
                   PlashkaBgHex='#FF212121'; PlashkaFgHex='#FFF3E5F5'
                   StatusIdleHex='#FF9575CD'; StatusDownloadingHex='#FFE040FB'
                   StatusSeedingHex='#FF7C4DFF'; StatusHashingHex='#FF651FFF'; StatusErrorHex='#FFFF3D00'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-preset-06-mint.png';           note = 'Мятная свежесть'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF00897B'; ColoredBottomHex='#FF80DEEA'
                   PlashkaBgHex='#FFFAFAFA'; PlashkaFgHex='#FF1B1B1B'
                   StatusIdleHex='#FFB0BEC5'; StatusDownloadingHex='#FFFF6D00'
                   StatusSeedingHex='#FF64DD17'; StatusHashingHex='#FF00B8D4'; StatusErrorHex='#FFD50000' } },
    @{ file = 'theme-preset-07-forest.png';         note = 'Лес · square + stripe'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF1B5E20'; ColoredBottomHex='#FFC0CA33'
                   PlashkaBgHex='#FFFAFAFA'; PlashkaFgHex='#FF212121'
                   StatusIdleHex='#FF8D6E63'; StatusDownloadingHex='#FFFFC107'
                   StatusSeedingHex='#FF43A047'; StatusHashingHex='#FF00ACC1'; StatusErrorHex='#FFE53935'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-preset-08-cyberpunk.png';      note = 'Киберпанк'
       fields = @{ Theme='Colored'; ColoredTopHex='#FFE91E63'; ColoredBottomHex='#FF00E5FF'
                   PlashkaBgHex='#FF0F0F0F'; PlashkaFgHex='#FFEEFF41'
                   StatusIdleHex='#FF546E7A'; StatusDownloadingHex='#FFFF00E5'
                   StatusSeedingHex='#FF00FF88'; StatusHashingHex='#FF00E5FF'; StatusErrorHex='#FFFF073F' } },
    @{ file = 'theme-preset-09-coffee.png';         note = 'Кофе · square + stripe'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF3E2723'; ColoredBottomHex='#FFD7CCC8'
                   PlashkaBgHex='#FFF5EDE0'; PlashkaFgHex='#FF3E2723'
                   StatusIdleHex='#FFA1887F'; StatusDownloadingHex='#FFFFB300'
                   StatusSeedingHex='#FF8BC34A'; StatusHashingHex='#FF00838F'; StatusErrorHex='#FFBF360C'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-preset-10-aurora.png';         note = 'Северное сияние'
       fields = @{ Theme='Colored'; ColoredTopHex='#FF1A237E'; ColoredBottomHex='#FF00E676'
                   PlashkaBgHex='#FF0D1B2A'; PlashkaFgHex='#FFE8EAF6'
                   StatusIdleHex='#FF7986CB'; StatusDownloadingHex='#FF00B0FF'
                   StatusSeedingHex='#FF69F0AE'; StatusHashingHex='#FF7C4DFF'; StatusErrorHex='#FFFF5252' } },
    @{ file = 'theme-dark.png';                     note = 'Dark theme · square + stripe'
       fields = @{ Theme='Dark'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } },
    @{ file = 'theme-light.png';                    note = 'Light theme · square + stripe'
       fields = @{ Theme='Light'
                   ButtonCornerRadius=0; PlashkaCornerRadius=0; StatusIndicatorStyle='Stripe' } }
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int sz);
}
"@

function Merge-Fields([object] $target, [hashtable] $fields) {
    foreach ($k in $fields.Keys) {
        if ($target.PSObject.Properties[$k]) { $target.$k = $fields[$k] }
        else { $target | Add-Member -NotePropertyName $k -NotePropertyValue $fields[$k] -Force }
    }
}

$captured = 0
foreach ($cfg in $configs) {
    Write-Host ""
    Write-Host "==> $($cfg.file)"
    Write-Host "    $($cfg.note)"

    $obj = $base | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    Merge-Fields $obj $cfg.fields
    $obj | ConvertTo-Json -Depth 6 | Set-Content -Path $settingsPath -Encoding UTF8

    $proc = Start-Process $jtcExe -PassThru
    # Longer wait than v0.5.6 (3 s) so MonoTorrent has time to re-attach to
    # the fast-resume state, discover peers, and flip the row status from
    # Waiting -> Downloading — so the 25 % status-tint on the progress fill
    # renders in the actual download colour (orange), not the muted idle grey.
    Start-Sleep -Seconds 10

    $running = Get-Process -Name JTC -EA SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $running) { Write-Warning "  no JTC window"; continue }
    $hwnd = $running.MainWindowHandle
    [Win]::ShowWindow($hwnd, 9) | Out-Null
    [Win]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600

    $rect = New-Object Win+RECT
    $sz = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([Win+RECT]))
    $hr = [Win]::DwmGetWindowAttribute($hwnd, 9, [ref] $rect, $sz)
    if ($hr -ne 0 -or ($rect.R - $rect.L) -le 0) {
        [Win]::GetWindowRect($hwnd, [ref] $rect) | Out-Null
    }
    $w = $rect.R - $rect.L
    $h = $rect.B - $rect.T
    if ($w -le 0 -or $h -le 0) { Write-Warning "  bad rect"; Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force; continue }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, [System.Drawing.Size]::new($w, $h))
    $outPath = Join-Path $outDir $cfg.file
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "    saved: $outPath ($($w)x$($h))"
    $captured++

    Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if (Test-Path $backup) {
    Copy-Item $backup $settingsPath -Force
    Remove-Item $backup -Force
    Write-Host ""
    Write-Host "Restored original settings.json"
}
Write-Host ""
Write-Host "Done. Captured $captured / $($configs.Count) screenshots."
