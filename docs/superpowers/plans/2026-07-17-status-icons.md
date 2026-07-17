# Status Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Russian status word in every torrent row's "Состояние" column with a Segoe MDL2 Assets glyph coloured by the existing StatusBrush, add Пауза as a first-class state, keep the Russian word as a hover tooltip.

**Architecture:** Purely additive in `TorrentViewModel` (new `Display.Paused` enum member, new `StateGlyph` observable property populated by the same switch that fills `StateText`); one line changes in `MainWindow.xaml` (TextBlock → FontIcon in the row template). No changes to settings, storage, or theme helpers — the icon inherits `StatusBrush`, which is already user-configurable per state.

**Tech Stack:** WinUI 3 (`FontIcon`), CommunityToolkit.Mvvm `[ObservableProperty]`, Segoe MDL2 Assets font (bundled with Windows 10 20H1+ / 11).

**Spec:** `docs/superpowers/specs/2026-07-17-status-icons-design.md`

**TDD note:** No unit tests. `TorrentViewModel` is not covered by the existing test suite (checked `tests/JTC.Tests/`) — it depends on `DispatcherQueue`, `Brush`, and `TorrentManager` which have no test doubles. Verification is manual (build succeeds, running app shows correct glyph per state, tooltip renders in Russian). This matches the existing convention for VM changes in this codebase.

---

### Task 1: TorrentViewModel — Display.Paused + StateGlyph

**Files:**
- Modify: `src/JTC/ViewModels/TorrentViewModel.cs`

- [ ] **Step 1: Add `Paused` to the `Display` enum**

Edit `src/JTC/ViewModels/TorrentViewModel.cs` line 25:

Replace:
```csharp
private enum Display { Waiting, Downloading, Seeding, Error, Hashing }
```

With:
```csharp
private enum Display { Waiting, Downloading, Seeding, Error, Hashing, Paused }
```

- [ ] **Step 2: Add the `StateGlyph` observable property**

Edit `src/JTC/ViewModels/TorrentViewModel.cs` after line 39 (`_stateText = "Ожидание";`), insert:

```csharp
    // Segoe MDL2 Assets glyph for the row's status column. Mirrors StateText through
    // the same ApplyDisplay switch so the two are always in sync. Default is Hourglass
    // (E823) to match the "Ожидание" default StateText.
    [ObservableProperty] private string _stateGlyph = "";
```

- [ ] **Step 3: Populate `StateGlyph` in `ApplyDisplay`**

Edit `src/JTC/ViewModels/TorrentViewModel.cs` `ApplyDisplay` method (currently at lines 121-135). Replace the entire method body with:

```csharp
    private void ApplyDisplay(Display d)
    {
        if (_current == d)
            return;
        _current = d;
        StateText = d switch
        {
            Display.Seeding     => "Раздача",
            Display.Downloading => "Загрузка",
            Display.Error       => "Ошибка",
            Display.Hashing     => "Проверка",
            Display.Paused      => "Пауза",
            _                   => "Ожидание",
        };
        // Segoe MDL2 Assets codepoints. The string literals below contain the actual
        // Private-Use-Area characters (U+E898 Upload, U+E896 Download, U+E7BA Warning,
        // U+E895 Sync, U+E769 Pause, U+E823 Hourglass). If your editor collapses them to
        // blanks, type the values as "" etc. instead — the C# compiler accepts both
        // and the runtime rendering is identical.
        StateGlyph = d switch
        {
            Display.Seeding     => "", // Upload
            Display.Downloading => "", // Download
            Display.Error       => "", // Warning
            Display.Hashing     => "", // Sync
            Display.Paused      => "", // Pause
            _                   => "", // Hourglass (Waiting)
        };
        RebuildRowBackground();
    }
```

- [ ] **Step 4: Map Paused/Stopped in `ComputeDisplay`**

Edit `src/JTC/ViewModels/TorrentViewModel.cs` `ComputeDisplay` method (currently at lines 190-197). Replace with:

```csharp
    private static Display ComputeDisplay(TorrentManager m) => m.State switch
    {
        TorrentState.Seeding                                     => Display.Seeding,
        TorrentState.Error                                       => Display.Error,
        TorrentState.Hashing                                     => Display.Hashing,
        TorrentState.Paused or TorrentState.Stopped              => Display.Paused,
        TorrentState.Downloading when m.Monitor.DownloadRate > 0 => Display.Downloading,
        _                                                        => Display.Waiting,
    };
```

- [ ] **Step 5: Handle the `Display.Paused` colour in `RebuildRowBackground`**

