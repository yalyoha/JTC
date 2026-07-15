# Ночной прогон — стабильность и скорость

Ветка: `night/stability-speed` (создана из `main` @ ea686be).
План: [`docs/claude-code-handoff.md`](claude-code-handoff.md).
Стартовое состояние: build ✅ (19 warn, 0 err), tests ✅ 22/22.

**Расхождение со схемой версий:** план указывает «текущая — 0.3.28»,
фактически `<Version>` в `src/JTC/JTC.csproj` = **0.3.27** (installer @ ea686be
поднимался отдельно). Использую следующие patch-номера строго по таблице плана
(0.3.29 = task1 … 0.3.35 = task7), пропуская 0.3.28.

**Заcтэшено перед стартом (принято безопасное решение):**
незакоммиченные изменения на `main`, не относящиеся к плану, спрятаны через
`git stash push -m "pre-night uncommitted work …"`. Файлы:
`installer/JTC.iss`, `src/JTC/App.xaml.cs`, `src/JTC/Helpers/RowBrushes.cs`,
`src/JTC/Helpers/ThemeHelper.cs`, `src/JTC/JTC.csproj`,
`src/JTC/MainWindow.xaml.cs`, `src/JTC/Services/AppSettings.cs`,
`src/JTC/Services/FileAssociation.cs`, `src/JTC/Services/SingleInstance.cs`.
Восстановить: `git stash list` → `git stash pop stash@{0}` (или `stash apply`).

---

## Прогресс по задачам

| # | Задача                              | Версия  | Тег               | Коммит | Статус  | Заметки |
|---|-------------------------------------|---------|-------------------|--------|---------|---------|
| 1 | per-torrent TorrentSettings         | 0.3.29  | v0.3.29-task1     | 0b13246 | done    | build+tests зелёные (22/22) |
| 2 | диагностическое логирование         | 0.3.30  | v0.3.30-task2     | 1998f99 | done    | build+tests зелёные (22/22). NEEDS HUMAN VERIFY (live run) |
| 3 | watchdog авто-восстановления        | 0.3.31  | v0.3.31-task3     | e6e436e | done    | build+tests зелёные (34/34, +12 policy tests) |
| 4 | защита сохранения состояния         | 0.3.32  | v0.3.32-task4     | fffde82 | done    | build+tests зелёные (35/35, +1 parallel-save тест) |
| 5 | кэширование настроек                | 0.3.33  | v0.3.33-task5     | 963d863 | done    | build+tests зелёные (35/35) |
| 6 | периодический fast-resume           | 0.3.34  | v0.3.34-task6     | f2defce | done    | build+tests зелёные (35/35). NEEDS HUMAN VERIFY (kill-during-download). Использована reflection на internal-метод. |
| 7 | персист выбора файлов               | 0.3.35  | v0.3.35-task7     | ef107b3 | done    | build+tests зелёные (37/37, +2 round-trip и legacy-json тесты) |

---

## 🌅 Итоги ночного прогона

**Все 7 задач выполнены, коммит + тег на каждой.** Финальный gate: build ✅
(19 warn, 0 err), tests ✅ 37/37 (стартовые 22 + 12 policy + 1 parallel-save
+ 2 skip-persist).

### Диапазон коммитов
```
0b13246 task 1: per-torrent TorrentSettings on AddAsync (v0.3.29)
1998f99 task 2: periodic diagnostic logging + Error-state cause (v0.3.30)
e6e436e task 3: watchdog auto-recovery from TorrentState.Error (v0.3.31)
fffde82 task 4: harden state save against concurrent writers (v0.3.32)
963d863 task 5: cache AppSettings in memory (v0.3.33)
f2defce task 6: periodic fast-resume flush via reflection (v0.3.34)
ef107b3 task 7: persist DoNotDownload file-selection (v0.3.35)
```

Ветка: `night/stability-speed` (7 коммитов над `ea686be` из `main`).
Все 7 тегов присутствуют: `v0.3.29-task1 … v0.3.35-task7`.

### NEEDS HUMAN VERIFY (требуется живой прогон)
- **Task 2** — расширенное диагностическое логирование. Юнит-тесты не
  покрывают live-метрики; нужно один раз запустить закачку, дождаться >15 с
  и убедиться, что в `%LocalAppData%\JTC\debug.log` появляются строки
  `DIAG '<name>' state=… prog=…% D=… U=… conn=… peers(avail/seeds/leech)=…`.
