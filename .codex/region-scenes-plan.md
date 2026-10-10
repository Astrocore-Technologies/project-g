# Project G — от вертикального среза к полноценной игре

Статус: **направление R0 принято; R1/R2 реализованы, ожидают проверки пользователя. Рельеф и общие подземелья приняты 2026-10-10, runtime ещё плоский**.
Подготовлен 2026-10-09 по [концепту регионов](https://app.notion.com/p/3f38867306b181b59c0cf8aa6df42dca).
Архитектура: [ADR 0012](../docs/architecture/0012-scene-authored-regions.md); рельеф и общие подземелья — [ADR 0014](../docs/architecture/0014-terrain-and-shared-dungeons.md).
Gameplay: [зафиксированный контракт](../docs/design/gameplay-contract.md).
История прототипа: [основной план](plan.md), этапы 0–18.

## Порядок работы

**Один этап → автоматические проверки → проверка пользователя → явная приёмка → следующий этап.**

Документация, реализация, автоматическая проверка и пользовательская приёмка — разные статусы.
Готовность прототипа не означает готовность production-систем. Непринятые вручную
этапы прошлого плана сохраняют свой статус; этот документ не принимает их задним числом.
В начале каждого этапа уточняем затронутые файлы и критерий, в конце записываем
фактические проверки и короткую инструкцию для пользователя. Не обновляем AGENTS
ради очередной функции. Новые постоянные gameplay-правила сначала согласуем.

## Что сохраняем и что меняем

Сохраняем серверную authority, fixed tick 20 Hz, LiteNetLib, Client/Server/Shared/Database,
runtime IDs отдельно от persistent IDs, AOI, prediction/reconciliation, игровые данные,
транзакции и существующие возможности прототипа. SQLite остаётся базой Development;
PostgreSQL-адаптер не удаляем. Два существующих региона `prototype` и `outskirts`
сначала работают в одном процессе и не становятся копиями/каналами.

Меняем способ создания локаций: Godot scenes вместо программного строительства
terrain. Сервер получает экспортированную геометрию, навигацию и stable placements,
но не загружает Godot. Клиент управляет загрузкой сцен и отображает серверный state.
Runtime actors/VFX могут создаваться кодом из подготовленных сцен — это не authoring карты.

Не переносим всё в ECS или отдельные сервисы ради рефакторинга. Не переписываем
работающие combat/economy/social системы одновременно с pipeline регионов.

## Общая архитектура и ограничения

| Область | Ответственность |
| --- | --- |
| Client | GameRoot, region scenes, editor markers/exporter, scene loader, presentation, input/UI |
| Server | RegionCatalog, simulation, travel coordinator, eligibility, dynamic world state |
| Shared | Bounded messages, версии, IDs, общая movement/nav математика без Godot и секретов |
| Database | Versioned persistence, leases, транзакции, миграции и recovery |
| Tests | Server/Shared contracts, export fixtures, UDP, миграции и повторяемые Godot smoke checks |

Точное размещение предлагаемых файлов и ownership приведено в ADR 0012.
Формат контента версионируется отдельно от wire protocol и mutable world state.
Экспорт deterministic; производные файлы не становятся вторым ручным источником карты.
Секретные условия/названия не попадают в клиентский manifest или PCK. Статическую
клиентскую геометрию нельзя считать защищённой от изучения файлов.

Начальные demo scenes ограничены существующей плоской сеткой до 1024 клеток.
Это не архитектура больших регионов: production navigation и много-региональный fog
потребуют отдельного решения и миграции. Не снимаем bounds простым увеличением массива.
Загрузка клиента не останавливает общий server tick и не удерживает SQL transaction.
Переход переносит владельца персонажа ровно один раз, с ограниченными tickets/таймаутами.
На Mobile учитываем transient память старой и новой сцены, instantiate/render stalls,
видимость с высокой камеры и стоимость динамических объектов.

## Решения перед реализацией

| Решение | Содержание | Требуется |
| --- | --- | --- |
| D1 | Scenes + offline export; две demo scenes, один процесс | До R1 |
| D2 | Плоская текущая сетка только для demo; крупная navigation после spike | До R2 |
| D3 | Поведение travel при combat tag и задержке загрузки | До R4 |
| D4 | Наполнение/масштаб/арт первого полноценного региона | До G1 |
| D5 | Рельеф внутри региона; подземелья/глубокие уровни — общие регионы через физические входы; несколько поверхностей для мостов | Направление принято 2026-10-10 в ADR 0014; backend определяется в R7 |

D3 принят пользователем 2026-10-10: пока действует combat tag, запрещён переход
в любой другой регион, включая подземелье. При reconnect загрузка не снимает
tag и не меняет прежнюю уязвимость. В интеграционном срезе geometry-ready,
baseline-applied и activation происходят после durable destination commit;
preload до commit и полный R4 остаются отдельной работой.

## Переход на сцены: R0–R8

### R0. Зафиксировать границы перехода

Цель: принять направление, не начинать большой refactor вслепую.

- Сверить ADR 0012 с gameplay contract; согласовать D1 и рамки D2.
- Зафиксировать текущий набор функций двух регионов, IDs и контрольные сохранения.
- Отметить открытые D3/D4, версии protocol/content/SQL и ограничения navigation/fog.
- Разделить результаты старых тестов и будущую проверку нового pipeline.

Результат: согласованная архитектура и проверяемый список того, что нельзя потерять.
Приёмка: пользователь принимает направление и первый implementation-этап R1.
Никакой production migration, смены combat или удаления старых сцен на R0.

### R1. Сценовый каркас и authoring contract

Цель: локации можно собирать и проверять в редакторе.

- Описать coordinate frame, допустимые shapes/transforms, stable IDs и типы markers.
- Создать persistent GameRoot и независимые region scenes двух demo-регионов.
- Добавить минимальные RegionRoot/Entry/Gate/Spawn/Interaction/NavPatch markers.
- Разделить Environment, public state bindings и authoring anchors.
- Предусмотреть локальный preview без сетевого gameplay и симметричный lifecycle.

Затрагиваем Client scenes/editor scripts и authoring schema; не меняем wire/SQL.
Не трогаем пользовательские сторонние addons. Не прячем серверные правила в scripts сцен.
Проверки: Godot import/build, открытие обеих сцен, unique IDs, корректные transforms,
создание/удаление preview без утечек подписок и runtime actors.
Приёмка: пользователь двигает объект в редакторе, сохраняет сцену и видит изменение
в preview без правки кода строительства уровня. Gameplay пока остаётся прежним.

### R2. Экспорт и проверка серверных данных

Цель: одна authored геометрия даёт воспроизводимый server package.

- Один exporter для editor и headless запуска; deterministic sorting/quantization/hash.
- Экспорт текущей bounded grid, obstacles, entries, gates и stable placements.
- Server-only RegionRules отдельно от public client manifest.
- Проверка ссылок между регионами, walkable arrivals, IDs, bounds, shapes и revisions.
- Атомарная публикация согласованного набора; dirty/stale export обнаруживается до запуска.
- Server RegionCatalog загружает результат без зависимости от Godot/Client.

Wire пока не меняем; persistence keys сохраняем. Уточняем CLI exporter после его создания,
а не записываем несуществующую команду как рабочую.
Проверки: повторный экспорт даёт одинаковый результат; изменение blocker меняет geometry
hash; duplicate ID, сломанная arrival/reference и unsupported collider дают понятную ошибку.
Проверяем фактические client export/PCK на server-only metadata.
Приёмка: передвинутый в Godot blocker меняет серверную проходимость после экспорта;
ошибочная карта не запускается и не заменяет предыдущий валидный package.

### R3. Перенести две существующие локации в сцены

Цель: scene-equivalent замена без расширения игровых правил.

- Перенести terrain/decorations, gates, NPC/resource/market/craft placements.
- Сохранить RegionId, persistent object keys, bounds и известные точки входа.
- Создать RegionSceneLoader и bindings для state/actors/camera/UI.
- Подготовленные actor/prop scenes заменяют процедурные visuals там, где это нужно.
- Удалять старые builders только после проверки эквивалентности; без второго источника geometry.

R3 проверяет preview и соответствие server export. Полноценная сетевая активация новой
сцены зависит от R4; не объявляем переход безопасным по одному успешному local load.
Проверки: обе сцены открываются, placements совпадают с exports, не дублируются static
и server actors; state bindings выдерживают повторное создание/освобождение сцены.
Приёмка: обе локации редактируются как сцены; прежние игровые объекты и UI не потеряны.

### R4. Надёжная загрузка и активация региона

Цель: персонаж не управляется до готовности destination scene и baseline.
D3 выбран: travel при combat tag запрещён.

- Versioned Prepare → SceneReady → durable Commit → baseline → RegionApplied → Activate.
- Preload не замораживает исходного персонажа; eligibility повторно проверяется перед commit.
- Короткое transactional переключение ownership, неактивный destination до подтверждения.
- Bounded ticket/generation/epoch, таймауты, quotas, hash mismatch и ясные ошибки.
- Initial login/reconnect используют тот же activation barrier.
- Старые packets/snapshots не меняют новую сцену; нет принятых movement до activation.
- После durable commit ошибка ведёт к recovery в destination, не к техническому rollback.

Затрагиваем Client/Server/Shared, повышаем protocol version. SQL locks не ждут клиента.
Проверки: round-trip/malformed/replay, disconnect до/после commit, missed readiness,
late acknowledgement, повторный travel, shutdown, два клиента при 100–150 мс и loss/reorder.
Приёмка: переход с реальной загрузкой сцены, camera/UI и actors работает без дублей,
падения сквозь terrain и потери персонажа; искусственно медленный клиент не тормозит остальных.

### R5. Dynamic state поверх authored сцен

Цель: сервер меняет мир, а клиент согласованно показывает его состояние.

- Stable bindings для мостов/проходов, resources и interactive objects.
- Base geometry отдельно от разрешённых patches и mutable state/revision.
- Baseline включает актуальную проходимость и visual state до activation.
- Revision gap вызывает bounded resync; поздний join видит текущее состояние.
- Live-DM адресует RegionId/разрешённые patches, а не произвольный клиентский terrain.
- Первым переносим существующее открытие прохода. Закрытие занятого прохода требует
  отдельного gameplay-правила и не добавляется автоматически.

Протокол/SQL меняем только при необходимости после аудита нынешних state records.
Проверки: patch persistence, два клиента, AOI reentry, restart, отсутствие divergent
visual/nav state; прежние OpeningCells переводятся через проверяемый export mapping.
Приёмка: изменение мира одинаково видно обоим клиентам, влияет на server movement
и переживает reconnect/restart без восстановления старой геометрии.

### R6. Совместимость сохранений и сквозная приёмка

Цель: смена authoring не уничтожает достигнутый вертикальный срез.

- Проверить старые персонажи/leases/регион/инвентарь/Echo/progression/fog/dropped items.
- Content pins и migration report: removed/moved IDs обнаруживаются явно.
- При несовместимости сохранения понятная остановка/repair policy, не новый пустой персонаж.
- Fault injection вокруг commit/activation; повторный вход и shutdown fail closed.
- SQLite integration обязательна; PG parity запускается при доступной среде, иначе явно pending.
- Ручной regression checklist старых функций, включая торговлю, craft, смерть/оживление
  и разрешённые условия перехода; исторические проверки не подменяют новую приёмку.

Миграции сначала на копии fixtures/БД. Working database не сбрасываем.
Проверки: idempotence, rollback до commit, durable destination после commit, no duplicates,
content mismatch, clean build/tests/validation и Godot network smoke.
Приёмка: пользователь проходит прежний игровой цикл через обе сцены и перезапускает
сервер без потерь. После R6 pipeline малых регионов готов, масштабирование ещё не доказано.

### R7. Исследовать navigation большого региона
Статус первого блока, 2026-10-10: тестовая сцена и оба .NET кандидата реализованы;
41 navigation test, headless bake и графический preview прошли. Проверены склон,
впадина, мост/нижний проход, радиус прохода, обрыв, patch и stale revision.
[Отчёт и F6 preview](../docs/development/navigation-study.md),
[предложение ADR 0015](../docs/architecture/0015-navigation-study-recommendation.md).
Рекомендуется polygon backend. Приёмка, окончательные limits, большая связная
карта, live bandwidth и Mobile замеры ещё впереди; весь R7 не закрыт.

Цель: выбрать backend по измерениям, не расширять demo grid вслепую.

- Подготовить representative blockout, длинные маршруты, узкие проходы и dynamic patch.
- Обязательные fixtures ADR 0014: холм со склоном, впадина, обрыв и мост с двумя поверхностями на одинаковых X/Z. Сравнение только плоских карт недостаточно.
- Сравнить bounded tiled grid + межтайловый граф с offline exported polygon navigation.
- Измерить export size, query CPU/allocations, snapshot/nav bandwidth и память клиента.
- Проверить radius, unreachable goals, prediction parity и stale revisions.
- На основе принятого ADR 0014 определить coordinate/surface identity, bounded height queries, connectivity, clearance и отдельные combat LOS queries до вертикального контента.
- Оформить отдельный ADR с выбранным вариантом, limits и migration strategy.

Результат — измерения и выбор, не production rewrite. Приёмка: пользователь принимает
backend и ограничения. Если backend не подходит, корректируем исследование, не выпускаем
крупную карту на непроверенной навигации. 200/400 игроков пока остаются целями, не гарантией.

### R8. Реализовать выбранную navigation и региональную exploration

Цель: снять конкретные подтверждённые ограничения до создания больших карт.

- Реализовать принятый R7 backend, chunk/tile budgets и bounded delivery/cache.
- Согласовать client prediction с server route/collision semantics.
- Первый vertical slice ADR 0014: клик на поверхность, подъём/спуск по склону, два клиента и NPC, snapshots/reconnect. Затем высотные боевые проверки и мост/нижний проход без ударов сквозь перекрытие.
- До публикации вертикальных карт согласованно обновить координаты actors/items/placements и relevant wire/save models; не подменять server height клиентским terrain sampling.
- Выделить versioned exploration по character/region/map revision, paged хранение.
- Убрать ограничение одной OtherExplorations через явную миграцию, не безлимитный DTO.
- Перенести legacy fog/navigation на известных mappings; неизвестные revisions fail closed.
- Протокол, SQL и content schemas обновляются согласованно, rollback описан до публикации.

Проверки: границы tiles/regions, malformed sizes, memory pressure, many-region reconnect,
миграция копии старых saves, сеть с loss/reorder и длительный movement soak.
Приёмка: несколько регионов сохраняют exploration; representative большая карта
проходима без зависаний и неприемлемых stalls. Если нужен только малый контент,
G1 blockout допустим параллельно как preview, но production большой карты ждёт R8.

## Полноценная игра: G1–G7

Эти этапы — порядок доработки, а не разрешение одновременно добавить все функции.
Перед каждым составляем точный небольшой backlog из gameplay contract и актуального
концепта. Уже работающую систему углубляем, а не реализуем второй раз.

### G1. Первый полноценный игровой регион

После R6 можно утверждать D4 и делать blockout; большой playable region зависит от R8.
Определить маршруты, landmarks, high-angle читаемость, плотность NPC/resources/events,
safe/опасные территории и связь с соседним регионом. Затем art pass и collision/nav pass.
Без выдуманного лора и автоматического превращения примеров Notion в канон.
Проверка: полный цикл исследование → бой → сбор → craft/рынок → progression → переход
→ возвращение; ошибки placements и geometry проходят exporter validation.
Приёмка: пользователь проходит наполненную локацию без debug-команд и ручного ремонта.

### G2. Клиентский игровой интерфейс и представление

Сценовые UI/actor composition, camera, readable animation/VFX/audio, loading/errors,
tooltips и согласованное отображение inventory/equipment/skills/Echo/progression/social.
Не теряем функции prototype panels. RMB movement и LMB manual attack сохраняются;
не добавляем auto attack/автоподход незаметно при переделке input.
Проверка: reconnect и scene switch не ломают UI/subscriptions, разные разрешения,
Mobile renderer, память и frame stalls; права на assets проверены.
Приёмка: основной игровой цикл понятен без чтения server logs; loading показывает
состояние и восстановление, а не оставляет пустой экран.

### G3. Боевой цикл, NPC и Echo

Доработать текущие telegraphs, hit feedback, AI/leash, encounters, навыки/statuses,
death/revive и lag tolerance. Ровно текущая server authority и manual combat.
Балансировать по данным и playtests; шесть base stats неотрицательны, derived stats
сохраняют утверждённые signed semantics. До 3 Echo — действующий контракт.
Полный Echo resonance из прежнего этапа 19 — отдельный согласуемый подэтап,
не автоматическая часть технического переезда.
Проверка: stat/effect boundaries, cooldown/resource/replay, AI spatial queries,
несколько игроков, AOI reentry, latency/loss; клиент не назначает damage или HP.
Приёмка: один законченный encounter демонстрирует читаемость, управление и роль Echo.

### G4. Progression, профессии и экономика

Углубить существующие progression/hidden professions, items/equipment/crafting,
resources и regional market. Определить темп/стоимость/полезность через согласованный
balance backlog, не изобретать постоянные денежные или monetization правила.
Server-only conditions остаются скрытыми; клиент узнаёт разрешённый результат.
Transactional/auditable/idempotent операции, item ownership и migration не упрощаем
ради UI. Farming/bot abuse проверяется сервером.
Проверка: concurrency/duplicate requests/disconnect/rollback, persistence restart,
secret leaks в messages/PCK, economy audit и end-to-end craft/sale/equip.
Приёмка: progression-loop имеет понятные цели и полезные награды без потерь/дублирования.

### G5. Мир реагирует на игроков

Один законченный world-event с наблюдаемым последствием, regional state, NPC reaction,
существующими party/guild/social и инструментом Live-DM. Затем расширение событий.
Нет личной обязательной main story и gameplay layers. Приватные условия отделены
от public consequences. Авторские variants используют разрешённые scene patches.
Проверка: event restart/recovery, late join, две группы, cross-region references,
permission/audit для Live-DM и отсутствие раскрытия будущих secrets.
Приёмка: игроки видят устойчивое изменение мира от действий, не только toast/квестовый счётчик.

### G6. Надёжность и готовность к закрытому тесту

Согласовать hardware/workload и измеримые CPU/memory/network budgets. Load/soak tests,
AOI и дальний AI, transition spikes, DB contention, backups/restore, metrics/log correlation.
200 normal / 400 burst на регион — проверяемые цели; при провале оптимизируем измеренный
bottleneck, не вводим копии зоны. Worker/process split — отдельный ADR при доказанной нужде.
Перед внешним тестом нужны production identity/auth, права DM, abuse/rate limits,
deployment Staging/Production, PG проверки и процедура восстановления. Конкретный auth
provider/service выбирается отдельно; SQLite Development не выдаём за production rollout.
Приёмка: опубликован capacity report и выполнен drill restart/restore/rollback на Staging;
остаточные риски известны, критические ownership/security проблемы устранены.

### G7. Контентное расширение и выпуск

Расширять соседние регионы через проверенный pipeline, затем PC/Steam closed testing.
CI проверяет schemas/exports/лицензии/совместимость client-server-content; релизный
набор версионирован. Собрать onboarding, accessibility/localization backlog и feedback.
Mobile profile выполняется на реальном согласованном устройстве до обещаний поддержки.
Приёмка: новый регион добавляется без переписывания ядра; игроки проходят build/install,
вход, gameplay, переход и повторный вход. Публичный релиз — отдельное решение пользователя.

## Проверка этапов

| Сценарий | Где проверять |
| --- | --- |
| Export determinism, invalid IDs/routes/shapes, секреты | R1–R3: exporter fixtures + actual client package |
| Нет управления до baseline/activation | R4: server unit + protocol + UDP + Godot |
| Late/duplicate/reordered packets, slow loading | R4/R6: tickets/epochs, loss/reorder integration |
| Disconnect/crash до и после durable commit | R4/R6: fault injection + copied DB recovery |
| Dynamic nav и visual согласованы | R5: server state + два клиента + restart |
| Старый персонаж и мир сохранены | R6/R8: versioned SQLite fixtures, PG parity |
| Большая карта и много регионов | R7/R8: benchmark + bounded network + fog migration |
| Склон/впадина/мост и боевые перекрытия | R7/R8: geometry/path/LOS fixtures, prediction parity, два клиента + NPC + reconnect |
| Открытое подземелье — одна территория | После R4/R6: разные группы через входы видят одни actors/items/world state; возврат и loading recovery |
| Игровой цикл и читаемость | G1–G5: reproducible playtest checklist |
| Capacity/security/операционная готовность | G6/G7: measured load + Staging drills |

Базовые команды будущих code changes (не результаты текущей документационной задачи):

```powershell
dotnet build Game.slnx --artifacts-path .artifacts/region-scenes -m:1
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/region-scenes --no-build
dotnet .artifacts/region-scenes/bin/Content.Server/debug/Content.Server.dll --validate-content
git diff --check
```

Добавляем targeted tests по этапу. Godot editor/headless import, exporter invocation
и scene/UDP smoke запускаем фактически доступным executable; фиксируем точные команды
и результаты при реализации. Не называем пропущенный PG или визуальный тест пройденным.
Нет тестов, которые проверяют только наличие строк в документации вместо поведения.

## Что сейчас не делаем

Маунт и обмен/продажа карт отложены пользователем. Также не нужны на переходе к сценам:
instances/channels, распределённый кластер, корабли, terrain hot reload, arbitrary
runtime map editor, новая monetization или замена engine/network/database foundation.
Разработка полной игры не означает выполнение этих пунктов без отдельного запроса.

## Текущая точка

2026-10-09: пользователь разрешил продолжить после подготовки архитектуры. D1 и
демонстрационные рамки D2 приняты для R1/R2. D3 принят; D4 остаётся открытым.

R1 реализован: offline GameRoot, две independently editable demo scenes, пять типов
markers, stable UUID/структура/bounds/transform validation, preview switching и smoke.
Authoring contract и ручная проверка: [region-authoring.md](../docs/design/region-authoring.md).
Сцены пока примеры каркаса, не эквивалент нынешних server maps; перенос содержимого R3.
Существующая main scene продолжает обслуживать сетевой игровой цикл.

Проверено: `dotnet build Game.slnx -m:1`, затем после добавления smoke
`dotnet build Game.slnx --no-restore -m:1` — 0 ошибок. Остались NU1900 (NuGet audit
недоступен) и существующий NU1903 для SQLitePCLRaw.lib.e_sqlite3 2.1.11.
Godot 4.7.1 .NET headless smoke — exit 0 и `REGION_AUTHORING_SMOKE_OK`: 20 switches,
PackedScene round-trip, duplicate/malformed IDs, bounds/transform, invalid replacement
и освобождение subtree. Запуск через `Start-Process -Wait -WindowStyle Hidden`,
`--headless --path D:/projects/project-g/Content.Client --log-file
D:/projects/project-g/.artifacts/r1-smoke-final.log
res://Tests/Regions/RegionAuthoringSmoke.tscn --quit-after 240`.
Для штатного cache Godot потребовался запуск вне песочницы. Editor import запускался;
при раннем выходе editor дал scan-aborted / HotReloadAssemblyWatcher timer diagnostic,
поэтому его не объявляем полностью чистой проверкой UI. Runtime smoke без ошибок.
`git diff --check` выполнен. Server/Shared tests не запускались: их код и wire/SQL не менялись.
Визуальная приёмка R1 ожидается.

2026-10-10: по разрешению пользователя реализован общий экспорт R2 для действующих
RiverLanding/RiverCity scenes. Один editor/headless pipeline создаёт deterministic
server package с geometry/UUID/bindings/source fingerprints; сервер проверяет граф,
ссылки, placements и stale sources до открытия БД/порта. Публикация — атомарная замена
одного JSON после успешной проверки. Публичный PCK собран, открыт и проверен на
authoring nodes/scripts. Старые export entry points вызывают общий инструмент.

Граница этапа: outskirts остаётся legacy-картой; полный release PCK и его подключение
к игровому loader, migration policy, большой navigation backend и loading protocol
не реализованы. Действующий F5 сохраняет прежний способ загрузки authored scenes.
Это не приёмка R3/R4 или full production export.

Проверено: build — 0 ошибок; 65 targeted tests — passed; headless scene export smoke,
побайтно одинаковый повторный export и реальный Godot/UDP smoke с четырьмя переходами
город → пристань → город → окраина → город — passed. Проверки использовали отдельную БД;
тестовый сервер остановлен. Остались существующие NuGet warnings и ручная визуальная
приёмка. Команды, ограничения и чеклист: [region-export.md](../docs/development/region-export.md).

Уточнение 2026-10-10: ручной экспорт больше не нужен перед обычным Development-запуском.
Сервер проверяет пакет до БД/порта, при изменениях вызывает экспортёр и загружает
обновлённый пакет из checkout. Актуальные карты пропускаются, explicit custom package
и standalone остаются без автоэкспорта; validate-content не публикует данные.
Проверены 19 targeted tests и настоящий запуск с устаревшим/актуальным пакетом.


## Интеграционный срез высот — 2026-10-10

По разрешению пользователя выбран offline polygon backend ADR 0015 и прямая
дальность атак в 3D. TerrainTest и DungeonTest подключены к настоящему server
movement, клиентскому prediction/interpolation, combat LOS, сети и сохранениям.
Два клиента входят в один общий dungeon и возвращаются с прежним персонажем и
inventory. Export обновляется автоматически при Development-запуске.
[Проверки и точки настройки](../docs/development/terrain-integration.md).

Это интеграционный срез R8 и части R4: не завершены большая карта, smoothing,
tile/AOI geometry cache, fog по этажам, нагрузка/Mobile и preload до commit.
Текущий этап ожидает пользовательской приёмки, D4 — лора/наполнения.
