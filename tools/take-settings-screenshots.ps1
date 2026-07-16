#!/usr/bin/env pwsh
# Companion to take-screenshots.ps1 — captures the settings dialog open on top of
# the main window for a couple of themes. Uses UI Automation to invoke the
# "Настройки" button so no mouse coordinates are needed. Escape key closes the
# dialog before we kill JTC to keep state clean.

$ErrorActionPreference = 'Stop'

$jtcExe = 'E:/PROJECTS/JuniorTorrentClient/src/JTC/bin/Debug/net10.0-windows10.0.19041.0/win-x64/JTC.exe'
if (-not (Test-Path $jtcExe)) { throw "JTC debug build not found at $jtcExe" }

$dataDir      = Join-Path $env:LocalAppData 'JTC'
$settingsPath = Join-Path $dataDir 'settings.json'
$backup       = "$settingsPath.settings-screenshot-backup"
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

$configs = @(
    @{ file = 'settings-colored-pink-orange.png'; note = 'Settings dialog on Colored (pink -> orange)'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FFE52E71'; ColoredBottomHex = '#FFFF8A00'
           PlashkaBgHex = '#FFFFFFFF'; PlashkaFgHex = '#FF212121'
           StatusIdleHex = '#FF90A4AE'; StatusDownloadingHex = '#FFFF9100'
           StatusSeedingHex = '#FF00E676'; StatusHashingHex = '#FF2979FF'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'settings-colored-blue-lime.png'; note = 'Settings dialog on Colored (blue -> lime)'
       fields = @{
           Theme = 'Colored'
           ColoredTopHex = '#FF324166'; ColoredBottomHex = '#FF7AB317'
           PlashkaBgHex = '#FFFFFFFF'; PlashkaFgHex = '#FF212121'
           StatusIdleHex = '#FF90A4AE'; StatusDownloadingHex = '#FFFF9100'
           StatusSeedingHex = '#FF00E676'; StatusHashingHex = '#FF2979FF'
           StatusErrorHex = '#FFFF1744'
       } },
    @{ file = 'settings-dark.png'; note = 'Settings dialog on Dark'
       fields = @{ Theme = 'Dark' } },
    @{ file = 'settings-light.png'; note = 'Settings dialog on Light'
       fields = @{ Theme = 'Light' } }
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win2 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
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
    Start-Sleep -Seconds 3

    $running = Get-Process -Name JTC -EA SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $running) { Write-Warning "  no JTC window"; continue }
    $hwnd = $running.MainWindowHandle

    [Win2]::ShowWindow($hwnd, 9) | Out-Null
    [Win2]::SetForegroundWindow($hwnd) | Out-Null
    # Grow the main window enough that the ContentDialog can expand vertically
    # to fit the FULL colour list (gradient + plashka + 5 statuses + presets
    # + description + action buttons ≈ 890 dip). Dialog title + CommandSpace
    # buttons + padding claim ~160 dip on top of that, so window needs ≥ 1150
    # tall to avoid the ContentScrollViewer clipping. 1250 gives comfortable
    # breathing room without pushing the dialog off screens smaller than that.
    [Win2]::MoveWindow($hwnd, 60, 10, 1100, 1250, $true) | Out-Null
    Start-Sleep -Milliseconds 700

    # Invoke SettingsButton via UI Automation — no pixel guessing needed.
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'SettingsButton')
        $btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if (-not $btn) { throw "SettingsButton not found in UI tree" }
        $invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
    } catch {
        Write-Warning "  UIA invoke failed: $_"
        Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force
        continue
    }

    # Wait for the dialog to render + layout + our Loaded handlers to finish
    # (ContentScrollViewer scroll-bar enable + CommandSpace clearing).
    Start-Sleep -Milliseconds 1200

    $rect = New-Object Win2+RECT
    $sz = [System.Runtime.InteropServices.Marshal]::SizeOf([type]([Win2+RECT]))
    $hr = [Win2]::DwmGetWindowAttribute($hwnd, 9, [ref] $rect, $sz)
    if ($hr -ne 0 -or ($rect.R - $rect.L) -le 0) {
        [Win2]::GetWindowRect($hwnd, [ref] $rect) | Out-Null
    }
    $w = $rect.R - $rect.L
    $h = $rect.B - $rect.T
    if ($w -le 0 -or $h -le 0) {
        Write-Warning "  bad window rect"
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

    # Close the dialog with Escape before killing, so if the app crashes on
    # kill we at least don't leave a modal open.
    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    Start-Sleep -Milliseconds 300
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
Write-Host "Done. Captured $captured / $($configs.Count) dialog screenshots."
