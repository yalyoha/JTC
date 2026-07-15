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
| 1 | per-torrent TorrentSettings         | 0.3.29  | v0.3.29-task1     |        | pending |         |
| 2 | диагностическое логирование         | 0.3.30  | v0.3.30-task2     |        | pending | NEEDS HUMAN VERIFY |
| 3 | watchdog авто-восстановления        | 0.3.31  | v0.3.31-task3     |        | pending |         |
| 4 | защита сохранения состояния         | 0.3.32  | v0.3.32-task4     |        | pending |         |
| 5 | кэширование настроек                | 0.3.33  | v0.3.33-task5     |        | pending |         |
| 6 | периодический fast-resume           | 0.3.34  | v0.3.34-task6     |        | pending | NEEDS HUMAN VERIFY |
| 7 | персист выбора файлов               | 0.3.35  | v0.3.35-task7     |        | pending |         |

## Журнал шагов

_(пусто, будет заполнено по мере прогона)_
