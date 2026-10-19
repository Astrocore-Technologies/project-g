# Character & Stats C1 — очки, EXP и распределение

Дата: 2026-10-20. C1 реализован и проверен на изолированных данных;
пользовательская приёмка впереди. Это первый этап [плана GDD 03](../design/character-stats/implementation-plan.md),
не завершение всех требований Character & Stats.

## Что изменилось

- Новые персонажи сохраняют `SavedStatAllocation` v1 внутри существующего
  SavedProgression v1: стартовые семь значений, целочисленные вложения,
  EarnedFromLevels, AchievementBonusPoints. Проверяется инвариант
  `earned = invested + available`, а permanent stats должны совпадать с базой
  и вложениями. Level-up добавляет ровно +5 и в свободный пул, и в учёт выдачи.
- Старые документы без Allocation остаются без угаданной истории вложений.
  Они могут продолжать текущую прогрессию/распределение; полноценный respec
  такой истории потребует явного переноса перед C4. Новые нормальные персонажи
  не теряют информацию для будущего сброса. Sandbox-билд за пределами бюджета
  явно остаётся тестовым, без выдуманного обычного ledger.
- `ExperienceCurve` компилирует пороги один раз при загрузке каталога.
  Power-форма фиксирует степень 1.5, параметры Scale/Offset задаются явно.
  Пороги не нормируются по cap. Чистый `ProgressionRules.Award` обрабатывает
  несколько level-up за один результат и отбрасывает EXP на cap.
- SavedProgression сохраняет server-only ExperienceCurveKey. Ключ не включает
  cap, поэтому его расширение не меняет прежние пороги. Смена параметров
  кривой или попытка загрузить немигрированное legacy-сохранение в power-кривую
  отклоняется с требованием явной миграции, без сброса/пересчёта опыта по догадке.
- Применение из окна персонажа ссылается на owner-bound preview. Сервер
  сравнивает свободные очки, permanent stats, текущие/projected derived stats
  и сам набор вложений. Изменившийся или чужой preview возвращает StalePreview
  без списания; UI обновляет расчёт и предлагает подтвердить заново.
- Предпросмотр не меняет персонажа. Распределение, ledger и permanent stats
  сохраняются существующим atomic checkpoint до публикации результата.
  Частые snapshots, ownership, AOI/LOS и transport concerns не менялись.
- `BalanceTestBuild` теперь проверяет и учитывает WIS в расходе бюджета.
  BalancePreview и presets используют общий `GrantedPoints`.
  `BalanceDocument.ConfigurePowerExperienceCurve` меняет только черновик,
  поддерживает undo/redo; `CharacterExperienceTable` читает настоящие серверные
  пороги. После явного добавления конфигурации A/B доступны через метаданные
  страницы уровней с отметкой экспериментального баланса.

## Баланс и совместимость

Основной content **не включает A=150/B=4 автоматически**: §5 GDD 03 явно
называет эти числа экспериментом. Прежняя линейная конфигурация остаётся
совместимой до отдельного выбора конечных параметров; принятая power-механика
уже реализована и проверена в настоящем runtime. Численные опыты не являются
новыми постоянными gameplay-решениями.

Для отдельной Development-копии content schema 25 можно добавить в `progression`:

```json
"experienceCurve": { "scale": 150, "offset": 4 }
```

Это явно экспериментальная конфигурация. На первом уровне она даёт порог
1677 EXP. Численная конвенция эксперимента — `Math.Round(..., ToEven)`;
она входит в identity кривой и не означает принятие окончательного округления.
Запуск: существующий `--Balance:ContentPath=<изолированная копия>` и отдельная
SQLite, без применения к рабочим сохранениям. Удаление ExperienceCurve возвращает
legacy-форму; сохранения с другой CurveKey всё равно не переинтерпретируются.

Content schema **25** читает также schema 24 без новой power-конфигурации.
Balance revision остаётся **19**. SQL schema **12**, CharacterState v2 и
SavedProgression v1 не менялись; Allocation — собственное versioned optional
дополнение. Старый binary со strict JSON не обязан читать новые расширения:
клиент/сервер обновляются вместе, рабочая БД здесь не мигрировалась.

Int64 wire/model в C1 не вводился: текущий проверяемый диапазон порогов
помещается в существующий int32/1e9 storage budget. Промежуточная сумма награды
считается в long, чтобы multi-level сначала погасил пороги без int32 overflow.
Непредставимый порог отклоняется при загрузке. Это явный технический бюджет,
не новый игровой cap статов. Развитие представления дальше требует версии,
а не молчаливого насыщения.

## Wire v78

IDs 40/41/42/83 сохранены; новые IDs не добавлялись.

| Сообщение | Изменение | Тело / packet / с regional envelope |
| --- | --- | --- |
| ID40 AllocateStats | После sequence/action/statIndex/skillId/slot — PreviewSequence uint32, затем семь int32 STR/AGI/VIT/INT/DEX/LUK/WIS | 41 / 43 / 53 байта |
| ID40 PreviewStats | Прежний порядок без PreviewSequence | 37 / 39 / 49 байт |
| ID40 прочие actions | Прежние 9 байт, PreviewSequence запрещён | 9 / 11 / 21 байт |
| ID42 ProgressionResult | Новый outcome StalePreview = 6 | Прежний размер |
| ID41 / ID83 | Форматы state/preview не менялись | Прежний размер |

Все эти редкие сообщения остаются owner-only ReliableOrdered.
PreviewSequence=0 сохраняет прямое распределение по текущему серверному
состоянию для существующих API/fixtures; игровой presenter всегда подтверждает
последний полученный preview. Это не блокировка снаряжения и не доверие
расчётам клиента. Любое распределение повторно проверяет очки и actor state.
Старый клиент v77 отклоняется handshake; старое тело AllocateStats не читается
как v78. Bounds, truncated/trailing data и неверное использование sequence
проверяются до мутации.

