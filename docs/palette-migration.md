# Палитра плашек + шрифт Ubuntu — перенос из веб-мокапа

Мокап отсмотрен и одобрен здесь: `E:\PROJECTS\LAV-Server\var\www\frankiemakers\jtc-design\index.php`.
В приложение переезжают **три** вещи:

1. Новая цветовая палитра пяти состояний (BrandPalette в `RowBrushes.cs`).
2. Новый способ рендеринга прогресса: **заливка всей плашки**, а не тонкая полоска сверху.
3. Шрифт **Ubuntu** для всего приложения (Regular / Medium / Bold).

Всё меняется только в Brand-теме. Dark/Light палитры не трогаем — они остаются как есть.

---

## 1. Палитра — что подставлять в `RowBrushes.cs`

Пять состояний × (bg / fill / bgSelected / fillSelected). Значения — из мокапа, `rgba(...)` → `Color.FromArgb(alpha, r, g, b)`.

**Альфа-словарь:**
- `0.30` → `0x4D` (77)
- `0.60` → `0x99` (153)
- `0.90` → `0xE6` (230)

**Состояния (Brand-палитра):**

| Состояние     | RGB base         | bg (α)      | fill (α)    | bgSel (α)   | fillSel (α) |
|---------------|------------------|-------------|-------------|-------------|-------------|
| `Idle`        | 48, 32, 64       | 0x4D (0.30) | 0x4D (0.30) | 0x99 (0.60) | 0x99 (0.60) |
| `Downloading` | 176, 32, 32      | 0x4D (0.30) | 0x99 (0.60) | 0x99 (0.60) | 0xE6 (0.90) |
| `Seeding`     | 95, 173, 86      | 0x4D (0.30) | 0x4D (0.30) | 0x99 (0.60) | 0x99 (0.60) |
| `Hashing`     | 90, 160, 255     | 0x4D (0.30) | 0x99 (0.60) | 0x99 (0.60) | 0xE6 (0.90) |
| `Error`       | 140, 60, 40      | 0x4D (0.30) | 0x99 (0.60) | 0x99 (0.60) | 0xE6 (0.90) |

Особенности:
- **Seeding** и **Idle** имеют `fill == bg` (эти состояния не «набирают» цвет — Seeding уже на 100%, Idle стоит на 0%). Т.е. плашка выглядит однотонной, но структура кода одинаковая.
- **Hashing** сейчас в коде делит палитру с Idle (`_ => IsSelected ? p.IdleSelected : p.Idle`). В мокапе для него отдельный синий тон. **Развести — добавить `Hashing` / `HashingSelected` в `Palette` и отдельный case в `ApplyDisplay`.**

---

## 2. Прогресс-заливка вместо ProgressBar

Сейчас в `MainWindow.xaml` есть `ProgressBar` высотой 2px в верхней строке `Grid.Row=0`. **Убрать целиком.** Прогресс отрисовывается как заливка всей плашки, левая часть — `fill`-цвет, правая — `bg`-цвет, граница ровно на `Progress%`.

### Реализация — через `LinearGradientBrush` с двойными stop-ами

Не нужен ни отдельный overlay-Rectangle, ни ScaleTransform, ни конвертеры. `RowBackground` продолжает быть одним brush, но теперь это градиент с четырьмя stop-ами:

```csharp
private static LinearGradientBrush BuildRowBrush(Color bg, Color fill, double progress)
{
    var p = Math.Clamp(progress / 100.0, 0.0, 1.0);
    var brush = new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint   = new Windows.Foundation.Point(1, 0),
    };
    brush.GradientStops.Add(new GradientStop { Color = fill, Offset = 0 });
    brush.GradientStops.Add(new GradientStop { Color = fill, Offset = p });
    brush.GradientStops.Add(new GradientStop { Color = bg,   Offset = p });
    brush.GradientStops.Add(new GradientStop { Color = bg,   Offset = 1 });
    return brush;
}
```

Два stop-а на одном offset дают чёткую вертикальную границу — визуально это две монотонные полосы, а не градиент. При `progress == 0` вся плашка = `bg`. При `progress == 100` — вся `fill`. Никаких артефактов.

### Куда встроить

В `TorrentViewModel.cs`:

1. Заменить `Brush _rowBackground = RowBrushes.Current.Idle` на `Brush _rowBackground = new SolidColorBrush(Colors.Transparent)` (стартовое значение — брашь пересчитается на первом же `ApplyDisplay`).
2. Сохранять текущий `Display` в поле:
   ```csharp
   private Display _current = Display.Waiting;
   ```
