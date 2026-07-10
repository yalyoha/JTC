# TClient — Design Spec

**Date:** 2026-07-11
**Status:** Approved for planning
**Author:** design brainstormed with Claude

## Purpose

A minimal, personal-use BitTorrent client for Windows 11 with a native Fluent UI look. Built on top of the `MonoTorrent` .NET library — we own the UX, MonoTorrent owns the protocol.

## Goals

- Launch on Win11, look like a first-party Win11 app (Acrylic backdrop, system accent color, Segoe Fluent Icons, correct titlebar integration)
- Add torrents from `.torrent` files or `magnet:` links
- Download to a user-chosen folder, verify, restore across restarts
- Show live per-torrent and aggregate progress
- Pause / resume / remove torrents

## Non-Goals (explicit YAGNI)

- No cross-platform build (Windows-only)
- **No MSIX packaging, ever.** No Microsoft Store, no `.msix`/`.msixbundle`, no `Package.appxmanifest`, no signing cert, no sideloading flow. This app is unpackaged and always will be. Distribution is a plain `.exe` (plus its dependency DLLs, or single-file self-contained publish)
- No traditional installer either — just copy the output folder or single-file `.exe` and run
- No settings UI (constants in code for port, max connections)
- No selective-file download from multi-file torrents (download everything)
- No system tray icon, no minimize-to-tray
- No `.torrent` file-type association or `magnet:` URI-scheme registration
- No seeding-focused features (ratio limits, seeding time targets) — we still upload while a torrent is present, but there's no dedicated UI for it
- No search / built-in trackers list / RSS
- No remote control / web UI
- No IPv6-specific handling beyond what MonoTorrent does by default
- No proxy / VPN configuration

## Stack

- **.NET 10** (SDK already installed: 10.0.200)
- **WinUI 3** via `Microsoft.WindowsAppSDK` in **unpackaged mode**. Concretely this means the csproj sets:
  - `<OutputType>WinExe</OutputType>`
  - `<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>`
  - `<UseWinUI>true</UseWinUI>`
  - `<WindowsPackageType>None</WindowsPackageType>` — this is the flag that disables MSIX packaging
  - `<EnableMsixTooling>false</EnableMsixTooling>` (belt-and-braces; ensures the MSIX build targets are not imported)
  - `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` — bundles the WindowsAppSDK runtime so users don't need to install anything
  - `<SelfContained>true</SelfContained>` + `<PublishSingleFile>true</PublishSingleFile>` for `dotnet publish` output (a single `.exe`)
  - **No** `Package.appxmanifest` file exists in the project. The application icon and version metadata live in a Win32-style resource / `AssemblyInfo` instead
  - App bootstrap uses the WindowsAppSDK **auto-initializer** (`Microsoft.WindowsAppSDK.SelfContained` implicit init) — no manual `Bootstrap.Initialize` call needed
- **MonoTorrent** (latest stable NuGet) — the entire BitTorrent stack (bencode, trackers HTTP/UDP, DHT, PEX, peer wire protocol, uTP, encryption, hash verification, fast-resume)
- **CommunityToolkit.Mvvm** — source-generated `[ObservableProperty]` / `[RelayCommand]` to keep MVVM boilerplate minimal
- **xUnit** — unit tests for pure helpers only
- Language: C# 13, nullable enabled, implicit usings enabled

Zero other runtime dependencies.

## User Experience

Single window. Console messages and UI text are in English.

```
┌─────────────────────────────────────────────────────────┐
│ [+ Open .torrent]  [+ Add magnet]      [▶] [❚❚] [🗑]    │  command bar
├─────────────────────────────────────────────────────────┤
│ Name              Size    Progress       DL      UL  P  │
│ ubuntu-24.04.iso  5.7 GB  ▓▓▓▓▓▓░░ 62%   4.2M/s  0.1  42│
│ debian-12.iso     600MB   ▓▓▓▓▓▓▓▓100%   —       —    — │
├─────────────────────────────────────────────────────────┤
│ ↓ 4.2 MB/s   ↑ 0.1 MB/s   Peers 42                      │  status bar
└─────────────────────────────────────────────────────────┘
```

