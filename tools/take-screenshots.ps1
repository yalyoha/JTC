#!/usr/bin/env pwsh
# Captures a series of JTC main-window screenshots showing off the theme system.
# Rewrites %LocalAppData%\JTC\settings.json for each preset, launches JTC, waits
# for the window to render, grabs it via GetWindowRect + BitBlt, saves PNG, kills
# JTC, and moves to the next. Original settings.json is backed up and restored
# at the end (torrents.json is never touched — the same torrent list appears in
# every screenshot so the row-fill / plashka differences are directly comparable).

$ErrorActionPreference = 'Stop'

$jtcExe = Join-Path $env:LocalAppData 'Programs\JuniorTorrentClient\JTC.exe'
if (-not (Test-Path $jtcExe)) { throw "JTC not installed at $jtcExe" }

$dataDir      = Join-Path $env:LocalAppData 'JTC'
$settingsPath = Join-Path $dataDir 'settings.json'
$backup       = "$settingsPath.screenshot-backup"
$outDir       = 'E:/PROJECTS/JuniorTorrentClient/screenshots'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

Get-Process -Name JTC -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $settingsPath) { Copy-Item $settingsPath $backup -Force }

# Base settings — take LastDownloadDir + MaxSimultaneousDownloads + CustomPresets
# from the current install so we don't lose them.
$base = if (Test-Path $settingsPath) {
    Get-Content $settingsPath -Raw | ConvertFrom-Json
} else {
    [pscustomobject]@{
        LastDownloadDir = $null
        MaxSimultaneousDownloads = 3
        CustomPresets = @()
    }
}

# Configurations to screenshot, in order. Each merges its fields into $base and
# gets written to settings.json before that launch. Filenames match the visible
# palette so the README can reference them by descriptive name.
$configs = @(
    @{ file = 'theme-colored-pink-orange.png'; note = 'Colored: pink -> orange (default), white plashka'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FFE52E71'; ColoredBottomHex = '#FFFF8A00'
           PlashkaBgHex = '#FFFFFFFF'; PlashkaFgHex = '#FF212121'
           StatusIdleHex = '#FF90A4AE'; StatusDownloadingHex = '#FFFF9100'
           StatusSeedingHex = '#FF00E676'; StatusHashingHex = '#FF2979FF'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'theme-colored-blue-lime.png'; note = 'Colored: blue-navy -> lime, white plashka'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FF324166'; ColoredBottomHex = '#FF7AB317'
           PlashkaBgHex = '#FFFFFFFF'; PlashkaFgHex = '#FF212121'
           StatusIdleHex = '#FF90A4AE'; StatusDownloadingHex = '#FFFF9100'
           StatusSeedingHex = '#FF00E676'; StatusHashingHex = '#FF2979FF'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'theme-colored-pink-orange-dark-plashka.png'; note = 'Colored: pink -> orange, DARK plashka'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FFE52E71'; ColoredBottomHex = '#FFFF8A00'
           PlashkaBgHex = '#FF2A2A2A'; PlashkaFgHex = '#FFFFFFFF'
           StatusIdleHex = '#FF90A4AE'; StatusDownloadingHex = '#FFFF9100'
           StatusSeedingHex = '#FF00E676'; StatusHashingHex = '#FF2979FF'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'theme-colored-purple-teal.png'; note = 'Colored: purple -> teal, cool status palette'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FF6A1B9A'; ColoredBottomHex = '#FF00ACC1'
           PlashkaBgHex = '#FFFFFFFF'; PlashkaFgHex = '#FF212121'
           StatusIdleHex = '#FF78909C'; StatusDownloadingHex = '#FF7C4DFF'
           StatusSeedingHex = '#FF00E5FF'; StatusHashingHex = '#FF3D5AFE'
           StatusErrorHex = '#FFFF3D00'
       } },
    @{ file = 'theme-colored-sunset-dark.png'; note = 'Colored: deep orange -> dark red, dark plashka'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FFFF6F00'; ColoredBottomHex = '#FFB71C1C'
           PlashkaBgHex = '#FF1E1E1E'; PlashkaFgHex = '#FFFFEBCD'
           StatusIdleHex = '#FFBCAAA4'; StatusDownloadingHex = '#FFFFAB40'
           StatusSeedingHex = '#FFFFEB3B'; StatusHashingHex = '#FFFF80AB'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'theme-colored-mint-fresh.png'; note = 'Colored: teal -> cyan, spring statuses'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FF00897B'; ColoredBottomHex = '#FF80DEEA'
           PlashkaBgHex = '#FFFAFAFA'; PlashkaFgHex = '#FF1B1B1B'
           StatusIdleHex = '#FFB0BEC5'; StatusDownloadingHex = '#FFFF6D00'
           StatusSeedingHex = '#FF64DD17'; StatusHashingHex = '#FF00B8D4'
           StatusErrorHex = '#FFD50000'
       } },
    @{ file = 'theme-dark.png'; note = 'Dark theme'
       fields = @{ Theme = 'Dark' } },
    @{ file = 'theme-light.png'; note = 'Light theme'
       fields = @{ Theme = 'Light' } }
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
    // Windows 11 draws a large drop shadow around windows that GetWindowRect
    // includes. DwmGetWindowAttribute with EXTENDED_FRAME_BOUNDS (9) returns
    // the tighter "no shadow" bounds — nicer for showcase screenshots.
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(
        IntPtr h, int a, out RECT r, int sz);
}
"@

function Merge-Fields([object] $target, [hashtable] $fields) {
    foreach ($k in $fields.Keys) {
        if ($target.PSObject.Properties[$k]) {
            $target.$k = $fields[$k]
        } else {
            $target | Add-Member -NotePropertyName $k -NotePropertyValue $fields[$k] -Force
        }
    }
}

$captured = 0
foreach ($cfg in $configs) {
    Write-Host ""
    Write-Host "==> $($cfg.file)"
    Write-Host "    $($cfg.note)"

    # Fresh clone so we don't leak fields from the previous config's non-Coloured theme.
    $obj = $base | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    Merge-Fields $obj $cfg.fields
    $obj | ConvertTo-Json -Depth 6 | Set-Content -Path $settingsPath -Encoding UTF8

    $proc = Start-Process $jtcExe -PassThru
    Start-Sleep -Seconds 3

    # Refetch — the launched pid may spawn a child; we want the one with MainWindow.
    $running = Get-Process -Name JTC -EA SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $running) {
        Write-Warning "  no JTC window — skipping"
        continue
    }
    $hwnd = $running.MainWindowHandle

    [Win]::ShowWindow($hwnd, 9) | Out-Null  # SW_RESTORE
    [Win]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 700

    # Prefer DWM tight bounds; fall back to GetWindowRect.
    $rect = New-Object Win+RECT
    $sz = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([Win+RECT]))
    $hr = [Win]::DwmGetWindowAttribute($hwnd, 9, [ref] $rect, $sz)
    if ($hr -ne 0 -or ($rect.R - $rect.L) -le 0) {
        [Win]::GetWindowRect($hwnd, [ref] $rect) | Out-Null
    }
    $w = $rect.R - $rect.L
    $h = $rect.B - $rect.T
    if ($w -le 0 -or $h -le 0) {
        Write-Warning "  bad window rect ($w x $h) — skipping"
        Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force
        continue
    }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, [System.Drawing.Size]::new($w, $h))
    $outPath = Join-Path $outDir $cfg.file
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
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
