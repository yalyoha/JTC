# TClient

Minimal personal-use BitTorrent client for Windows 11.

Built on **WinUI 3** (Windows App SDK, **unpackaged** — no MSIX, no Microsoft Store) and **MonoTorrent**.

## Build

Prerequisites:
- Windows 10 20H1+ (Windows 11 recommended for the full Fluent look)
- .NET 10 SDK
- No Visual Studio required — CLI is enough

```powershell
git clone <this repo>
cd TClient
dotnet build -c Release
dotnet run --project src/TClient
```

For a distributable single-file `.exe`:
```powershell
dotnet publish src/TClient -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# output at src/TClient/bin/Release/net10.0-windows.../win-x64/publish/TClient.exe
```

No `.msix` is produced — this is intentional.

## Runtime data

State is stored under `%LocalAppData%\TClient\`:
- `cache/` — MonoTorrent fast-resume + DHT node cache
- `torrents.json` — the list of known torrents

Delete this folder to reset the app.

## Smoke test procedure

After building, verify manually:

1. Launch — window appears with Acrylic backdrop, "TClient" title left, min/max/close right
2. Click **Open .torrent** — pick a small public-domain torrent (e.g., latest Ubuntu netinst from `releases.ubuntu.com/*.torrent`)
3. Pick a destination folder — download starts, row appears, progress ticks
4. Click **Pause** on the selected row — state changes; DL rate goes to `—`
5. Click **Resume** — state returns to Downloading
6. Close and reopen the app — the torrent reappears and resumes from the same offset
7. Click **Add magnet** — paste a valid magnet URI; verify metadata resolves and download starts
8. Click **Remove** → **Keep files** — row disappears, files remain
9. Click **Remove** → **Delete files** on a completed download — row disappears, files are gone

If any step fails, file the reproduction in a scratch note and fix.

## Design docs

- Spec: `docs/superpowers/specs/2026-07-11-tclient-design.md`
- Implementation plan: `docs/superpowers/plans/2026-07-11-tclient.md`
