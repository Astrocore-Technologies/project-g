# Character & Stats C3 — PvE-смерть и таверна

Дата: 2026-10-20. Срез реализует C3 по GDD 03; C4–C7 остаются впереди.
План и архитектура обновлены. Рабочая БД не открывалась и не мигрировалась.

## Принятые правила и результат

Прямые ответы пользователя: штраф округляется вниз без минимума 1;
новому герою и legacy без привязки назначается стартовая таверна Линвеля;
смена бесплатна у NPC таверны, живым вне боя. Согласованные ранее 5%, 60 с
и 50% HP/MP + 100% Stamina не пересогласовывались.

`DeathPenaltyPolicy.PveLoss` вычисляет `min(CurrentEXP, NextThreshold / 20)`.
При пороге 1000 и EXP 200 теряется 50; при EXP 20 — 20. При 1677 теряется 83;
порог ниже 20 даёт 0. Cap, уровень и очки сохраняются, долга нет.
Политика действует и без PvP-модуля; его собственные штрафы/loot/PK не заменены.
Отрицательная HP regen использует уже подключённую в C2 общую смерть.

`SavedDeathPenalty` v1 — persistent receipt последнего death episode
(DeathId, PvP flag, фактический EXP debit). Повтор события/reconnect/revive не
списывает EXP снова. Старые PvP receipts сохраняют историческую потерю;
уже мёртвое legacy сохранение не штрафуется задним числом.

`SavedRespawnBinding` v1 — stable tavern ID, placement revision, region и
трёхмерная точка. При отсутствии binding назначается configured starting tavern.
Удалённая/перенесённая точка не выбирает случайный fallback: загрузка требует
явной миграции. Оба документа — optional versioned дополнения CharacterState v2;
SavedProgression v1 и SQL schema 12 сохраняются. Необратимой миграции нет.

Услуга проверяет владельца, loaded state, sequence/rate limit, жизнь, In Combat,
3D-дистанцию и LOS. Она меняет только binding, не ресурсы, предметы, EXP или cooldown.
Session watermark сбрасывается при reconnect, сама привязка сохраняется.

Возврат в другой регион использует существующий regional checkpoint/fencing.
Источник замораживается, жизнь/ресурсы/регион и прежний receipt сохраняются вместе;
actor назначения, Accepted и новый epoch публикуются после commit. Ответ Accepted
дополнительно хранится в observer view до первой отправки baseline после загрузки
сцены: очистка tick results во время загрузки не теряет его. При ошибке
staging/commit realm требует recovery из durable state и не сохраняет частичный respawn.
Обычный revive на месте и сохраняемые cooldown/ACR/effects остаются по combat contract.

## Где настраивать и проверять

- `Content.Server/Data/taverns.json` — server-only catalog v1, 1–16 taverns,
  stable IDs, network IDs, revision, region, NPC/Spawn, range до 4 м.
- `Content.Client/Scenes/Regions/Linvel/Services/StartingTavern.tscn` — редактируемые
  markers Npc/Respawn и metadata; `Tools/Respawn/ExportTaverns.gd` экспортирует
  JSON только в явно указанный `--output=...`. Сцены и ручные правки не переписывает.
- `Content.Client/UI/Screens/TavernView.tscn` и `DeathView.tscn` — рабочие окна;
  используют существующие WindowShell и rework-theme. C# связывает данные/действия.
- `Content.Client/Tests/UI/TavernSmoke.tscn` — изолированный live smoke той же
  рабочей сцены: `--server-port=<port> --capture-dir=<existing-directory>`.
  Стенд требует отдельные server content/catalog/SQLite, описанные ниже.

**Размещение временное.** Исполняемый server graph содержит gardens/beech/fox_hills,
городская таверна ещё не построена. Сейчас трактирщик в стартовом лагере gardens:
NPC `(-423,14.1,230)`, Respawn `(-420,14.1,230)`. Это сервис и editable точки,
не готовое городское здание. Перенос в будущую городскую таверну требует новой
placement revision и миграции сохранённых binding. Городская сцена не редактировалась.

Основной маршрут: подойти к трактирщику, F2/клик → «Назначить местом возрождения»
→ дождаться подтверждения. После PvE-смерти окно показывает фактическую потерю EXP,
сохранённую таверну и серверный таймер; после 60 с «Вернуться в таверну».
На неизвестной/занятой/далёкой услуге UI показывает причину отказа.

## Protocol v79

Старый v78 отклоняется handshake. Команда не содержит client-selected coordinates.
Все новые сообщения редкие ReliableOrdered; snapshots не менялись.

| ID | Сообщение | Тело / назначение |
| --- | --- | --- |
| 126 | TavernCommand | 12 B: sequence + runtime NPC ID; клиент → сервер |
| 127 | TavernState | OwnerId/tick, home ID/name/region, last DeathId/loss/PvP; только владельцу, reader budget 256 B |
| 128 | TavernResult | 9 B: sequence/tick/outcome; только владельцу |
| 86 | QuestNpcSpawn | Добавлен float Height; body до 220 B, с type 222 B и regional envelope 232 B; публичные NPC только в AOI |

