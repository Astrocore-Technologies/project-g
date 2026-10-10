# Project G — план разработки

Источник: [концепт](https://app.notion.com/p/MMORPG-3f18867306b180749a02c5ddb12d538a#968e66f9ca3f42e1b86d446afc1719b1). Gameplay-источник истины — `docs/design/gameplay-contract.md`, инженерные правила — `AGENTS.md`, технические решения — принятые ADR.

Порядок работы: **делаем один этап → пользователь проверяет → только после подтверждения идём дальше.** Реализация не означает приёмку.

## Предложение инструментов баланса и админа/ГМ — 2026-10-10

По запросу пользователя подготовлены [архитектура и варианты](../docs/architecture/balance-tools-proposal.md)
и [этапы B0–B5 / G1–G3 с критериями приёмки](../docs/design/balance-tools-plan.md).
После обсуждения пользователь исключил права/регионы/audit из текущего объёма и разрешил
реализацию локальной мастерской: редактор чисел, сравнение билдов, undo/save и отдельная
тестовая площадка. Принятое уточнение — [ADR 0013](../docs/architecture/0013-local-balance-workshop.md),
[запуск и проверка](../docs/development/balance-workshop.md). Админская часть, live reload и
разделение всех content-файлов сейчас не выполняются. План регионов ниже сохраняется.

Уточнение 2026-10-10: по разрешению пользователя добавлены отдельные таблицы прокачки навыков, навигация по профессиям и выбор уровня навыков в тестовом билде. Проверки и ограничения сохранённых персонажей описаны в руководстве мастерской.

UX мастерской 2026-10-10: реализованы дерево категорий и поиск параметров, отдельные страницы обучения/навыков/пассивок/рывка, вкладки инспектора, компактное сравнение и отдельная тестовая площадка. Добавлены избранное/изменённые, возврат поля/карточки, список отличий, массовая правка и вставка уровней, именованные локальные билды и назначение тестируемого навыка на Q. Подробная верификация — в руководстве; ожидается пользовательская приёмка. Баланс и рабочая БД персонажей этим UX-изменением не мигрируются.

## Активный план после вертикального среза

Текущий отдельный запрос: первый blockout «Окраины Речной пристани».
[Сцена, управление и результаты проверки](../docs/design/river-landing-blockout.md).
Blockout подключён к обычному F5: authored collision экспортируется в серверную карту,
используются прежние PlayerAvatar/PlayerController/NavigationMover. Protocol25,
ограниченная flat grid 80×64, migration существующих персонажей при первом входе
с backup SQLite и сохранением прогресса. Проверены копия текущей БД, lossy UDP и
Godot gameplay smoke. Ждём пользовательского обхода; полный R2–R8 и арт не завершены.

Новый источник: [концепт регионов и переходов](https://app.notion.com/p/3f38867306b181b59c0cf8aa6df42dca).
Подготовлены [полный план R0–R8 / G1–G7](region-scenes-plan.md) и
[предложенная архитектура ADR 0012](../docs/architecture/0012-scene-authored-regions.md).
Они описывают дальнейший переход от прототипа к полноценной игре. Направление R0
принято, R1 реализован и ожидает проверки пользователя; D3/D4 остаются открытыми.
История ниже сохраняется.

- R0 — принять границы перехода, решения D1/D2 и список сохраняемых функций.
- R1 — каркас Godot scenes и authoring markers.
- R2 — deterministic offline export и server validation.
- R3 — перенести две существующие локации в сцены без потери функций.
- R4 — preload, versioned readiness/baseline/activation и recovery; предварительно решить D3 о combat travel.
- R5 — серверные dynamic patches и согласованное отображение состояния мира.
- R6 — compatibility/migration и сквозная приёмка с сохранениями.
- R7 — измерить и выбрать navigation большого региона.
- R8 — реализовать выбранную navigation и много-региональную exploration.
- G1–G7 — полноценная локация, UI/presentation, combat/Echo, progression/economy,
  реагирующий мир, нагрузка/надёжность и выпуск контента.

Текущий шаг — игровая приёмка пристани: запустить обновлённый сервер и F5.
F6 у RiverLandingBlockout остаётся инспектором компоновки, не игровым запуском.
После приёмки — R2 (offline export). Ни один новый этап не считается
проверенным по результатам старого прототипа. Маунт и обмен картами остаются отложенными.
Старые пункты 19/20 не запускаются автоматически: их дальнейшая работа согласуется
в рамках нового плана. Подробные результаты и критерии приёмки — в linked roadmap.

## Обновлённый план

### Прототип

**0. Технический фундамент — принят**

Запуск и остановка сервера, версии протокола, handshake, конфигурации, диагностика и тесты.

**1. Авторитетное перемещение — принят пользователем**

Prediction, reconciliation, interpolation, последовательности команд, disconnect и сетевые помехи.

Проверка: два клиента плавно перемещаются при задержке 100–150 мс; потеря и перестановка пакетов не ломают состояние. Текущее переподключение создаёт новую сессию, восстановление персонажа появится позже.

**2. Серверные сущности и область видимости — принят пользователем**

Жизненный цикл сущностей, пространственный индекс, AOI, появление и удаление объектов у клиента. Убираем ограничение «первые 32 сущности в snapshot».

Проверка: клиент получает ближайшие релевантные сущности, а не весь регион; пересечение границы видимости работает без дублей.

Реализовано: пространственная сетка, AOI с гистерезисом, reliable spawn/despawn и независимые snapshot-пакеты максимум по 32 сущности. Протокол v3; spawn содержит серверный tick для защиты от старых snapshots. Движение — ПКМ. Подробности: `docs/architecture/0003-spatial-interest.md`.

Проверено после объединения тестов: `dotnet build Game.slnx -m:1` — 0 ошибок; предупреждение NU1900: NuGet не смог получить данные об уязвимостях пакетов из-за недоступности источника. `dotnet test Content.Tests/Content.Tests.csproj --no-build` — 33 теста (18 серверных и 15 shared). Тесты находятся в `Content.Tests/Server` и `Content.Tests/Shared`, общий проект — `Content.Tests/Content.Tests.csproj`. Три сетевых integration-теста проходят с задержкой 100–150 мс и потерей 10% пакетов. Тесты запускались вне песочницы: внутри testhost не устанавливал локальное соединение. Godot-сцена не запускалась — исполняемый файл не найден в PATH.

Приёмка: перезапустить сервер и два клиента с протоколом v3; двигаться ПКМ, разойтись дальше ExitRadius (14), затем сблизиться до Radius (12). Проверить исчезновение/повторное появление удалённого персонажа без дублей, сохранение собственного персонажа и камеры, плавность при сетевых помехах.

**3. Навигация и препятствия — принят пользователем**

Серверные данные проходимости, построение маршрута, столкновения, согласованное предсказание движения.

Проверка: персонаж обходит стену; поддельная команда не позволяет пройти сквозь неё.

Реализовано: ограниченная сетка проходимости, детерминированный A*, radius-aware collision sweep, серверные маршруты и общие правила prediction/reconciliation. Геометрия и радиус персонажа передаются сервером; тестовая стена строится из этих данных. Протокол v4: RegionNavigation (14) перед spawn; snapshots содержат подтверждённую точку назначения. Подробности: `docs/architecture/0004-navigation-obstacles.md`.

Проверено: `dotnet build Game.slnx -m:1` — 0 ошибок, предупреждение NU1900 из-за недоступности NuGet-аудита. `dotnet test Content.Tests/Content.Tests.csproj --no-build` — 54 теста прошли, включая UDP-обход стены при задержке 100–150 мс / потере 10% пакетов и проверку нулевых выделений памяти в прогретом цикле повторных целей/движения. Тесты запущены вне песочницы для локальных сокетов. Godot-сцена не запускалась: executable не найден в PATH; визуальная приёмка остаётся за пользователем.

Приёмка: перезапустить сервер и два клиента с протоколом v4. ПКМ за центральную стену — персонаж должен обойти её; ПКМ внутрь стены — прежнее движение не должно меняться. Проверить движение вдоль углов и краёв зоны, смену направления и коррекции при задержке 100–150 мс / потере 10% пакетов.

**4. Контентные данные и характеристики — принят пользователем**

Версионированные определения оружия, способностей и существ. Шесть неотрицательных базовых характеристик; производные статы могут быть отрицательными, кроме HP. Безопасная математика для знаковых производных и больших значений.

Проверка: ошибочные данные обнаруживаются при загрузке; формулы не создают NaN, бесконечность или деление на ноль.

Реализовано: серверный JSON-каталог schemaVersion 1 / balanceVersion 1, immutable definitions и проверка ссылок/диапазонов/неизвестных полей. Отдельный StatCalculator и знаковые модификаторы производных статов. Ragnarok-подобные влияния согласованы, коэффициенты временные. Сетевой протокол v4 и клиент не менялись. Подробности и формулы: `docs/design/stats-and-content.md`.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage4 -m:1` — 0 ошибок, NU1900 (недоступен NuGet-аудит). Обычный output заблокирован запущенным сервером, поэтому использован отдельный каталог без остановки пользовательского процесса. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage4 --no-build` — 108 тестов прошли; запуск вне песочницы для локального testhost/UDP. `dotnet .artifacts/stage4/bin/Content.Server/debug/Content.Server.dll --validate-content` — успешная загрузка 3 оружий, 3 способностей, 2 существ без открытия порта. `git diff --check` — без ошибок пробелов. Godot не запускался: клиент не менялся.

Приёмка: проверить данные `Content.Server/Data/prototype.json`, выполнить `dotnet run --project Content.Server -- --validate-content` и изучить формулы. В Godot на этом этапе новых игровых действий нет: атаки добавляются на этапе 5.

**5. Ручная базовая атака — принят пользователем**

ПКМ — движение, ЛКМ — атака по направлению курсора. Сервер проверяет дистанцию, направление, частоту и попадание. Автоподхода к противнику нет.

Проверка: два игрока одинаково видят атаку и подтверждённый урон; клиент не может назначить попадание.

Реализовано: ручной направленный melee-удар по ближайшему тренировочному манекену, серверные fixed-tick cooldown/геометрия/LOS/урон/крит/HP, защита от replay и ограниченная очередь команд. Клиент предсказывает только взмах; HP и урон подтверждает сервер. Новые bounded reliable messages 15–18, протокол v5. PvP, смерть/respawn, AI, windup и rewind не добавлены. Подробности: `docs/architecture/0005-manual-basic-attack.md`.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage5 -m:1` — 0 ошибок, NU1900 из-за недоступного NuGet-аудита. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage5 --no-build` вне песочницы — 126 тестов прошли, включая одинаковый урон у двух клиентов с задержкой 100–150 мс / потерей 10% пакетов и восстановление актуального HP после AOI reentry. `dotnet .artifacts/stage5/bin/Content.Server/debug/Content.Server.dll --validate-content` — успешная проверка каталога, конфигурации и боевого профиля без открытия порта. Godot-сцену не запускал: executable не найден в PATH. `.tscn`, `project.godot`, `.uid` и AGENTS.md не менялись; `git diff --check` без ошибок пробелов.

Приёмка: перезапустить сервер и два клиента v5. Фиолетовый манекен — X=-7, Z=3. Подойти ПКМ на расстояние до 2; ЛКМ в его сторону — удар, золотой конус и одинаковые HP/урон у обоих клиентов. Взмах вдали/в другую сторону не наносит урон и не запускает автоподход. Частые клики не обходят cooldown. Синий конус — только предсказание; при задержке 100–150 мс / потере 10% пакетов урон остаётся серверным. На HP=0 манекен не восстанавливается; для новой проверки перезапустить сервер. Godot-визуальную приёмку выполняет пользователь.

**6. Активные способности и Dodge — принят пользователем**

Сначала один projectile, одна ground AoE и один dash. Ресурсы, cooldown, telegraph, prediction и ограниченная компенсация задержки.

Проверка: способности читаются и работают при задержке; высокий ping не даёт неограниченный rewind.

Реализовано: Q — направленный projectile, W — ground AoE под курсором с телеграфом, Space — прямой dash к курсору с остановкой у стены. Сервер проверяет владение способностью, sequence/tick, aim/LOS, состояние, ресурс, cooldown и бюджеты; считает каст и урон на fixed tick. Клиент предсказывает визуал и движение dash с reconciliation, но не HP/урон/ману. Projectile использует swept collision и одно ограниченное RTT catch-up до 100 мс без rewind персонажей. AOI управляет жизненным циклом эффектов; мана/слоты/отклонения приватные. Протокол v6, schemaVersion 2. Snapshot содержит dash state и ability acknowledgement; размер пакета учитывает текущий MTU. Подробности: `docs/architecture/0006-active-abilities.md`.

Временные prototype assumptions: только тренировочный манекен, без PvP/AI; mana без regen, dash без неуязвимости, отменяет прежний маршрут. Новый ПКМ во время dash выполняется после его завершения. Формулы, cast/cooldown/cost/range/speed — серверные данные; это не окончательный баланс.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage6 -m:1 -p:NuGetAudit=false` — 0 ошибок/предупреждений (NuGet-аудит явно отключён; безопасность зависимостей этим не проверялась). `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage6 --no-build` вне песочницы для testhost/UDP — 159 тестов прошли. Проверка собранного сервера с `--validate-content` успешна: schema 2, 3 оружия, 3 способности, 2 существа, без открытия порта. Пользовательские процессы не останавливались. Godot executable не найден в PATH; визуальная приёмка остаётся за пользователем.

Приёмка: перезапустить сервер и два клиента v6. Манекен X=-7, Z=3: Q в его сторону — синий/золотой направленный телеграф, затем голубой снаряд и одинаковый подтверждённый урон у обоих клиентов. W на манекен — круг до каста, затем impact/урон. Space к курсору — рывок максимум на 3, без прохода сквозь стену; ПКМ после/во время него сохраняет управление. Проверить MP и cooldown над локальным игроком, отклонение недоступного каста, отсутствие автоподхода и плавность при задержке 100–150 мс / потере 10% пакетов. Мана и HP манекена сбрасываются только перезапуском. Этап 7 — после приёмки.

**7. Первое PvE — принят пользователем переходом к этапу 8**

Монстр с агро, преследованием, атакой и возвратом. Затем небольшой босс с фиксированными характеристиками без масштабирования по числу участников.

Проверка: encounter можно пройти одному или вместе; AI и урон рассчитывает сервер.

Checkpoint 7.1: один красный монстр X=7, Z=3; proximity aggro ближайшего живого игрока, sticky target, преследование по текущей навигации, замах с фиксированным направлением, серверная melee-атака, leash/возврат домой при потере цели. Манекен сохранён. ЛКМ/Q/W повреждают монстра, но не других игроков. Клиент отображает NPC через interpolation; AOI spawn/reentry сообщает актуальное HP и текущий замах. Протокол v7: Monster kind и reliable NpcWindup (24); позиции остаются unreliable snapshots. Данные — секция Npc в appsettings, боевой профиль — существующий серверный каталог schema 2. Подробности: `docs/architecture/0007-first-pve.md`.

Временные ограничения: один монстр, без threat table/loot/EXP/regen/respawn/босса. HP=0 блокирует действия; новую сессию игрока создаёт перезапуск клиента, новую арену — перезапуск сервера. При возврате домой монстр не лечится. Это не постоянные правила смерти и reset encounter.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage7 -m:1 -p:NuGetAudit=false` — 0 ошибок/предупреждений, аудит зависимостей явно отключён. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage7 --no-build` вне песочницы для локального testhost/UDP — 172 теста прошли. `dotnet .artifacts/stage7/bin/Content.Server/debug/Content.Server.dll --validate-content` — каталог и включённый NPC валидны без открытия порта. `git diff --check` без ошибок. Godot executable не найден в PATH, сцену не запускал. Пользовательские процессы и AGENTS.md не менялись.

Приёмка 7.1: перезапустить сервер и два клиента v7, подойти к X=7, Z=3. Монстр обнаруживает игрока в радиусе 6, идёт к нему, показывает красный конус перед атакой. Уйти ПКМ/Space из конуса — урона быть не должно. ЛКМ/Q/W позволяют победить соло или вместе; HP и события одинаковы у обоих. Увести цель дальше 10 от дома или отключить её — монстр идёт домой. Проверить препятствия, AOI reentry и задержку 100–150 мс / потерю 10%. После приёмки — checkpoint 7.2: небольшой босс с фиксированными статами, без autoscale; этап 8 пока не начинается.

Checkpoint 7.2: отдельный оранжевый босс X=7, Z=-8, 242 HP независимо от числа игроков. Чередует направленный удар и ground AoE под текущей целью; направление/центр фиксируются на замахе. Зона повреждает каждого живого игрока в радиусе один раз с проверкой LOS, а не только выбранную цель. Смерть босса, отключение/поражение цели или leash отменяют замах. Прежние монстр и манекен сохранены. Серверные definitions `test_boss`, `boss_claw`, `boss_ground_area`; секция Boss задаёт размещение/AI. Протокол v8: Boss kind и reliable NpcArea (25), snapshots остаются unreliable. Подробности: `docs/architecture/0007-boss-encounter.md`.

Проверено 7.2: `dotnet build Game.slnx --artifacts-path .artifacts/stage7-2 -m:1 -p:NuGetAudit=false` — 0 ошибок/предупреждений; аудит зависимостей отключён. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage7-2 --no-build` вне песочницы — 185 тестов прошли, включая одинаковые AoE hits у двух клиентов с задержкой 100–150 мс / потерей 10% пакетов. `--validate-content` успешен: schema 2, 4 оружия, 4 способности, 3 существа. `git diff --check` без ошибок. Godot не найден в PATH; сцену не запускал. AGENTS.md и пользовательские процессы не менялись.

Приёмка 7.2: перезапустить сервер и два клиента v8, подойти к X=7, Z=-8. Оранжевый босс с BOSS HP показывает конус перед ударом, затем оранжевый круг и красный impact. Уход из конуса/круга ПКМ или Space предотвращает урон. Зона не следует за целью после начала каста; игрок, вошедший в неё, тоже получает урон. Проверить победу соло/вдвоём, одинаковые HP/события, возврат домой при потере цели и отсутствие autoscale/respawn. Лут/EXP/reset encounter не добавлены. Этап 8 — только после приёмки 7.2.

### Vertical Slice

**8. Персонаж и устойчивые сохранения — принят пользователем**

Идентификация игрока, постоянный персонаж, версионированные сохранения, восстановление после перезапуска и переподключения.

Проверка: персонаж восстанавливается без дублирования и отката подтверждённых изменений.

Реализовано: серверная Development identity (localhost-only, случайный токен, в БД только SHA-256), постоянный UUID отдельно от runtime IDs, schema/model v1 и additive migrations, сохранение позиции/шести base stats/HP/маны/melee и ability cooldown. Development использует SQLite с WAL/FULL и exclusive file ownership + owner/revision fencing; PostgreSQL адаптер сохранён с session advisory lock. Изменённые персонажи одного tick сохраняются одной транзакцией до публикации snapshots/events. SQLite I/O выполняется на worker, polling не блокируется; следующий simulation tick ждёт commit. Logout/shutdown сохраняют перед освобождением ownership. Протокол v9: bounded token в hello/welcome; частые snapshots unreliable. AGENTS.md не менялся.

Пользователь уточнил: **SQLite используется сервером на время разработки**, не только автотестами. Development default — `.data/project-g-development.db` из корня репозитория, вне build artifacts и Git. Тесты используют тот же серверный store с изолированными SQLite файлами. PostgreSQL остаётся будущей стратегией; смена provider не переносит данные автоматически. Production/Steam auth и автоматический fallback отсутствуют. Подробности: `docs/architecture/0008-character-persistence.md`; запуск: `docs/development/character-persistence.md`.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage8 -m:1 -p:NuGetAudit=false` — 0 ошибок/предупреждений; аудит зависимостей явно отключён. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage8 --no-build` вне песочницы для testhost/UDP — 208 тестов прошли. Проверены реальный серверный SQLite store, reconnect/server restart, duplicate login, transactional rollback/replay, uncommitted-state barrier, store failure, graceful shutdown и future schema rejection. Отдельный тест запускал настоящий executable сервер на собственном временном порту/SQLite-файле, принудительно завершал только созданный им процесс и проверял сохранение подтверждённых координат/маны после повторного запуска. `--validate-content` успешен: schema 2, 4 оружия, 4 способности, 3 существа. `git diff --check` без ошибок. Live PostgreSQL не запускался; Godot executable не найден в PATH, сцену не запускал. AGENTS.md, пользовательская БД и пользовательские процессы не менялись.

Ограничения: один персонаж на Development identity, до 32 sessions по умолчанию (1..64); нагрузочные цели региона ещё не доказаны. NPC/world state не сохраняется. Прерванные casts/effects/routes не восстанавливаются, оплаченная мана/cooldown не возвращается. Временное offline-правило: cooldown уменьшается по wall clock, HP/мана не восстанавливаются, HP=0 остаётся нулевым. Respawn не вводится. Неизвестная identity, повреждённая модель или несовместимый content не заменяются новой пустой сессией.

Приёмка: из корня проекта задать `DOTNET_ENVIRONMENT=Development`, запустить сервер без PostgreSQL/Docker, перезапустить клиенты v9. Два клиента — разные `--identity=alice` / `--identity=bob`; после disconnect/рестарта сервера профиль возвращает позицию/HP/ману из SQLite. Двойной вход Alice отклоняется; Bob продолжает играть. Сбой БД не подтверждает незаписанное состояние. Визуальная приёмка остаётся за пользователем; следующий этап 9 — только после подтверждения. PostgreSQL-specific проверки отложены.

**9. Предметы и экипировка — принят пользователем**

Определение предмета отдельно от его экземпляра, инвентарь, экипировка, модификаторы и ручной подбор объектов.

Проверка: предмет нельзя продублировать повторным запросом; экипировка корректно влияет на бой.

9.1: definitions/instances, приватный инвентарь на 8 мест, Weapon/Armor, серверные modifiers и SQLite-сохранение
с durability barrier. Два тренировочных предмета выдаются один раз; I открывает панель, кнопки надеть/снять.
Protocol v10 / content schema 3. По инструкции пользователя SQL/providers/migrations вынесены в `Content.Database`;
character model v1 сохранён, additive schema v2 добавляет inventory model v1.
Подробности: `docs/design/inventory-prototype.md`, `docs/architecture/0009-content-database.md`.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage9 -m:1 -p:NuGetAudit=false` — 0 warnings/errors;
`dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage9 --no-build` — 219 тестов прошли.
Есть wire round-trip/truncation/bounds, ownership/rate-limit, equip/no-heal/cooldown, SQLite instances/reconnect,
schema v1→v2 без потери identity/HP и два UDP-клиента с latency 100–150 мс / loss 10%: gear acknowledgement после commit.
`dotnet .artifacts/stage9/bin/Content.Server/debug/Content.Server.dll --validate-content` — успешно.
Godot 4.7.1 console executable запущен headless (`--quit-after 900 -- --identity=stage9check`) на собственном временном
сервере с отдельной SQLite в .artifacts: handshake v10 и загрузка сцены без runtime errors; сервер остановлен штатно.
Визуальная проверка UI/кнопок — пользовательская приёмка. Live PostgreSQL не проверялся.

9.2: spatial AOI spawn/despawn для предметов, G под курсором — ручной pickup без автоподхода.
Сервер проверяет sequence/rate/state/distance/препятствия/место; один предмет нельзя забрать дважды.
Два одноразовых тестовых seed (меч -5/1, броня -5/4), atomic ground claim + inventory + character checkpoint.
Database schema v3 / protocol v11; content schema 3 и character/inventory model v1 не менялись.
Подробности и приёмка: `docs/design/ground-items-prototype.md`. Loot rewards/trade/crafting не включены.

Проверено 9.2: `dotnet build Game.slnx --artifacts-path .artifacts/stage9-pickup -m:1 -p:NuGetAudit=false`
— 0 warnings/errors. `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage9-pickup --no-build`
— 233 теста прошли: malformed/round-trip, replay/flood, race, distance/стены/HP/capacity, AOI exit/reentry,
SQLite tombstone/restart, batch rollback, sync/async failure без публикации и без выдачи при shutdown.
Два UDP-клиента тестируются с latency 100–150 мс / loss 10%; uncommitted pickup не меняет их inventory/world.
`dotnet .artifacts/stage9-pickup/bin/Content.Server/debug/Content.Server.dll --validate-content` и `git diff --check` — успешно.
Godot 4.7.1 console headless (`--quit-after 900 -- --identity=stage92check`) прошёл handshake v11 и создание сцены без runtime errors,
собственный сервер использовал отдельную SQLite в .artifacts и остановлен штатно. Рабочая БД не затронута.
Визуальная приёмка G/UI остаётся за пользователем; Live PostgreSQL 16 проверен на отдельном временном Docker-контейнере: full instance/restart/pickup, stale world fence rollback и terminal expiry. AGENTS.md/scenes/.uid не менялись.

**10. Первое Эхо — принят пользователем**

Обновление 2026-10-09: дальнейшая разработка Эхо и способ получения отложены. Игрок начинает один; автоматическая выдача Миры отменена. Система временно выключена (`Echoes.Enabled=false`), существующие записи сохраняются без удаления. Ниже — история прототипа, не актуальное правило стартового набора.

Один проработанный персонаж: следование, базовый AI, самостоятельные действия, ручная Signature Ability и простая реакция на мир. Ядро сразу допускает до трёх активных Эхо.

Проверка: Эхо помогает, но не играет вместо пользователя; его состояние сохраняется.

Мира: следование с обходом препятствий, помощь по боевым действиям/полученным ударам хозяина,
ручная Signature T (курсором выбрать точку), реплики готовности/Signature/смерти/восстановления.
Основа до трёх активных слотов T/Y/U, выдаётся один экземпляр. AOI + interpolation, server-only AI/урон.
UUID/слот/позиция/оба cooldown сохраняются атомарно; schema v4, Echo model v1, protocol v13.
Временное prototype assumption: Эхо не получает урон; судьба Эхо при смерти не становится постоянным правилом.
Подробности и ручная проверка: `docs/design/first-echo-prototype.md`.

Проверено: `dotnet build Game.slnx --artifacts-path .artifacts/stage10 -m:1 -p:NuGetAudit=false`
— 0 warnings/errors; `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage10 --no-build`
— 253 passed. Проверены wire bounds, replay/flood/state/cooldown/LOS, AI/idle/навигация, три слота,
SQLite schema 3→4 без reset, reconnect/stable UUID/cooldown и два UDP-клиента (100–150 мс, loss 10%).
Commit gate не допускает публикации действий до SQLite commit; same-tick HP учитывает удар Эхо последним.
`dotnet .artifacts/stage10/bin/Content.Server/debug/Content.Server.dll --validate-content` и `git diff --check` — успешно.
Godot 4.7.1 headless main scene (`--max-fps 60 --quit-after 900 -- --identity=stage10-smoke`):
handshake v13 и сцена без runtime errors на отдельной `.artifacts/stage10/godot-smoke.db`;
тестовый сервер штатно остановлен. Sandbox сначала дал native crash/ConnectionFailed; успешный прогон — вне sandbox.
Рабочая SQLite не затронута, Live PostgreSQL 16 проверен на отдельном временном Docker-контейнере: full instance/restart/pickup, stale world fence rollback и terminal expiry. AGENTS.md/scenes/.uid не менялись.

**11. Уровни и развитие навыков — реализован, визуальная проверка отложена пользователем**

Опыт за разные активности, очки характеристик за уровень, изучение навыков и развитие использованием. Панель примерно на восемь способностей, без skill points.

Проверка: награды нельзя повторно получить одним запросом; развитие навыков нельзя накручивать заведомо недействительными действиями.

Реализовано: серверный EXP за подтверждённые попадания и однократные открытия, stat points,
изучение навыка за открытие и освоение через успешное применение. Восемь слотов Q/W/E/R/A/S/D/F,
отдельный Space, окно K. Снятие/смена слота сохраняет освоение и cooldown.
Прогрессия и stats сохраняются атомарно под revision fence; schema v5, progression model v1, protocol v14.
Баланс — временные серверные данные; правила boss reward/death/profession не закрепляются.
Подробности и ручная приёмка: docs/design/progression-prototype.md; ADR 0010.

Проверено в Codespace: сборка всего Game.slnx — 0 ошибок/предупреждений;
полная suite Content.Tests — 266/266 (13 новых тестов прогрессии);
content validation и git diff --check прошли. Точные команды: docs/design/progression-prototype.md.
Исправлена нестабильная проверка прежней pickup-гонки: при потере пакетов победить может любой peer,
проверяется ровно один получатель и выдача только после commit. Godot-сцена не запускалась:
executable отсутствует; live PostgreSQL не проверялся. Визуальная приёмка остаётся за пользователем.


**12. Первая скрытая профессия — реализована, ожидает приёмки**

Серверные условия открытия, история действий и осознанная необратимая смена единственной активной профессии.

Проверка: условия не передаются клиенту; возможность открывается через игру, а переход требует явного решения игрока.

Переход к этапу 12 явно разрешён пользователем до домашней визуальной проверки этапа 11.
Реализована серверная история успешных действий и открытие «Хранителя троп» по временным данным.
Окно P: рассмотреть → предупреждение/галочка → отдельное подтверждение; отмена и запрет возврата.
Новый навык уровня 1, приватность, replay/rate/state validation, session-bound одноразовый token.
Protocol v15 (43/44/45); optional SavedProfession v1 в существующей fenced progression записи schema 5.
Ручная приёмка и точные ограничения: docs/design/first-profession-prototype.md.

Проверено в Codespace: сборка 0 ошибок/предупреждений, 12 новых profession tests,
полная suite 278/278, content validation и diff check прошли. Точные команды в design doc.
Godot executable не найден в PATH, сцена не запускалась. Live PostgreSQL не проверялся.
Визуальная приёмка ожидается; переход к этапу 13 явно разрешён пользователем.


**13. Первое изменение живого мира — реализовано, ожидает приёмки**

Один сюжетный Узел с несколькими Руслами: действия игроков меняют маршрут, угрозу, NPC и слухи. Минимальный Live-DM инструмент с авторизацией и аудитом.

Проверка: последствия общие для всех и сохраняются после перезапуска.

Реализован общий Узел «Переправа»: два вклада разных персонажей открывают проход, два вклада
после реального боя создают дозор и снижают агро; четыре общих исхода меняют NPC-реплику и слух.
H — разговор у хранителя (-3,-2), данные и числа временные. Live-DM stdin выключен по умолчанию:
три подготовленные операции, authorization hash, bounded inbox и transactional audit.
Schema 6 / SavedWorldNode v1, optional private participation в progression v1; protocol v16 (46–48).
World lease/revision, вклад персонажа и audit сохраняются атомарно до публикации. Reconnect/restart
получают текущую collision geometry; closing cells запрещён. Подробности: docs/design/first-world-node-prototype.md.

Проверено: сборка 0 warnings/errors, полная suite 293/293 (15 новых WorldNode tests), включая
два lossy UDP peers и failure/commit gate; отдельный executable stdin smoke — авторизация, аудит,
штатный shutdown на изолированной SQLite. Content validation/diff check успешны.
Godot в PATH отсутствует, визуальная приёмка ожидается. Live PostgreSQL не проверялся.
Этап 14 реализован; приёмка ожидается. Этап 15 в работе: сбор ресурсов и простой крафт реализованы, приёмка ожидается.

**14. Сборка законченной стартовой зоны — реализован, ожидает приёмки**

Один город, ближайшая территория, минимальное обучение без одиночного instance, старт без Эхо (решение 2026-10-09), исследование, fog of war, компас и координаты.

Проверка: новый игрок проходит цельный игровой цикл — исследует, сражается, открывает профессию и замечает последствия своих действий.

Реализовано: Речная пристань, памятка F1, сборка существующего общего игрового цикла,
приватная карта исследования, открытые места, миникарта/M, компас и координаты. Protocol v17,
SavedExploration v1 как optional-компонент SavedProgression v1; schema v6 без reset.
Проверено: сборка без warnings/errors; 304 теста (11 новых) прошли, content validation и diff check успешны.
Godot недоступен в Codespace, main scene не запускалась; визуальная приёмка ожидается.
Подробности: docs/design/starter-zone-prototype.md. Этапы 11–13 ждут домашней
визуальной приёмки; переход разрешён пользователем.

### Alpha и дальнейшее развитие

**15. Крафт и локальная экономика — реализован, ожидает приёмки**

15a–15e: общий stock/материалы/крафт, развиваемый клинок, заточка +0…+5, current/maximum/revision, ремонт, прямой обмен, игровая валюта и локальный escrow market. Истощённые ресурсы восстанавливаются по сохранённому deadline. Временный баланс явно утверждён пользователем 2026-10-08; docs/design/stage15-balance-proposal.md. C — крафт/ремонт, I — gear, J — кузница/vendor/market, B — обмен; памятка F1 обновлена.

Protocol v21, BalanceVersion 2, SQL schema 7: unique ownership registry с terminal UUID. SavedInventory/WorldNode v1 совместимы через optional-компоненты, нет reset. Эволюция сохраняет экземпляр/уровень/прочность; провал заточки снижает maximum и может уничтожить предмет. Server RNG, материалы, wallet, receipts, escrow/credits/stock и audit сохраняются атомарно до публикации. Повторы не тратят и не бросают шанс снова. Offline proceeds получаются отдельной идемпотентной операцией. Trade reservations/offer revision/двустороннее подтверждение; рыночные карточки только у станции.

Проверка: заключительный полный набор 351/351 (включая SQLite rollback/restart, lossy UDP commit gate, market AOI и гонки). Content validation: Schema 3 / Balance 2, успешно. git diff --check и whitespace validation новых файлов успешны. C# client/server/tests собираются; существующая SQLite dependency выдаёт NU1903. Godot executable не найден, визуальная приёмка ожидается; Live PostgreSQL 16 проверен на отдельном временном Docker-контейнере: full instance/restart/pickup, stale world fence rollback и terminal expiry. Рабочая .data/процессы не менялись; commit/push не выполнены.

Архитектура: docs/architecture/stage15-economy-plan.md. Реализация/лимиты/домашняя приёмка: docs/design/economy-stage15-progress.md. Это bounded прототип: один last receipt/actor (старые ID не исполняются, полный history retrieval отсутствует), до 8 listings/2 на продавца, 16 offline credit owners; full inventory/wallet/credit отклоняет всю операцию. Этап 16 реализован; приёмка ожидается.

**16. PvP, смерть и репутация — реализован, ожидает приёмки**

Временный пакет явно утверждён пользователем 2026-10-08, включая дополнение: подбор только с активным PvP-тегом на старте и завершении. До одного equipped unbound item, legendary без bound допускается; подбор сразу доступен, канал5с, expiry30мин без возврата. В город можно войти с тегом, PvP запрещён.

Реализованы mode/tag/safe policy для melee/projectile/AoE/Echo, civilian episodes/PK/rep, PvE/mixed/PvP death/EXP/receipt, full-instance world loot, explicit respawn, combat disconnect/same actor reconnect, V UI/flags/city boundary/G channel/TTL. Protocol v22 / balance3, SQL schema7: optional SavedPvp/DeathLoot JSON + unique registry, без reset/recovery. Death/EXP/PK/ownership/audit фиксируются до публикации. Детали: docs/architecture/stage16-pvp-death-plan.md; правила: docs/design/stage16-rules-proposal.md.

Проверено: C# client/server/tests build без ошибок, suite375/375 (24 новых PvP теста), content schema3/balance3 validation и tracked/untracked whitespace. Real SQLite rollback/restart/expiry/one owner, lossy UDP commit gate/reconnect/shutdown, настоящие projectile/AoE/Echo policy. Существующие NU1903 SQLite dependency. Godot отсутствует, сцена/UI не запускались; Live PostgreSQL 16 проверен на отдельном временном Docker-контейнере: full instance/restart/pickup, stale world fence rollback и terminal expiry. Домашняя приёмка/команды: docs/design/pvp-stage16-progress.md. Рабочая .data/процессы не менялись; commit/push не выполнены. Проверки выше относятся к checkpoint этапа16.

**17. Группы и гильдии — реализован прототип, визуальная приёмка ожидается**

Пользователь утвердил весь пакет правил сообщением «го». Реализованы party6/20, приглашения30с, роли/лидерство, offline slots и grace120с, приватные roster/HP/позиции, durable membership/replay и guild32/free/Leader-Officer-Member/explicit handoff. Friendly fire off внутри party; guild сама иммунитета не даёт. EXP личный, лут ручной по stage16. N — группа, O — гильдия.

Protocol23, additive SQL schema8; content schema3/balance3 сохранены. Social realm lease и общий character/world/social/audit checkpoint, unique membership/name/FK, bounded version1 documents и restore checks. SQLite и PostgreSQL поддерживаются; offline administration не дублирует membership в character JSON. Server-only budgets через Social.Enabled/MaxParties/MaxGuilds.

Проверено: full Game.slnx build — 0 ошибок, content validation успешна; 408/408 тестов без skips, включая33 новых social checks и живую PostgreSQL16. Domain/roles/replay/TTL/leader/restart; real projectile/AoE/Echo immunity и guild PvP; SQL unique/rollback/7→8 migration; lossy UDP100–150ms/10% privacy, commit gate и reconnect; packet budget20. Tracked/untracked whitespace проверены. Существующее NU1903 для SQLitePCLRaw.lib.e_sqlite3 2.1.11 сохраняется. Godot executable отсутствует: сцена/UI не запускались.

Архитектура: docs/architecture/stage17-social-plan.md. Утверждённые правила: docs/design/stage17-social-rules-proposal.md. Домашняя приёмка: docs/design/stage17-social-acceptance.md. Рабочая .data и пользовательские процессы не менялись, commit/push не выполнены. Temporary test PostgreSQL остановлена после проверки. Следующий этап18 требует отдельного решения о multi-region/social routing; сейчас не начат.

**18. Несколько регионов и путешествия — вертикальный срез реализован, ждёт проверки**

Два соседних региона в одном процессе: `prototype` и `outskirts`.
Сервер определяет пеший переход по границе; freeze → atomic SQL checkpoint →
активация назначения. Lease персонажа не освобождается, двойного владельца нет.
Переносятся inventory/progression/PvP и до3Эхо; личная карта каждого региона
сохраняется отдельно. Одна social authority; координаты группы другого региона скрыты.

Protocol24: reliable RegionEnter/epoch, bounded RegionPacket для игровых сообщений.
Старые команды/snapshots отбрасываются; AOI, prediction, navigation и UI клиента
пересоздаются для нового региона. Snapshots остаются unreliable.
Отключение/боевой reconnect/shutdown проходят через durability barrier.
Ошибка записи запрещает публикацию и autosave; restart берёт сохранённый регион.
SQLite Development, SQLschema8, без сброса данных; content schema3/balance3.

**По последнему решению пользователя маунт и обмен картами сейчас НЕ делаем.**
Межпроцессный handoff также отложен; fast travel не добавлен.
ADR: docs/architecture/0011-region-travel.md.
Реализация, проверки и домашняя приёмка: docs/design/stage18-travel-progress.md.
Проверено: build0 warnings/errors;443 passed/6 PostgreSQL skipped; content validation
и whitespace успешны. Реальный Godot headless: проход туда/обратно без ошибок.
Lossy UDP100–150ms/10%, stale epoch, disconnect/боевой reconnect, shutdown/ошибка БД
и crash/restart executable проверены на отдельной SQLite. Рабочая .data не менялась.

**19. Полноценные Эхо и резонанс**

До трёх компаньонов, affinity, настроение, личные истории, взаимные диалоги, призыв и дубликаты.

Проверка: персонажи имеют ценность вне боя; выдача и усиление Эхо защищены от повторных операций.

**20. Масштабирование и подготовка PC-релиза**

Нагрузочные боты, бюджеты tick/трафика, массовые события, восстановление после сбоев, мониторинг и закрытые тесты Steam.

Проверка: измеренные показатели соответствуют заранее выбранным целям нагрузки.

Региональные gateways и simulation cells вводим по результатам измерений. Single-shard означает один логический мир, а не необходимость сразу строить огромный кластер.

**Позже отдельными этапами:** мобильные клиенты, корабли, владение объектами мира, уникальные артефакты и профессии, Tournament Server. Официальная RMT-площадка — только после отдельного решения, не часть ближайшего плана.

**Текущий checkpoint — этап17 реализован как прототип; этапы11–17 ждут домашней визуальной приёмки. Этап18 реализован в согласованном объёме без маунта/обмена картами; ожидает пользовательской проверки. Этап19 не начат.**

По запросу пользователя добавлен Development-only инструмент «Оживить» в I: восстановление HP=0 → текущий Max HP
на fixed tick, server ownership + localhost + Development, без изменения позиции/маны/cooldown/предметов.
HP подтверждается только после существующего SQLite durability barrier. Protocol v12: command 33 (sequence uint32),
capability 34 (bool byte), оба ReliableOrdered. Persistent models/schema не меняются; правила полноценного respawn не приняты.
Проверки: `dotnet build Game.slnx --artifacts-path .artifacts/dev-revive -m:1 -p:NuGetAudit=false` — 0 warnings/errors;
`dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/dev-revive -p:NuGetAudit=false` — 238 passed.
Проверены wire bounds, fixed tick, отсутствие heal живого/изменения маны/gear/cooldown, SQLite restore и commit barrier,
запрет команд в Staging/Production. `git diff --check` — успешно.
Godot headless main scene запущена (`--quit-after 600 -- --identity=revive-smoke`); текущий пользовательский сервер v11
правильно отклонил клиент v12. Пользовательский процесс не остановлен, визуальный тест кнопки требует restart сервера v12.
AGENTS.md/scenes/.uid не менялись; это историческая проверка Development-инструмента до этапа 10.
