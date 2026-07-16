# Junior Torrent Client (JTC)

Минималистичный BitTorrent-клиент для Windows 11.
Свой pet-проект — только то, что реально нужно, ничего лишнего.

<p align="center">
  <img src="icon/logo.png" width="96" alt="JTC" />
</p>

<p align="center">
  <img src="screenshots/theme-preset-01-blue-lime.png" width="880" alt="Junior Torrent Client — окно приложения" />
</p>

## Что умеет

- Скачивание из `.torrent` файлов и magnet-ссылок
- Список торрентов с прогрессом, скоростями DL/UL, числом пиров и состоянием
- Пауза, возобновление, удаление (с опцией удалить или оставить файлы)
- Лимит одновременных закачек — если он достигнут, лишние торренты стоят в очереди и запускаются автоматически, когда предыдущий завершается
- Ассоциация с `.torrent` файлами в Проводнике: правой кнопкой → **Открыть с помощью → JTC**
- `magnet:` ссылки — регистрируется как обработчик, браузеры отдают ссылки прямо в JTC
- Single-instance: второй запуск (например, из «Открыть с помощью») не плодит второе окно, а передаёт файл в уже открытое приложение
- **Полностью настраиваемая палитра**: градиент окна (верх+низ), фон и цвет текста плашки-строки, все 5 цветов статусов (Ожидание / Загрузка / Раздача / Проверка / Ошибка). См. галерею ниже.
- Пресеты цветов — сохраняй свои палитры целиком (все 9 цветов) и переключай одним кликом. Две встроенных: «Розово-оранжевая», «Сине-зелёная»
- Индикатор состояния — 4 px неоновая полоса на левом краю каждой плашки в цвете статуса
- Прогресс-бар строки автоматически подкрашивается 17 % от цвета статуса (загрузка — оранжевым, проверка — синим, ошибка — красным)
- Полностью русский интерфейс, шрифт Ubuntu
- **Автоматическое обновление** — при активации окна JTC проверяет GitHub Releases и предлагает установить новую версию с показом release-notes
- Тихая переустановка — установщик умеет корректно закрывать запущенный JTC и полностью чистить папку установки (никаких stale-файлов после апдейта)

## Галерея тем

Три темы (**Цветная**, **Чёрная**, **Белая**) переключаются в Настройках. Цветная — с полным контролем каждого цвета (9 полей: градиент верх/низ + плашка фон/текст + пять статусов). В комплекте **10 встроенных пресетов**; можно собрать и сохранить любое своё сочетание.

### 10 встроенных пресетов Цветной темы

<table>
<tr>
<td width="50%"><img src="screenshots/theme-preset-01-blue-lime.png" alt="Сине-зелёная" /></td>
<td width="50%"><img src="screenshots/theme-preset-02-pink-orange.png" alt="Розово-оранжевая" /></td>
</tr>
<tr>
<td align="center"><b>1. Сине-зелёная</b> — тема по умолчанию</td>
<td align="center"><b>2. Розово-оранжевая</b></td>
</tr>
<tr>
<td width="50%"><img src="screenshots/theme-preset-03-sunset.png" alt="Закат" /></td>
<td width="50%"><img src="screenshots/theme-preset-04-ocean.png" alt="Океан" /></td>
</tr>
<tr>
<td align="center"><b>3. Закат</b></td>
<td align="center"><b>4. Океан</b></td>
</tr>
<tr>
<td width="50%"><img src="screenshots/theme-preset-05-purple-dusk.png" alt="Фиолетовый мрак" /></td>
<td width="50%"><img src="screenshots/theme-preset-06-mint.png" alt="Мятная свежесть" /></td>
</tr>
<tr>
<td align="center"><b>5. Фиолетовый мрак</b></td>
<td align="center"><b>6. Мятная свежесть</b></td>
</tr>
<tr>
<td width="50%"><img src="screenshots/theme-preset-07-forest.png" alt="Лес" /></td>
<td width="50%"><img src="screenshots/theme-preset-08-cyberpunk.png" alt="Киберпанк" /></td>
</tr>
<tr>
<td align="center"><b>7. Лес</b></td>
<td align="center"><b>8. Киберпанк</b></td>
</tr>
<tr>
<td width="50%"><img src="screenshots/theme-preset-09-coffee.png" alt="Кофе" /></td>
<td width="50%"><img src="screenshots/theme-preset-10-aurora.png" alt="Северное сияние" /></td>
</tr>
<tr>
<td align="center"><b>9. Кофе</b></td>
<td align="center"><b>10. Северное сияние</b></td>
</tr>
</table>