Полный список таверн и точки возрождения клиенту не передаются. Проверяются
точный размер, bounds/finite, UTF-8 budgets, enum, пустые/нулевые IDs,
truncation/trailing bytes. Серверный catalog проверяет region и navigation surface.

## Проверки

Выполнены:

- `dotnet build Game.slnx --no-restore -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false`
  — успешно, 0 ошибок. Одна промежуточная сборка пересеклась с запущенным UI-server
  и получила Windows DLL lock; после завершения принадлежащего стенду процесса
  последующая полная сборка прошла.
- `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Tavern|FullyQualifiedName~PvpTests|FullyQualifiedName~RegionalTravelTests|FullyQualifiedName~QuestProtocolTests|FullyQualifiedName~CombatLifeTests'`
  — **63 passed, 0 failed, 2 opt-in PostgreSQL skipped**;
  `.artifacts/character-stats/c3-target-final-2.trx`.
- Целевые тесты покрывают rounding/clamp/cap, no-PvP смерть, receipt/reconnect,
  бесплатную услугу, distance/combat/dead/replay/rate limit, reset session,
  binding JSON и content mismatch, 50/50/100 и atomic foreign-region respawn,
  stale world fence rollback, UDP owner-only/commit barrier, задержку загрузки
  target region, доставку Accepted и отклонение old-epoch move/respawn.
- `dotnet Content.Server/bin/Debug/net10.0/Content.Server.dll --validate-content --RegionExports:VerifySources=false`
  — успешно: schema 25, balance 19, 8 weapons, 18 abilities, 5 creatures;
  каталог таверн прошёл region/navigation validation.
- Обычный `--validate-content` запущен и останавливается на отсутствующем
  `Content.Client/Scenes/Regions/Linvel/GardenValleys.tscn`, до проверки таверн.
- `ExportTaverns.gd` реально выполнен в headless Godot: `TAVERN_EXPORT_OK: 1`;
  semantic comparison metadata/NPC/Spawn с production JSON — `TAVERN_SCENE_CATALOG_MATCH`.
- Live `TavernSmoke.tscn` — `TAVERN_SMOKE_OK`: F2, реальный клик назначения,
  подтверждённый home, Escape/modal cleanup, resize 1280/1920, настоящая PvE-смерть,
  ожидание реальных 60 секунд и клик возврата в новую таверну. Логи и PNG:
  `.artifacts/character-stats/c3-ui-8e36e9795ded4d30b1a4e22ac5ccb561/`.
  Рендеры TavernView/DeathView осмотрены: существующие тёмные материалы,
  золотая рамка и serif hierarchy, русские строки/кнопки без clipping при обоих размерах.
  Read-only проверка отдельной SQLite после respawn подтвердила `Dead=false`,
  DeathId=1, binding `preview_forest`, HP 50/100, MP 70/140, Stamina 100/100.

Первый полный serial run: 2034 passed / 1 failed / 7 skipped. Единственное падение —
старое ожидание PvE loss 1 вместо 5, исправлено. Добавленный после этого UDP-тест
выявил потерю Accepted во время region loading; исправление прошло целевые проверки.
Финальный полный serial run: **2036 passed / 0 failed / 7 skipped / 2043 total**,
7 мин 12 с; `.artifacts/character-stats/c3-regression-final.trx`.
Команда: `dotnet test Content.Tests/Content.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=c3-regression-final.trx' --results-directory .artifacts/character-stats`.
Временный output-only `xunit.runner.json` отключал parallel collections и удалён
в `finally`; конфигурация исходных tests не менялась. Все 7 skipped — 6 opt-in
PostgreSQL и проверка копии working development DB без предоставленного fixture.

В live Godot fixture используются отдельная SQLite, случайный UDP port и identity,
две таверны возле beech spawn и отдельная копия content: MaxHP=100, regen HP/MP=0,
test_ground_area имеет fatal health cost 100. Это позволяет вызвать настоящую
PvE-смерть обычной серверной командой; production balance не меняется.
Рендер Vulkan/Mobile на RTX 4060, реальные клики и F2/Escape, 1280×720/1920×1080.

Обычная проверка source scenes ограничена отсутствующими GardenValleys/FoxHills
в параллельной работе над картами; опубликованные surface exports проверяются
с `--RegionExports:VerifySources=false`. Автоэкспорт во время стенда выключен.
PostgreSQL opt-in и проверка копии working development DB не запускаются без их fixtures.
Известные NU1903 (SQLitePCLRaw 2.1.11), NU1900 и Godot interpolation deprecation
остаются отдельными предупреждениями. Пользовательская визуальная приёмка впереди.