Edit `src/JTC/ViewModels/TorrentViewModel.cs` `RebuildRowBackground`, the `statusColor` switch currently at lines 148-155. Add the `Paused` arm so it explicitly reuses the idle colour (documenting intent — without it the `_` fallback covers it, but the explicit arm makes the "paused reuses idle" decision visible):

Replace:
```csharp
        var statusColor = _current switch
        {
            Display.Downloading => RowBrushes.StatusDownloading,
            Display.Seeding     => RowBrushes.StatusSeeding,
            Display.Hashing     => RowBrushes.StatusHashing,
            Display.Error       => RowBrushes.StatusError,
            _                   => RowBrushes.StatusIdle,
        };
```

With:
```csharp
        var statusColor = _current switch
        {
            Display.Downloading => RowBrushes.StatusDownloading,
            Display.Seeding     => RowBrushes.StatusSeeding,
            Display.Hashing     => RowBrushes.StatusHashing,
            Display.Error       => RowBrushes.StatusError,
            // Paused shares the idle grey — only the glyph in the state column differs
            // from Waiting. No dedicated setting for a "paused" colour.
            Display.Paused      => RowBrushes.StatusIdle,
            _                   => RowBrushes.StatusIdle,
        };
```

- [ ] **Step 6: Build to verify**

Run:
```
dotnet build src/JTC/JTC.csproj -c Debug -v minimal
```

Expected: `Сборка успешно завершена.` / `Build succeeded.` with 0 errors (existing 21 MVVMTK0045 warnings are pre-existing and unrelated).

- [ ] **Step 7: Commit**

```
cd E:/PROJECTS/JuniorTorrentClient
git add src/JTC/ViewModels/TorrentViewModel.cs
git commit -m "feat(vm): Display.Paused + StateGlyph for status icons

Add Paused as a first-class Display state (paused/stopped torrents no
longer collapse into Waiting) and populate a StateGlyph string alongside
StateText via the same ApplyDisplay switch. Glyphs are Segoe MDL2 Assets
codepoints; the row's Foreground binding to StatusBrush colours them.
Paused shares StatusIdle brush — only the glyph differs from Waiting."
```

---

### Task 2: MainWindow.xaml — swap TextBlock for FontIcon

**Files:**
- Modify: `src/JTC/MainWindow.xaml:259-261`

- [ ] **Step 1: Replace the state TextBlock with a FontIcon**

Edit `src/JTC/MainWindow.xaml`. Replace this block (currently at lines 259-261):

```xml
                                <TextBlock Grid.Column="6" VerticalAlignment="Center" HorizontalAlignment="Center"
                                           Foreground="{x:Bind RowForeground, Mode=OneWay}"
                                           Text="{x:Bind StateText, Mode=OneWay}" />
```

With:
```xml
                                <FontIcon Grid.Column="6" VerticalAlignment="Center" HorizontalAlignment="Center"
                                          FontFamily="Segoe MDL2 Assets"
                                          FontSize="16"
                                          Glyph="{x:Bind StateGlyph, Mode=OneWay}"
                                          Foreground="{x:Bind StatusBrush, Mode=OneWay}"
                                          ToolTipService.ToolTip="{x:Bind StateText, Mode=OneWay}" />
```