### Чёрная и Белая темы

<table>
<tr>
<td width="50%"><img src="screenshots/theme-dark.png" alt="Чёрная тема" /></td>
<td width="50%"><img src="screenshots/theme-light.png" alt="Белая тема" /></td>
</tr>
<tr>
<td align="center"><b>Чёрная</b></td>
<td align="center"><b>Белая</b></td>
</tr>
</table>

### Окно настроек

<table>
<tr>
<td width="50%"><img src="screenshots/settings-colored-pink-orange.png" alt="Настройки — розово-оранжевая" /></td>
<td width="50%"><img src="screenshots/settings-colored-blue-lime.png" alt="Настройки — сине-зелёная" /></td>
</tr>
<tr>
<td align="center">Все девять цветовых swatch'ей — градиент + плашки + статусы</td>
<td align="center">Та же панель, другая тема</td>
</tr>
<tr>
<td width="50%"><img src="screenshots/settings-dark.png" alt="Настройки — чёрная" /></td>
<td width="50%"><img src="screenshots/settings-light.png" alt="Настройки — белая" /></td>
</tr>
<tr>
<td align="center">На <b>Чёрной</b> и <b>Белой</b> темах цветовые опции скрыты (нечего настраивать)</td>
<td align="center"></td>
</tr>
</table>

## Установка

**Готовый билд** (Release, самодостаточный, без установщика):

```powershell
git clone https://github.com/yalyoha/JTC.git
cd JTC
dotnet publish src/JTC -c Release -r win-x64 --self-contained
```

После этого в `src/JTC/bin/Release/net10.0-windows.../win-x64/publish/` появится папка со всеми файлами включая `JTC.exe` — переноси куда хочешь и запускай. Никаких `.msi`, `.msix`, установщиков, реестра HKLM или прав администратора не требуется.

Либо возьми готовый **инсталлятор** из [релизов](https://github.com/yalyoha/JTC/releases) — это Inno Setup, ставит в `%LocalAppData%\Programs\JTC`, добавляет ярлык в Пуск и деинсталлятор в «Программы и компоненты».

**Требования:**
- Windows 10 20H1+ (лучше Windows 11 — там красивее выглядит фон)
- .NET 10 SDK для сборки. Готовый билд уже включает всё что нужно, ставить .NET на конечной машине не надо

## Как пользоваться

1. Запусти `JTC.exe`
2. Первый раз нажми **Настройки** (⚙) → выбери папку для загрузок и лимит одновременных закачек (по умолчанию 3)
3. Дальше просто:
   - **+ Открыть .torrent** — выбор `.torrent` файла
   - **+ Добавить magnet** — вставка `magnet:?...` ссылки
   - Или правая кнопка на `.torrent` в Проводнике → **Открыть с помощью → JTC**
4. Кнопки справа (▶ / ❚❚ / 🗑) действуют на выделенный торрент:
   - **Возобновить** — запустить если стоит на паузе
   - **Пауза** — остановить закачку, соединения останутся
   - **Удалить** — с диалогом «удалить файлы с диска или оставить?»

## Где хранятся данные

Всё лежит в `%LocalAppData%\JTC\`:
- `settings.json` — папка загрузок и лимит закачек
- `torrents.json` — список текущих торрентов
- `cache/` — данные fast-resume от MonoTorrent (позволяет не перехешировать после перезапуска)
- `debug.log` — диагностический лог (ротируется на 1 МБ)
- `inbox/` — временные файлы обмена между экземплярами приложения

Чтобы «начать с нуля» — просто удали эту папку. Если ты запускал предыдущие версии до ребрендинга, старая папка `%LocalAppData%\TClient\` автоматически переименовалась в `JTC\` при первом старте — данные не потерялись.

## Сборка из исходников (Debug)

```powershell
git clone https://github.com/yalyoha/JTC.git
cd JTC
dotnet build -c Release
dotnet run --project src/JTC
```

Тесты:
```powershell
dotnet test
```

## Стек

- **WinUI 3** (Windows App SDK) — GUI, unpackaged режим
- **MonoTorrent** — движок BitTorrent-протокола
- **CommunityToolkit.Mvvm** — MVVM-сокращалки
- **xUnit** — юнит-тесты
- **Inno Setup** — установщик
- **.NET 10** — рантайм

## Лицензия

Личный проект, распространяется как есть. Пиши, если хочешь что-то добавить или нашёл баг — issues и PR приветствуются.