- **Task 6** — периодический fast-resume. Использована **reflection на
  internal-метод** `TorrentManager.SaveFastResumeAsync()` (публичного API
  в MonoTorrent 3.0.2 нет — это отступление от плана, зафиксированное выше).
  Нужно проверить: запустить крупную закачку, дать ей пройти ≥1 минуту (чтобы
  таймер сработал минимум один раз), убить процесс через диспетчер задач,
  запустить снова — прогресс должен подняться из fast-resume без полного
  recheck. При успехе в новом debug.log должны быть строки `fast-resume: saved N/M torrents`.
  Если после запуска запись `fast-resume: TorrentManager.SaveFastResumeAsync()
  not found via reflection` — MonoTorrent 3.0.2 сменил имя/сигнатуру
  internal-метода (не в этой версии, но возможно после апгрейда).

### Отступления от плана и допущения (принятые безопасно)
1. **Стартовая версия.** План говорил «текущая — 0.3.28», в `.csproj` было
   `0.3.27`. Использовал таблицу плана (0.3.29 = task1) не заполняя 0.3.28.
2. **Pre-night uncommitted work.** На старте на `main` были незакоммиченные
   правки не-plan-файлов; сохранены в stash с описательной меткой (см.
   заголовок этого файла), в ветку не попали.
3. **Task 6: reflection на internal-метод.** MonoTorrent 3.0.2 не имеет
   публичного `TorrentManager.SaveFastResumeAsync`; вызов через
   `MethodInfo.Invoke` с graceful-degradation, если метод пропадёт.

### Утренний чек-лист (для человека)

**1. Обзор коммитов и тегов**
```
git status                              # должно быть чисто, ветка night/stability-speed
git log --oneline main..HEAD            # 7 коммитов task 1..7
git tag -l "v0.3.*-task*"               # 7 тегов
```
Если нужно откатиться к любому шагу:
`git checkout v0.3.31-task3` (например, к состоянию после task 3).

**2. Живая верификация (то, что ночью проверить нельзя)**
- **Task 2 (diagnostic log):** запустить приложение, добавить один торрент,
  подождать 30 с. Открыть `%LocalAppData%\JTC\debug.log` — должны быть
  строки `DIAG '<name>' state=… …` (примерно раз в 10 с).
- **Task 6 (fast-resume после краха):** запустить крупный (>1 GB) торрент,
  дождаться первой записи `fast-resume: saved N/M torrents` в debug.log
  (~45 с после старта), убить процесс через Диспетчер задач, запустить
  снова — прогресс должен появиться сразу, без стадии `Hashing`.
  **Если в debug.log есть `fast-resume: TorrentManager.SaveFastResumeAsync()
  not found via reflection` — MonoTorrent сменил internal API, task 6 не
  работает; либо оставить деградацию до pre-task-6 (сохранение только при
  Stop), либо переработать через `MonoTorrent.Client.FastResume`
  Encode(Stream)/TryLoad(String, out FastResume) вручную.**
- **Task 3 (watchdog):** пример «мягкого» краха — переименовать папку
  загрузки, дождаться перехода в Error → авторестарта через 5 с;
  вернуть имя папки до attempt-5 → должна восстановиться сама. Проверить
  в debug.log строки `watchdog '…': scheduling attempt N/5 in Ns` и
  `watchdog '…': attempt N, StartAsync`.
- **Task 1 (per-torrent):** на одном горячем торренте после ramp-up
  проверить в debug.log `DIAG … conn=…` — должно быстро подходить к
  ~150–200 подключений (раньше упиралось в per-torrent дефолт MonoTorrent).

**3. Замеры скорости «до/после»**
Прогнать 2–3 эталонных торрента (много сидов / средний / magnet). Взять
пик скорости и число пиров из debug.log; сравнить с базовыми значениями
(если не собраны — сделать эти замеры на pre-task-1 состоянии через
`git checkout ea686be`, зафиксировать, вернуться на ветку).

**4. Восстановить pre-night uncommitted work (если он был вашим)**
```
git stash list                          # первым покажется наш stash
git stash pop stash@{0}                 # вернёт installer/theme/main-window правки
```
Если stash не нужен: `git stash drop stash@{0}`.

**5. Merge в main**
После проверки NEEDS HUMAN пунктов:
```
git checkout main
git merge --no-ff night/stability-speed # объединить с preservation истории
# для релиза
# — выставить финальную <Version> в src/JTC/JTC.csproj и installer/JTC.iss
# — заполнить dist/RELEASE_NOTES_*.md
```
Промежуточные версии 0.3.29–0.3.35 были рабочими метками; для реального
релиза стоит перескочить сразу на `0.3.36` (или другое, по политике проекта).

**6. Оценить, стоит ли принять reflection в task 6 или откатить его**
Если в живом прогоне task 6 работает (см. п.2) — оставить как есть, но
завести issue «task 6: заменить reflection на publish-нейтральный API после
MonoTorrent bump/PR». Если ломается — `git revert f2defce` вернёт поведение
до task 6 без сброса остальных 6 задач.

