# Character & Stats C5a/b — источники и распределение EXP

Дата: 2026-10-20. Реализован первый срез C5; пользовательская приёмка впереди.
C5c и весь GDD 03 не объявляются завершёнными. Рабочая БД не открывалась и
не мигрировалась; проверки используют отдельные SQLite-файлы.

## Принятые уточнения

Пользователь выбрал временные 20 EXP за PvE-победу, 5 EXP за каждые 100 новых
проходимых ячеек, раскрытых при движении, сохранение существующих наград:
craft 2/5, самостоятельное открытие 60, текущее поручение 20 EXP.
Eligibility общего пула: текущий бой, фактический урон, эффективное лечение
или успешный контроль, живой connected/loaded участник того же региона
в пределах 40 м от NPC. Party без вклада не награждается; герой на cap не
разбавляет делитель. Эхо хранит собственную долю на конкретном экземпляре,
без введения уровней. Дроби каждого получателя сохраняются до целого EXP.

Это выбранный стендовый баланс, не финальная калибровка темпа и экономики.
Торговля, новые логистические контракты, каталоги квестов и Live-DM требуют
собственных definitions и антиабуз-правил C5c. Дополнительные stat points
за выдающиеся достижения относятся к C6 и здесь не начисляются.

## Реализация и настройка

- `Content.Server/Data/experience-rewards.json`: version/revision, 20 EXP PvE,
  радиус 40 м, шаг исследования 100 и награда 5. Переопределение файла:
  `--Experience:RewardsPath=...`. Program загружает policy во все регионы
  перед входом персонажей. Domain world без policy не выдаёт боевой EXP.
- `ServerWorld.Experience` получает только подтверждённую смерть NPC. Legacy
  +1 EXP за damage event полностью удалён, как и Character EXP от манекенов;
  skill practice остаётся отдельной системой. Нет GrantEXP RPC или hourly cap.
- Eligibility и делитель считаются после боевого frame. Список берётся из
  contribution ledger этого боя, без сканирования всех игроков мира.
  Для героя используется persistent root credit, включая вклад его спутников;
  участвующий Эхо дополнительно определяется по его собственному source UUID.
  Поэтому owner + один участвующий Эхо получают по одной доле, а не две герою.
  Echo тела должны быть активны, живы, в радиусе, с тем же живым connected owner.
- Source победы — NPC life UUID и death ordinal. Повторное сообщение той же
  смерти не награждается. Новое поражение после revive имеет новый ordinal;
  восстановленный encounter очищает предыдущий вклад. Новый spawn — новая жизнь.
  Return-home тоже очищает ledger. PvP и training targets не создают PvE reward.
- Смена party не переносит и не сбрасывает подтверждённый вклад. Сохраняются
  source/owner identity отложенного projectile. Удалённое тело Эхо не создаёт
  фантомную долю; его подтверждённый вклад сохраняет owner root attribution.
- Все доли одной победы сначала подготавливаются, затем применяются в одном
  frame. Награды всех получателей, NPC respawn receipt и audit входят в общий
  SQLite checkpoint. Ошибка/fence одного получателя не публикует частичный EXP.
- Craft, discovery и quest проходят через общий `ProgressionReward` с source,
  activity, revision и amount. Их durable receipts — craft operation ordinal,
  личный discovery bit в регионе и completed quest state. Следующий валидный
  craft того же рецепта снова награждается; нулевой reward рецепта не грузится.
  Revision этих наград — BalanceVersion каталога; PvE/карта — revision policy.
- Карта хранит `ExperienceCells` вместе с уже известными bits. Считаются лишь
  новые проходимые ячейки при движении; initial reveal и исторические landmarks
  не награждаются. Старые карты не получают ретроактивный EXP. Региональный
  архив сохраняет счётчик, повторное посещение/reconnect не создают второй EXP.

Проверка основного сценария: победить обычного NPC, наблюдать целый EXP героя
в окне персонажа; с участвующим Эхо открыть F3 и проверить его отдельный EXP.
При трёх получателях пул 20 даёт каждому 6 и сохранённые 2/3. Повторить бой:
остатки продолжают накапливаться. При исследовании раскрывать новые ячейки
движением; старый маршрут, стояние и повторный вход награду не создают.

## Persistence и protocol

SQL schema 12, CharacterState v2, SavedProgression и SavedEchoCombat v1 сохранены.
Добавлены optional `SavedExperienceFraction` герою и `Experience`/дробь экземпляру
Эхо. Его capture, recall, recovery и login сохраняют EXP. Нет Echo level curve.
Дробь — точная сокращённая рациональная пара, максимум по 512 десятичных знаков
на numerator/denominator; бюджет документа остаётся 8 KiB. Invalid/oversized
state отклоняется до применения распределения. На cap герой не банковует
целый EXP и дробный остаток. Потеря целого EXP от смерти не пересчитывает доли
других участников.