**Interactions:**
- `Open .torrent` — WinUI `FileOpenPicker` filtered to `.torrent`. Then a `FolderPicker` for the destination. Torrent is added and started immediately
- `Add magnet` — a `ContentDialog` with a `TextBox` for the magnet URI, then a `FolderPicker` for destination
- `Play` / `Pause` / `Remove` — act on the currently selected row(s). Enabled only when a row is selected. Remove asks "delete files on disk too? Yes / No / Cancel"
- Column headers are static (no sorting in MVP)
- Row shows a per-row `ProgressBar` using the system accent color (or `Paused` visual state when paused)

**Look:**
- Window uses `DesktopAcrylicBackdrop` — translucent with blur (matches user's spec)
- Title bar is extended into client area (`ExtendsContentIntoTitleBar = true`) with a custom minimal `TitleBar` control so acrylic reads through it
- All colors bound via `{ThemeResource ...}` — system accent, light/dark theme reacts live
- Icons from Segoe Fluent Icons font (`&#xE710;` Add, `&#xE768;` Play, `&#xE769;` Pause, `&#xE74D;` Delete)
- No custom color palette — everything follows the system theme

## Architecture

```
┌────────────────────────────────┐
│  MainWindow (XAML)             │
│  + custom TitleBar             │
└──────────────┬─────────────────┘
               │ DataBinding
               ▼
┌────────────────────────────────┐
│  MainViewModel                 │
│    ObservableCollection<       │
│       TorrentViewModel>        │
│    Aggregate DL/UL/Peers       │
│    RelayCommands (open/paste/  │
│       play/pause/remove)       │
└──────────────┬─────────────────┘
               │
               ▼
┌────────────────────────────────┐
│  TorrentService (singleton)    │
│    holds ClientEngine          │
│    AddTorrent / AddMagnet      │
│    Pause / Resume / Remove     │
│    LoadState / SaveState       │
└──────────────┬─────────────────┘
               │
               ▼
      ┌──────────────────┐
      │  MonoTorrent     │
      │  ClientEngine    │
      │  TorrentManager  │
      └──────────────────┘
```

### Components

**`App` (`App.xaml` / `App.xaml.cs`)**
- Standard WinUI 3 bootstrap
- Owns the `TorrentService` singleton
- On `OnLaunched`: create `MainWindow`, wire it to a `MainViewModel` that references the service, call `TorrentService.LoadStateAsync()`
- On window `Closed`: `await TorrentService.ShutdownAsync()` before disposing

**`MainWindow`**
- XAML: `Grid` with three rows — command bar, list, status bar
- Sets `SystemBackdrop = new DesktopAcrylicBackdrop()`
- Sets `ExtendsContentIntoTitleBar = true`; a `TitleBar` control lives at top of the grid, drag region set via `SetTitleBar`
- List is a `ListView` with a `DataTemplate` binding to `TorrentViewModel`. Selection mode: `Extended`
- No code-behind logic beyond wiring picker calls to VM commands (pickers need the window `HWND`, so their invocation lives here)

**`MainViewModel`**
- `ObservableCollection<TorrentViewModel> Torrents`
- `TorrentViewModel? SelectedTorrent`
- Aggregate properties: `TotalDownloadRate`, `TotalUploadRate`, `TotalPeers`
- Commands: `OpenTorrentFileCommand`, `AddMagnetCommand`, `PlayCommand`, `PauseCommand`, `RemoveCommand`
- Timer (`DispatcherQueueTimer`, 1 s) that recomputes aggregates and pokes each `TorrentViewModel` to refresh volatile fields

**`TorrentViewModel`**
- Wraps a single `TorrentManager`
- `[ObservableProperty]` fields: `Name`, `SizeText`, `Progress` (0–100 double), `ProgressText`, `DownloadRateText`, `UploadRateText`, `PeerCount`, `StateText`, `IsPaused`
- Subscribes to `TorrentManager.TorrentStateChanged` and `PieceHashed`; refresh happens both from events and from the MainViewModel's 1 s tick
- Uses `DispatcherQueue.TryEnqueue` to marshal back to UI thread (MonoTorrent events fire on worker threads)

**`TorrentService` (singleton)**
- Owns `ClientEngine` with `EngineSettings { CacheDirectory = <appdata>\cache, MaximumConnections = 200 }`
- `AddTorrentFileAsync(string torrentPath, string downloadDir)` — `engine.AddAsync(torrentPath, downloadDir)`, then `manager.StartAsync()`
- `AddMagnetAsync(string uri, string downloadDir)` — `MagnetLink.Parse`, `engine.AddAsync(link, downloadDir)`, `manager.StartAsync()`
- `PauseAsync(TorrentManager m)` / `ResumeAsync(TorrentManager m)` — thin wrappers
- `RemoveAsync(TorrentManager m, bool deleteFiles)` — `engine.RemoveAsync(m)`, optionally delete files/folder on disk
- `LoadStateAsync()` — read `torrents.json`, re-add each entry
- `SaveStateAsync()` — write `torrents.json`
- `ShutdownAsync()` — stop all, save state, dispose engine
- Raises `TorrentAdded` / `TorrentRemoved` events consumed by `MainViewModel`

**`Formatting` (static helpers)**
- `BytesToHuman(long)` → `"5.7 GB"`, `"600 MB"`, `"1.2 KB"`
- `RateToHuman(long bytesPerSec)` → `"4.21 MB/s"` or `"—"` when zero
- `EtaToHuman(TimeSpan)` → `"00:14:22"` or `"—"`
- Pure functions; the only code with real unit tests

### Data flow — adding a torrent

```
User clicks "Open .torrent"
  → MainWindow uses FileOpenPicker (needs HWND from window)
  → returns path
  → MainWindow calls MainViewModel.OpenTorrentFileCommand(path)
  → VM calls FolderPicker for download-dir
  → VM calls TorrentService.AddTorrentFileAsync(path, dir)
  → Service: engine.AddAsync -> manager -> manager.StartAsync
  → Service raises TorrentAdded(manager)
  → VM handler creates TorrentViewModel(manager) and adds to Torrents
  → VM appends new entry to torrents.json (SaveStateAsync)
```

### Data flow — progress update (1 s cadence)

```
DispatcherQueueTimer tick
  → For each TorrentViewModel:
      - read manager.Progress, manager.Monitor.DownloadRate / UploadRate,
        manager.Peers.Available, manager.State
      - update [ObservableProperty] fields (bindings propagate to UI)
  → MainViewModel recomputes aggregates
```

## Persistence

Two artifacts under `%LocalAppData%\TClient\`:

1. `cache\` — MonoTorrent's own cache directory (fast-resume files, DHT node cache, metadata for magnet-only adds). Managed entirely by MonoTorrent.
2. `torrents.json` — our own list of "known" torrents. Schema:
   ```json
   [
     {
       "source": "C:\\...\\ubuntu-24.04.iso.torrent",  // or "magnet:?xt=..."
       "sourceKind": "TorrentFile" | "Magnet",
       "downloadDir": "D:\\Downloads",
       "paused": false
     }
   ]
   ```
   When `sourceKind == TorrentFile`, we may copy the `.torrent` into `cache\` so removing the original file doesn't break resume. To be decided at implementation time; for MVP, storing the original path is fine and we accept that a moved/deleted source means the torrent is gone on next launch.

On startup: `TorrentService.LoadStateAsync` reads `torrents.json` and re-adds each entry, respecting the `paused` flag.

On graceful shutdown: `SaveStateAsync` writes the current list; MonoTorrent flushes its own cache.

On unclean shutdown: we lose whatever was not yet persisted; MonoTorrent will re-hash from disk on next start to recover, which is fine.

## Error handling

- **Invalid `.torrent` file** — catch `TorrentException` in the service, surface via a `ContentDialog` "Could not open this torrent: <message>"
- **Invalid magnet URI** — `MagnetLink.TryParse` returns false → same `ContentDialog`
- **Destination folder unwritable / disk full** — MonoTorrent will raise; catch, show dialog, remove the torrent
- **Tracker / peer errors** — swallow silently (MonoTorrent retries internally). We reflect them only via `StateText` if state becomes `Error`
- **Corrupt fast-resume** — MonoTorrent handles by re-hashing; nothing for us to do

We do not build a logging system for MVP. `Debug.WriteLine` for developer diagnostics; nothing user-facing besides dialogs.

## Threading

- All MonoTorrent events arrive on worker threads
- `TorrentViewModel` uses `DispatcherQueue.TryEnqueue` for every UI-facing property update
- `TorrentService` operations are `async` and can be awaited from the UI thread directly
- No manual locking — `ObservableCollection<T>` is only mutated on the UI thread

## Testing

Unit tests (xUnit) for pure logic only:

- `FormattingTests` — byte/rate/ETA formatting: zero, small, large, threshold rollovers, negative-guard
- `MagnetParsingTests` (if we add any custom pre-validation before handing to MonoTorrent)

We do **not** unit-test:
- MonoTorrent internals — trust the library
- ViewModels — bindings are validated by running the app
- UI XAML — WinUI has no meaningful headless test story

Manual smoke test procedure (documented in README):
1. Add a small public-domain torrent (e.g., Ubuntu netinst)
2. Verify hashing → downloading → seeding transition
3. Verify pause / resume
4. Verify remove (both keep-files and delete-files)
5. Close app while downloading, re-open, verify resume from correct offset
6. Verify Acrylic renders and reacts to theme/accent changes

## Project layout

```
E:\PROJECTS\TClient\
├── TClient.sln
├── .gitignore                         # dotnet + WinUI
├── README.md                          # build + smoke-test steps
├── src\
│   └── TClient\
│       ├── TClient.csproj             # unpackaged: WindowsPackageType=None, no Package.appxmanifest
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Controls\
│       │   └── TitleBar.xaml / .cs
│       ├── ViewModels\
│       │   ├── MainViewModel.cs
│       │   └── TorrentViewModel.cs
│       ├── Services\
│       │   └── TorrentService.cs
│       ├── Helpers\
│       │   └── Formatting.cs
│       └── Assets\                    # app icon
├── tests\
│   └── TClient.Tests\
│       ├── TClient.Tests.csproj
│       └── FormattingTests.cs
└── docs\superpowers\specs\
    └── 2026-07-11-tclient-design.md
```

## Open questions deferred to implementation

- Exact WindowsAppSDK version — pick latest stable at implementation time
- Whether to snapshot the `.torrent` file into cache to survive source-file deletion (nice-to-have)
- Row context-menu vs. only command bar for actions — start with command bar, add context menu if it feels missing during smoke test
- Exact `MaximumConnections` value — starts at 200, tune if needed

## Success criteria

Done when all of these hold on a clean Win11 machine with .NET 10:
- `dotnet run --project src\TClient` launches the app with Acrylic backdrop
- Adding a `.torrent` file starts the download and shows live progress
- Adding a magnet link resolves metadata and starts the download
- Pausing / resuming / removing works
- Closing and reopening resumes torrents from where they were
- The app visually reads as a Win11 Fluent app (accent color, theme reactivity, correct titlebar, Segoe icons)
- `dotnet test` is green
- `dotnet publish -c Release -r win-x64` produces a runnable `.exe` (no `.msix`, no `Package.appxmanifest` anywhere in the tree, no Store dependency, no cert install needed)