## Реально выполненные проверки

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false` — заключительная
  сборка успешна, 0 ошибок. Существующие NU1903 SQLitePCLRaw 2.1.11 и NU1900
  недоступного audit endpoint остаются. Отдельная попытка сборки во время
  регрессии встретила занятый testhost DLL; заключительная сборка после её
  завершения прошла. Это не ошибка компиляции исходников.
- Целевой набор Progression/StatAllocation/BalanceTools/CharacterPersistence/
  HealthRecovery/FrozenCharacterState — **73 passed**. После последнего патча
  и добавления двух перепроверок — **75 passed**, 0 failed, 0 skipped.
  `c1-final-and-recheck.trx` включает настоящие UDP, SQLite reopen, owner-only,
  publish-after-commit, stale/чужой quote, новый ledger и legacy-совместимость.
- Первый полный прогон: **1992 passed / 2 failed / 7 skipped**. Упали
  `SerializedCharacterRetainsMissingChargesAndOfflineSequentialRecovery`
  (2.799 вместо нижней границы 2.8) и
  `FifteenClientsLoadTheDetailedRegionAndKeepSmallDatagrams` (timeout).
  Оба затем прошли отдельно в составе 75 проверок без одновременного build/render.
  Заключительный полный прогон без параллельных коллекций xUnit —
  **1994 passed / 0 failed / 7 skipped**, всего 2001, около 7 минут:
  `c1-regression-serial.trx`. Временный `xunit.runner.json` создавался только
  в build output и удалён после проверки. Шесть skips — opt-in PostgreSQL,
  один — opt-in копия development-БД. Исходники двух timing-sensitive tests
  не изменялись; полный последовательный прогон тоже включает оба.
- Обычный `--validate-content` остановился на отсутствующем
  `Content.Client/Scenes/Regions/Linvel/GardenValleys.tscn`: в workspace параллельно
  изменяются карты GardenValleys/FoxHills/LinvelCity. Эти правки не исправлялись
  и не откатывались. `--validate-content --RegionExports:VerifySources=false`
  прошёл: Schema=25, Balance=19, 8 weapons, 18 abilities, 5 creatures.
  Это проверка опубликованных exports и content, **не** подтверждение их
  соответствия изменяемым исходным сценам.
- Godot 4.7.1, Vulkan 1.4 / Forward Mobile, RTX 4060: рабочий
  `Tests/UI/PlayerHudSmoke.tscn` подключился к отдельному UDP server v78,
  получил экспериментальный порог 1677, проверил реальные GUI clicks по
  портрету, семь статов, draft/preview/cancel/apply, resize 1280×720/1920×1080
  и cleanup. Результат `PLAYER_HUD_OK`.
  В smoke добавлено движение виртуального курсора перед mouse-down: без него
  hover-based routing игрового ввода поглощал начальный клик. Рабочая
  компоновка сцены этим исправлением не менялась. Старое Terrain3D deprecation
  `instance_reset_physics_interpolation` остаётся предупреждением.
- `git diff --check` по затронутым исходникам — успешно. Рабочие `.data`,
  пользовательские серверы и редактор не открывались/останавливались.
  Live PostgreSQL и opt-in копия development-БД не проверялись.

Артефакты: `.artifacts/character-stats/`; графический прогон
`ui-1f3aac0abd71475a882302f0eb18aff0/` содержит server/client logs,
experimental content, собственную SQLite и кадры. Script стенда
`run-ui.ps1` запускается через **PowerShell 7**, использует новый свободный порт
и останавливает только созданные им процессы. Исходные карты не экспортируются:
стенд явно читает опубликованную геометрию без проверки меняемых источников.

Фактическое окно при серверном предпросмотре двух очков:

![Character Stats C1 — реальный Vulkan/Mobile кадр](../../.artifacts/character-stats/ui-1f3aac0abd71475a882302f0eb18aff0/character-preview-1280.png)

## Как проверить и где настраивать

1. Пересобрать и перезапустить клиент/сервер вместе на v78. Взять отдельный
   чистый Development profile: все семь по 10, свободных 18.
2. K или портрет → «Характеристики». Добавить очки в STR/DEX/WIS, проверить
   current/projected. До «Применить» серверные значения не меняются.
3. «Отменить распределение» сохраняет все очки. После «Применить» изменяются
   только выбранные статы, повтор пакета не списывает второй раз; после
   reconnect значения сохраняются. В тесте весь стартовый пул в WIS даёт 28.
4. Level 30 без достижений имеет 163 очка создания/уровней; весь пул в STR
   даёт 173. Дополнительные +10 тестовых achievement points дают 183 и остаются
   отдельно учтёнными. Сам producer достижений относится к C6.

Настройки — `Content.Server/Data/prototype.json`, `progression`; power-опыты
в изолированной копии или draft Balance.Core. Стартовые статы — creature
`test_adventurer`; текущая сцена — `Content.Client/UI/Screens/CharacterView.tscn`.
Ledger и curve helpers не задают layout окна и не добавляют формулы в клиент.

## Оставшаяся работа

C2: процент ресурсов и signed regen. C3: PvE penalty от порога и выбранная
таверна. C4–C6: полный respec, reward receipts/distribution и достижения.
Текущий clamp ресурсов и старая PvE penalty остаются известными разрывами
следующих этапов, а не объявляются выполненными этим C1. Конечные A/B в
основном балансе не выбраны; миграция исторических вложений не выполнялась.
