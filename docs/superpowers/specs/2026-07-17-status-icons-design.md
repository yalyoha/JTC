# Status icons in the row "Состояние" column (v0.6.0)

## Goal

Replace the Russian status word ("Ожидание" / "Загрузка" / "Раздача" / "Проверка" / "Ошибка" / "Пауза") in every torrent row's rightmost column with a **Segoe Fluent Icons** glyph. The icon inherits the user-picked status colour (StatusIdleHex / StatusDownloadingHex / …) so it lands naturally in every preset without additional configuration.

The Russian word moves to a **tooltip** on hover — no accessibility loss for keyboard/screen-reader users, and the visual language becomes universal.

## Glyph mapping (Segoe Fluent Icons)

| State (Display enum) | Word (tooltip) | Glyph | Hex codepoint |
|---|---|---|---|
| Waiting  | Ожидание | Hourglass | `E823` |
| Downloading | Загрузка | Download  | `E896` |
| Seeding  | Раздача  | Upload    | `E898` |
| Hashing  | Проверка | Sync      | `E895` |
| Error    | Ошибка   | Warning   | `E7BA` |
| Paused   | Пауза    | Pause     | `E769` |

Font family: **`Segoe MDL2 Assets`** — ships with Windows 10 20H1+ (our floor) and Windows 11. All six codepoints exist and render identically on both. `Segoe Fluent Icons` (Win 11 only) has more polished renderings for the same codepoints, but WinUI 3 `FontIcon.FontFamily` does not accept a fallback list, so a single family that works everywhere beats OS-version detection.

Icon size: **16 px** — matches the visual weight of the surrounding row text (Ubuntu font, 14 pt in the current row template).

Icon colour: bound to the row's `StatusBrush` (already user-configurable per state via the settings dialog; no new colour picker needed). Idle-grey shows through for both Waiting and Paused states, which is fine — the glyph itself carries the distinction.

## Paused as a first-class state

Currently `Display` enum has 5 members (Waiting, Downloading, Seeding, Error, Hashing); paused torrents collapse into Waiting via `ComputeDisplay`. To honour Aleksey's request for a dedicated **Пауза** icon:

- Add `Display.Paused` to the private enum in `TorrentViewModel`.
- `ComputeDisplay` maps `TorrentState.Paused` / `TorrentState.Stopped` → `Display.Paused` instead of `Display.Waiting`.
- `Display.Paused` reuses the existing `StatusIdle` colour (no new settings field). Only the glyph differs from Waiting.

The row's left-edge stripe / dot indicator continues to use `StatusIdle` for both Waiting and Paused — no behaviour change there. The distinction is carried entirely by the new glyph column.

## Implementation surface

**`TorrentViewModel.cs`**

- Add `Display.Paused` to the enum.
- Update `ComputeDisplay` mapping.
- Add two new `[ObservableProperty]` fields:
  - `private string _stateGlyph = "";` (Hourglass default matching Waiting)
  - `_stateText` stays, used only for the tooltip binding.
- In `ApplyDisplay`, set `StateGlyph` alongside `StateText`.

**`MainWindow.xaml`** (row template — the DataTemplate for TorrentViewModel inside the ListView)

- Locate the "Состояние" column's `TextBlock` binding `StateText`.
- Replace with:
  ```xml
  <FontIcon FontFamily="Segoe MDL2 Assets"
            Glyph="{x:Bind ViewModel.StateGlyph, Mode=OneWay}"
            Foreground="{x:Bind ViewModel.StatusBrush, Mode=OneWay}"
            FontSize="16"
            HorizontalAlignment="Center"
            ToolTipService.ToolTip="{x:Bind ViewModel.StateText, Mode=OneWay}" />
  ```
- Column header "Состояние" stays — the header names the column, individual rows show the icon.

**No changes** to `AppSettings.cs`, `SettingsStore.cs`, `ThemeHelper.cs`, or `RowBrushes.cs` — the icon plugs into existing colour infrastructure.

## Testing / verification

- Manually launch JTC, load a small torrent, watch the icon flip through Hashing → Downloading → Seeding as the torrent progresses.
- Right-click a downloading row → Pause → verify icon flips to Pause glyph (and back to Download when Resume is picked).
- Force an error (e.g. corrupt file, permissions issue) → verify Warning glyph shows in red.
- Hover any row → verify Russian word appears in tooltip.
- Take screenshots via `tools/take-screenshots.ps1` and confirm the icon renders in all 12 presets (odd = stripe indicator + icon, even = circle indicator + icon).

## Out of scope

- No new user-facing setting (no toggle between icon / text mode). Aleksey's ask was to replace, not to add an option.
- No animation on state transition. Static glyph swap.
- No custom colour picker for Paused. Reuses StatusIdle.
- Font Awesome / Lucide / custom SVGs — rejected in favour of Segoe Fluent Icons per Aleksey's pick.