### Стартовая точка
Ветка `night/stability-speed` создана из `main @ ea686be`. Все правки —
только по файлам из списка задач + `<Version>` в `.csproj` + этот прогресс-лог
+ два plan-документа (`docs/claude-code-handoff.md`, `docs/stability-speed-plan.md`),
которые лежали untracked на main и включены в первый коммит.

---

## Журнал шагов (обратный хронологический порядок)

### Task 7 — persist skipFileIndices — done (v0.3.35)
- `PersistedTorrent.SkipFileIndices : int[]?` — nullable для обратной
  совместимости (легаси-записи без поля десериализуются в `null` = «качать всё»).
- В `AddTorrentFileAsync` при сохранении: нормализуем через
  `skipFileIndices.OrderBy(i => i).ToArray()` (стабильный diff в torrents.json).
- В `LoadStateAsync` (файловая ветка): `SkipFileIndices` → `HashSet<int>` →
  тот же параметр `skipFileIndices` у `AddTorrentFileAsync`, который уже
  умеет применять `Priority.DoNotDownload` до `StartAsync`.
- Magnet-ветку оставил без skip — на момент add-a metadata ещё нет, а
  UI-диалог выбора файлов открывается только для .torrent.
- Новые тесты в `StateStoreTests`:
  `SaveAsync_ThenLoadAsync_RoundTripsSkipFileIndices` (0,3,7 → survives)
  и `LoadAsync_LegacyRecordWithoutSkipFileIndices_LoadsWithNull` (JSON без
  поля читается, `SkipFileIndices == null`).
- Gate: build ✅, tests ✅ 37/37. Commit `ef107b3`, тег `v0.3.35-task7`.

### Task 6 — periodic fast-resume flush — done (v0.3.34) — NEEDS HUMAN VERIFY
- **Разобрался с API MonoTorrent 3.0.2:** метод `TorrentManager.SaveFastResumeAsync()`
  существует, но помечен `internal` (нет public-обёртки; XML-доки не описывают
  его; reflection через `MonoTorrent.Client.dll` подтверждает наличие
  compiler-generated `<SaveFastResumeAsync>d__229` state-machine).
- Резолвим `MethodInfo` один раз через `BindingFlags.NonPublic | Instance`,
  кешируем; если не найден (например, будущее переименование MonoTorrent) —
  один раз пишем warning в `debug.log` и таймер становится no-op (деградация
  до pre-task-6 поведения «сохранение только при Stop»).
- Таймер `_fastResumeTimer`, интервал 45 с (в середине диапазона 30–60 из плана;
  учитывает I/O-нагрузку на HDD/SMR). Первый тик через 45 с — до этого нет смысла
  сохранять «пустой» прогресс.
- В тике перебираем `_engine.Torrents`, для `Downloading`/`Seeding` вызываем
  через `MethodInfo.Invoke`, `Task.Wait(TimeSpan.FromSeconds(10))` — чтобы
  медленный flush на SMR не давал наложения тиков.
- Таймер disposed в `DisposeAsync` до `_engine.StopAllAsync`.
- **Отступление от плана:** план предполагал публичный API. Из-за реального
  состояния MonoTorrent 3.0.2 использована reflection. Альтернатива —
  оставить как есть и полагаться на `FastResumeMode.BestEffort`, но по документации
  это гарантий периодичности не даёт.
- Gate: build ✅ (19 warn, 0 err), tests ✅ 35/35. Commit `f2defce`, тег
  `v0.3.34-task6`. **Требуется живой прогон: убить процесс во время закачки,
  запустить снова — прогресс должен подняться из fast-resume без полного recheck.**

### Task 5 — cache AppSettings — done (v0.3.33)
- Поле `_currentSettings = SettingsStore.Load()` (инициализируется при
  создании `TorrentService`), обновляется в `ApplySettingsAsync(settings)`.
- `CanStartMore()` больше не вызывает `SettingsStore.Load()` — читает
  `_currentSettings.MaxSimultaneousDownloads` из памяти.
- Логика очереди не изменилась; юнит-тестов, покрывающих очередь напрямую,
  в проекте нет — вся регрессия ловится через `dotnet test` (35/35).
- Gate: build ✅, tests ✅ 35/35. Commit `963d863`, тег `v0.3.33-task5`.

### Task 4 — safe state save against races — done (v0.3.32)
- `StateStore`: добавлен instance-`SemaphoreSlim`, уникальный tmp через
  `Path.GetRandomFileName() + ".tmp"`, cleanup в catch (не мусорим tmp
  при падении сериализации).