Новый код читает старые записи без этих полей. Старый strict reader новых полей
не знает: rollback старого бинарника требует совместимого snapshot/явной
обратной миграции, а не молчаливого удаления накопленных долей.

Protocol **v81**: owner-only `EchoRosterPage`, ID **115**, прежний low-frequency
ReliableOrdered. В конце каждой entry после Refusal идут Experience Int64
и отображаемый fractional Double [0,1). Точный rational, критерии eligibility
и журнал боя остаются на сервере. Max page, packet budget, malformed/truncated/
trailing validation сохранены. Старый клиент отклоняется handshake по версии.
Новых packet IDs, отдельных процессов или границ authority нет.

`EchoControls.Roster` добавляет подтверждённый EXP в существующий Details Label
рабочей `Content.Client/UI/Screens/EchoView.tscn`. Иерархия и Theme не менялись.
Hero ProgressionState показывает целую часть; остаток сохранён на сервере.

## Фактическая проверка

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  — успешно, 0 ошибок. Новых compiler warnings нет; остаются прежние NU1903
  SQLitePCLRaw 2.1.11 и NU1900 недоступного NuGet audit endpoint.
- Целевые `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore`
  с фильтром ExperienceReward/ExperiencePersistence/ExperienceRosterProtocol/
  EchoRosterProtocol/ProgressionTests/CraftingPersistence/QuestPersistence/
  StarterZone — **63 passed / 0 failed / 0 skipped**.
  TRX: `.artifacts/character-stats/c5-target-final.trx`.
- Проверены damage/heal/control, overheal и passive party, смена party,
  dead/offline/distant/unloaded/cap, return-home/revive/new spawn, исчезнувшее
  Эхо и released projectile, точные дроби с разными делителями, reconnect,
  исследование blocked/старых ячеек, валидный повторный craft и retry.
- Реальные SQLite проверки: restart сохраняет EXP и смерть NPC; stale recipient
  откатывает все доли и world receipt. Реальный UDP+SQLite проверяет задержку
  commit: до неё клиент не получает доли героя/Эхо, после неё видит обе;
  observer не получает чужие progression/roster. Protocol tests проверяют
  Int64, отрицательные значения, NaN, truncation, trailing bytes и packet budget.
  Дополнительное read-only чтение `character_owned_echoes` после UDP-проверки
  подтвердило сохранённые 10 EXP Эхо в отдельной тестовой SQLite
  `.../sqlite-tests/990251d176fe4726a385b86ce28c086e/characters.db`.
- Полная последовательная регрессия: **2069 passed / 0 failed / 7 skipped**,
  всего 2076, 7 мин 31 с. `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore`
  с временным output-only `xunit.runner.json` (`parallelizeTestCollections=false`);
  конфигурация удалена после запуска. TRX: `.artifacts/character-stats/c5-regression-final.trx`.
  Пропуски: шесть внешних PostgreSQL-сценариев и опциональная проверка копии
  development characters; все локальные EXP-проверки выполнены.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content --RegionExports:VerifySources=false --RegionExports:AutoExport=false`
  — успешно: schema 25, balance 19, 8 weapons, 18 abilities, 5 creatures.
  Проверка использует опубликованные exports. Исходный GardenValleys.tscn
  по-прежнему отсутствует; пользовательские городские сцены не восстанавливались.
- Godot 4.7.1, Vulkan/Mobile, RTX 4060 Laptop: **ECHO_EXPERIENCE_PREVIEW_OK**.
  Автономный adapter запускает рабочий EchoControls и EchoView без подключения:
  EXP 6 + 0.667, resize 1280×720/1920×1080, Escape. Фактические кадры осмотрены,
  новая строка читается и не обрезается; полный редизайн окна сюда не входил.
  Live серверный путь доказан UDP-тестом; ручной бой в полном Godot-клиенте
  в этой проверке не проводился.

Preview: `Content.Client/Tests/UI/EchoExperiencePreview.tscn`. Кадры и лог:
`.artifacts/character-stats/c5-ui-b11f0c1b66304f2abd1675a7af4de35d/`.
Настройка компоновки — рабочая EchoView.tscn и её Theme; подпись EXP привязана
в EchoControls.Roster.cs. Вспомогательный preview не дублирует рабочий экран.

## Осталось

C5c: определения значимых торговых и логистических результатов, конкретные
квестовые/Live-DM награды, антиабуз и соответствующие producers. C6: определения
достижений, область/повторяемость shared и правило world_unique winner. C7:
конечный баланс, темп, измерения и общая приёмка. Эти задачи не заменены
пустыми интерфейсами и не объявлены выполненными.