Note: `Foreground` is now bound to `StatusBrush` (the same brush already used for the row's left-edge stripe / circle indicator), NOT `RowForeground`. This gives the icon its per-state colour.

- [ ] **Step 2: Build to verify XAML compiles**

Run:
```
dotnet build src/JTC/JTC.csproj -c Debug -v minimal
```

Expected: 0 errors. The XAML compiler runs at build time — a typo in the `x:Bind` path or the FontIcon attributes would fail here.

- [ ] **Step 3: Launch and eyeball**

Run:
```
Start-Process E:/PROJECTS/JuniorTorrentClient/src/JTC/bin/Debug/net10.0-windows10.0.19041.0/win-x64/JTC.exe
```

Verify:
- Each row's "Состояние" column shows a glyph, not a Russian word.
- Downloading rows show a Download arrow in orange (or whatever StatusDownloadingHex is set to).
- Seeding rows show an Upload arrow.
- Hover any row's icon → the Russian word appears in a tooltip.
- Pause a torrent via the row context menu → the icon flips to the Pause glyph.

Close JTC after checking:
```
Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force
```

- [ ] **Step 4: Commit**

```
cd E:/PROJECTS/JuniorTorrentClient
git add src/JTC/MainWindow.xaml
git commit -m "feat(ui): status glyph in the row «Состояние» column

Replace the per-row status TextBlock with a Segoe MDL2 Assets FontIcon
bound to the new StateGlyph and coloured by StatusBrush. Russian word
moves to the hover tooltip so keyboard/screen-reader users don't lose
the semantic label. Column header 'Состояние' stays as the column name."
```

---

### Task 3: Regenerate gallery screenshots

**Files:**
- Modify (via script): `screenshots/theme-preset-*.png`, `screenshots/theme-dark.png`, `screenshots/theme-light.png`

- [ ] **Step 1: Rebuild Debug so title bar shows v0.6.0**

The screenshot script prefers the Debug binary. The last Debug build was made before the version bump, so `Assembly.GetName().Version` still returns 0.5.9. Rebuild:

```
dotnet build src/JTC/JTC.csproj -c Debug -v minimal
```

Expected: 0 errors. The output DLL now embeds the 0.6.0 assembly version from `JTC.csproj`.

- [ ] **Step 2: Run the screenshot script**

Kill any running JTC first (the script does this internally but a clean state avoids the initial 500 ms sleep tripping over stale processes):

```
Get-Process -Name JTC -EA SilentlyContinue | Stop-Process -Force
```

Then:

```
& 'E:/PROJECTS/JuniorTorrentClient/tools/take-screenshots.ps1'
```

Expected: `Done. Captured 12 / 12 screenshots.` (takes ~2 minutes — 12 configs × ~10 s wait for MonoTorrent to attach).

- [ ] **Step 3: Spot-check the pattern via the Read tool**

Read two representative screenshots and confirm:

- `screenshots/theme-preset-01-blue-lime.png` — title bar shows `v0.6.0`, plashkas are square, status indicator is a left-edge stripe, and the "Состояние" column shows glyphs (arrows / hourglass) instead of words.
- `screenshots/theme-preset-02-pink-orange.png` — title bar shows `v0.6.0`, plashkas are rounded capsules, status indicator is a small circle, and the "Состояние" column shows the same glyphs.

If either the version string or the icon rendering is wrong, do NOT commit — fix the underlying issue first.

- [ ] **Step 4: Commit the fresh screenshots**

```
cd E:/PROJECTS/JuniorTorrentClient
git add screenshots/theme-preset-01-blue-lime.png screenshots/theme-preset-02-pink-orange.png screenshots/theme-preset-03-sunset.png screenshots/theme-preset-04-ocean.png screenshots/theme-preset-05-purple-dusk.png screenshots/theme-preset-06-mint.png screenshots/theme-preset-07-forest.png screenshots/theme-preset-08-cyberpunk.png screenshots/theme-preset-09-coffee.png screenshots/theme-preset-10-aurora.png screenshots/theme-dark.png screenshots/theme-light.png
git commit -m "docs: regenerate gallery screenshots for v0.6.0

Fresh captures show the renamed «Юниор» / «Фламинго» presets, the new
status glyphs in the row «Состояние» column, and the intended 50/50
split — nechetnye presets (1,3,5,7,9) + Dark + Light with square
plashkas and left-stripe status indicator, chetnye (2,4,6,8,10) with
rounded capsules and circle indicator."
```

---

## Post-plan: hand off for release

After Task 3 lands, the working tree has three commits on top of `main`:
1. `a967425 feat: rename built-in presets to «Юниор» + «Фламинго» (v0.6.0)`
2. `a954c4b docs: spec — status icons in the row «Состояние» column`
3. Task 1's commit — `feat(vm): Display.Paused + StateGlyph for status icons`
4. Task 2's commit — `feat(ui): status glyph in the row «Состояние» column`
5. Task 3's commit — `docs: regenerate gallery screenshots for v0.6.0`

Since the icons feature grows the release from "cosmetic rename" to "cosmetic rename + new UI element", consider whether the version should bump to **0.7.0** instead of 0.6.0. Ask Aleksey before tagging.

Update `dist/RELEASE_NOTES_0.6.0.md` (or `.../0.7.0.md`) to describe the icon feature alongside the rename, then rebuild the installer + zip:

```
& 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe' 'E:/PROJECTS/JuniorTorrentClient/installer/JTC.iss'
Compress-Archive -Path 'E:/PROJECTS/JuniorTorrentClient/src/JTC/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/*' -DestinationPath 'E:/PROJECTS/JuniorTorrentClient/dist/JTC-v0.6.0-win-x64.zip' -Force
```

(Publish first with `dotnet publish src/JTC/JTC.csproj -c Release -r win-x64 --self-contained true` if the Release output isn't fresh.)

Then wait for Aleksey's local-terminal "go" for `git tag v0.6.0 && git push origin main && git push origin v0.6.0 && ./tools/publish-github-release.ps1 -Version 0.6.0`, followed by the standing policy of deleting all prior GitHub Releases (tags preserved).