- `SettingsStore` (static): `lock (_writeLock)` + аналогичный уникальный tmp.
- Требование про `DebugLog.Error` в `OnManagerStateChanged` уже выполнено
  в задаче 2 — здесь не трогал повторно.
- Новый регресс-тест `SaveAsync_ManyParallelWriters_DoNotThrowAndLeaveValidJson`:
  40 параллельных writers, проверяем что все завершаются без исключений,
  итоговый файл — валидный JSON одного из payload'ов, и в директории нет
  сирот `*.tmp`.
- Gate: build ✅, tests ✅ 35/35. Commit `fffde82`, тег `v0.3.32-task4`.

### Task 3 — watchdog auto-recovery from Error — done (v0.3.31)
- Новый файл `src/JTC/Services/TorrentRestartPolicy.cs` — чистый класс без
  зависимостей от MonoTorrent: `TryReserveNextAttempt`, `RecordSuccess`,
  `MarkFatal`, `IsFatalException` (статический классификатор).
- Backoff: `5s, 15s, 60s, 60s, 60s`; `MaxAttempts = 5`.
- Фатальные ошибки: `UnauthorizedAccessException`, `DirectoryNotFoundException`,
  `IOException` с HResult `0x80070070` (ERROR_DISK_FULL) или содержащая
  "not enough space" / "disk is full".
- В `TorrentService`: словарь `_restartPolicies`, при `NewState == Error`
  запускается `TryRestartAfterErrorAsync(manager)` (fire-and-forget):
  фатал → лог + `MarkFatal`; иначе `Task.Delay(backoff)` → если ещё в Error
  → StartAsync (attempts 1–3) или HashCheckAsync + auto-start (attempts 4–5).
- Успешный переход в скачивание (`!WasDownloading(old) && WasDownloading(new)`)
  сбрасывает счётчик через `RecordSuccess`.
- `RemoveAsync` теперь чистит `_restartPolicies` в finally.
- Новый тест-файл `tests/JTC.Tests/TorrentRestartPolicyTests.cs` — 12 тестов:
  backoff-последовательность, лимит, сброс, отказ после `MarkFatal`,
  классификация фатальности для 6 случаев.
- Gate: build ✅ (19 warn, 0 err), tests ✅ 34/34. Commit `e6e436e`, тег
  `v0.3.31-task3`.

### Task 2 — extended diagnostic logging — done (v0.3.30) — NEEDS HUMAN VERIFY
- `_diagTimer` (`System.Threading.Timer`) — период 10 с, первый тик через 15 с.
- Каждый тик: снимок `_engine.Torrents`, для каждого не-Stopped/Paused
  торрента в `debug.log` строка `DIAG '<name>' state=… prog=…%
  D=… U=… conn=… peers(avail/seeds/leech)=…/…/… trackers=OK/N dht=…`.
- Все property-доступы обёрнуты `try { … } catch { }` — гонки MonoTorrent
  при teardown не роняют весь тик.
- API-открытия по MonoTorrent 3.0.2 (сверено через reflection на
  `MonoTorrent.Client.dll`): свойство называется `_engine.Dht` (не
  `DhtEngine` как в XML-докax); `manager.Peers.{Available,Seeds,Leechs}`,
  `manager.OpenConnections`, `manager.TrackerManager.Tiers[i].ActiveTracker.Status`
  (сравниваем с `TrackerState.Ok`).
- `OnManagerStateChanged` теперь при `NewState == Error` логирует
  `manager.Error?.Exception` (тип + сообщение + стек — через существующий
  `DebugLog.Error`), а прежний catch { /* best-effort */ } заменён на
  `DebugLog.Error` (тихое проглатывание больше не маскирует ошибки).
- Таймер утилизируется в `DisposeAsync` до `_engine.StopAllAsync`.
- Gate: build ✅ (19 warn, 0 err), tests ✅ 22/22. Commit `1998f99`, тег
  `v0.3.30-task2`. **Требуется живой прогон закачки для верификации** —
  проверить, что появляются строки `DIAG …` в `%LocalAppData%\JTC\debug.log`.

### Task 1 — per-torrent TorrentSettings — done (v0.3.29)
- Добавлены `PerTorrentMaxConnections=200`, `PerTorrentUploadSlots=16` рядом с
  `MaxPeerConnections=500`, `MaxHalfOpenConnections=50`.
- Введён `BuildTorrentSettings()` (`TorrentSettingsBuilder` →
  `MaximumConnections`, `UploadSlots`, `AllowDht=true`,
  `AllowPeerExchange=true`).
- Передан в обе перегрузки `_engine.AddAsync(..., settings)` (torrent-файл и
  magnet).
- Gate: build ✅ (19 warn, 0 err), tests ✅ 22/22. Commit `0b13246`, тег
  `v0.3.29-task1`.