3. Разделить `ApplyDisplay(d)` на две части:
   - Первая ставит `_current` и `StateText`.
   - Вторая — `RebuildRowBackground()` — читает `_current`, `IsSelected`, `Progress`, дёргает нужный `StateColors` из палитры, зовёт `BuildRowBrush`.
4. Подписать `RebuildRowBackground` на изменения `Progress` и `IsSelected`:
   ```csharp
   partial void OnProgressChanged(double value)  => RebuildRowBackground();
   partial void OnIsSelectedChanged(bool value)  => RebuildRowBackground();
   ```
   (`OnIsSelectedChanged` уже есть — заменить его тело на `RebuildRowBackground()`.)
5. В `Refresh()` порядок такой: сначала `Progress = Manager.Progress` (это запустит `OnProgressChanged`), потом `ApplyDisplay(ComputeDisplay(Manager))`. Так брашь пересчитывается ровно один раз в тик даже если и состояние, и прогресс изменились.

### Структура `Palette` в `RowBrushes.cs`

Старый record с 8 полями `SolidColorBrush` разваливается — вместо него компактнее:

```csharp
public sealed record StateColors(Color Bg, Color Fill, Color BgSelected, Color FillSelected);

public sealed record Palette(
    StateColors Seeding,
    StateColors Downloading,
    StateColors Idle,
    StateColors Hashing,   // ← новое: сейчас Hashing использует Idle
    StateColors Error);
```

Значения для BrandPalette:

```csharp
private static readonly Palette BrandPalette = new(
    Seeding:     new(Bg: C(0x4D, 0x5F, 0xAD, 0x56), Fill: C(0x4D, 0x5F, 0xAD, 0x56),
                     BgSelected: C(0x99, 0x5F, 0xAD, 0x56), FillSelected: C(0x99, 0x5F, 0xAD, 0x56)),
    Downloading: new(Bg: C(0x4D, 0xB0, 0x20, 0x20), Fill: C(0x99, 0xB0, 0x20, 0x20),
                     BgSelected: C(0x99, 0xB0, 0x20, 0x20), FillSelected: C(0xE6, 0xB0, 0x20, 0x20)),
    Idle:        new(Bg: C(0x4D, 0x30, 0x20, 0x40), Fill: C(0x4D, 0x30, 0x20, 0x40),
                     BgSelected: C(0x99, 0x30, 0x20, 0x40), FillSelected: C(0x99, 0x30, 0x20, 0x40)),
    Hashing:     new(Bg: C(0x4D, 0x5A, 0xA0, 0xFF), Fill: C(0x99, 0x5A, 0xA0, 0xFF),
                     BgSelected: C(0x99, 0x5A, 0xA0, 0xFF), FillSelected: C(0xE6, 0x5A, 0xA0, 0xFF)),
    Error:       new(Bg: C(0x4D, 0x8C, 0x3C, 0x28), Fill: C(0x99, 0x8C, 0x3C, 0x28),
                     BgSelected: C(0x99, 0x8C, 0x3C, 0x28), FillSelected: C(0xE6, 0x8C, 0x3C, 0x28)));

private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
```

Dark/Light палитры оставляем со старой структурой, но их надо привести к тому же `StateColors`-record'у, чтобы `TorrentViewModel` работал одинаково. Простой перенос: старая `Seeding` = новый `StateColors.Bg`, старая `SeedingSelected` = новый `StateColors.BgSelected`, `Fill` = `Bg`, `FillSelected` = `BgSelected` (тогда в Dark/Light заливка не будет отличаться от фона — как сейчас, без визуального сюрприза; при желании можно потом развести).

Для Dark/Light `Hashing` = копия `Idle` — сохраняем текущее поведение.

### `MainWindow.xaml` — правки

- Убрать `<ProgressBar ... Grid.Row="0" />`.
- Убрать `Grid.RowDefinitions` внутри плашки (сейчас там `RowDefinition Height="2"` + `RowDefinition Height="*"`), оставить одну строку — контент.
- Внутренний Grid контента больше не нужен как `Grid.Row="1"` — раскрыть в родителя.
- Padding контента: раньше был `8,10,8,12` (лишние 2px снизу компенсировали 2px полоску сверху). Теперь симметрично: `8,10,8,10`.

---

## 3. Шрифт Ubuntu

Файлы **уже скачаны** из репозитория `google/fonts` (Ubuntu Font Licence):

```
E:\PROJECTS\JuniorTorrentClient\src\JTC\Assets\Fonts\
  Ubuntu-Regular.ttf   (344 KB)
  Ubuntu-Medium.ttf    (331 KB)
  Ubuntu-Bold.ttf      (324 KB)
```

### `JTC.csproj` — включить как Content

Добавить в существующий `<ItemGroup>` рядом с `tclient.ico`:

```xml
<Content Include="Assets\Fonts\*.ttf">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

### `App.xaml` — объявить FontFamily-ресурс

Внутри существующего `ResourceDictionary` (после `MergedDictionaries`):

```xml
<FontFamily x:Key="AppFontFamily">ms-appx:///Assets/Fonts/Ubuntu-Regular.ttf#Ubuntu</FontFamily>
```

Формат ссылки: `ms-appx:///путь/к/файлу.ttf#ИмяСемействаВнутриФайла`. Внутри Ubuntu .ttf семейство называется просто `Ubuntu`. Одного Regular-файла достаточно, чтобы задать имя семейства — Medium и Bold WinUI подхватит из тех же `.ttf` (если объявить все три в одном FontFamily через запятую) **или** можно объявить отдельный ресурс на каждый вес.

Более надёжный вариант — все три в одной строке, WinUI сам выберет по FontWeight:

```xml
<FontFamily x:Key="AppFontFamily">
  ms-appx:///Assets/Fonts/Ubuntu-Regular.ttf#Ubuntu,
  ms-appx:///Assets/Fonts/Ubuntu-Medium.ttf#Ubuntu,
  ms-appx:///Assets/Fonts/Ubuntu-Bold.ttf#Ubuntu
</FontFamily>
```

### `MainWindow.xaml` — применить

Добавить `FontFamily` на корневой `Grid`:

```xml
<Grid x:Name="RootGrid" FontFamily="{StaticResource AppFontFamily}">
```

Это унаследуется всеми `TextBlock` / `Button` / `MenuFlyoutItem` внутри окна.

Проверить также диалоги: `ContentDialog`, которые тюнятся через `ThemeHelper.ApplyToDialog`, живут в popup-слое и НЕ наследуют FontFamily от RootGrid. Либо явно ставить `dialog.FontFamily = (FontFamily)Application.Current.Resources["AppFontFamily"];` в `ApplyToDialog`, либо переопределить через ресурсы диалога. Первый вариант короче — добавить одну строку в `ApplyToDialog` перед возвратом на строке 46 (или в самом начале, до всех остальных настроек).

Аналогично для `MenuFlyout` (контекстное меню строки): в `ApplyBrandMenuFlyoutBrushes` добавить
```csharp
res["ContentControlThemeFontFamily"] = (FontFamily)res["AppFontFamily"];
```
Хотя проще — задать `FontFamily` прямо на `<MenuFlyout>` в XAML.

---

## 4. Порядок действий (чек-лист)

1. `RowBrushes.cs` — переписать `Palette` (структура + значения). Заменить BrandPalette. Обновить Dark/Light в новую структуру (Fill=Bg).
2. `TorrentViewModel.cs` — поле `_current`, метод `RebuildRowBackground` + `BuildRowBrush`. Переписать `ApplyDisplay`. Добавить `OnProgressChanged` partial. Поправить `Refresh` порядок (Progress до ApplyDisplay).
3. `MainWindow.xaml` — удалить ProgressBar + внутренние `RowDefinitions`, унифицировать Padding, добавить `FontFamily="{StaticResource AppFontFamily}"` на `RootGrid`.
4. `App.xaml` — добавить `<FontFamily x:Key="AppFontFamily">` с тремя весами Ubuntu.
5. `JTC.csproj` — добавить `<Content Include="Assets\Fonts\*.ttf">`.
6. `ThemeHelper.ApplyToDialog` — прокинуть `dialog.FontFamily` из ресурсов.
7. Собрать, запустить. Прогнать все состояния: Ожидание → Загрузка → Раздача, Проверка (Обновить в контекст-меню), проверить Ошибка (можно временно бросить исключение в MonoTorrent или подождать битую раздачу).
8. Убедиться что в Dark/Light темах ничего не сломалось (плашки должны выглядеть как раньше — Fill=Bg).

## 5. Референсы

- Веб-мокап: `E:\PROJECTS\LAV-Server\var\www\frankiemakers\jtc-design\index.php`
- Скриншот оригинала: `E:\PROJECTS\LAV-Server\var\www\frankiemakers\jtc-design\img.png`
- Текущий код палитры: `src\JTC\Helpers\RowBrushes.cs`
- Текущий рендер строки: `src\JTC\MainWindow.xaml` (ListView.ItemTemplate)
- Текущий VM: `src\JTC\ViewModels\TorrentViewModel.cs`
