# Movement & Controls — M2

Дата: 2026-10-17. Статус: технически реализовано, автоматические проверки пройдены.
Пользовательская визуальная приёмка отдельно от автоматической проверки.

Применяются [контракт](../design/gameplay-contract.md) и решение D129:
базовые 20 TPS сохраняются. Исходный GDD 02 и старый план 30 TPS не заменяют
это более позднее принятое решение.

## Границы среза

- Server владеет маршрутом, проверкой input order, sequence, времени,
  частоты и регионального epoch. Частота snapshots отделяется от рассылки
  боевых/lifecycle результатов.
- Client предсказывает с fixed delta из handshake, отправляет разовый
  Destination/Stop надёжно, held cursor — Sequenced с частотой 20 Hz.
  Локальные кадры prediction продолжаются без обязательного пакета каждый tick.
- Shared содержит bounded intent kind и подтверждённый client simulation tick
  в snapshot; никаких Godot types или изменения authority.
- БД, content schema, баланс, рабочие карты и renderer не меняются.
- Wire layout меняется с обязательной пересборкой обеих сторон.

Для разового Destination серверный snapshot подтверждает не только sequence,
но и client tick, до которого сервер уже продвинул маршрут. Это позволяет
удалять обработанные кадры replay без потока повторных Destination-пакетов.
Время клиента не двигает серверный clock и не включает lag compensation.

Проверки: baseline на пяти отдельных UDP-клиентах реального linvel_beech,
protocol malformed/budget tests, timing/rate/order validation, независимое
расписание snapshots, потерянный/задержанный Destination и Stop, reconnect,
Godot prediction/correction smoke, релевантная регрессия. CPU-замер исключает
render и БД; packet measurement описывает application payload, не wire traffic.

## Поведение и точки настройки

Одиночный клик создаёт одну надёжную Destination-команду. Локальный mover
продолжает fixed-step prediction, не отправляя тот же маршрут каждый tick.
Уточнение пользователя M004: удерживаемая ПКМ задаёт прямое направление без
обхода. Первый CursorStart надёжен; обновления Cursor идут с частотой 20 Hz
через Sequenced. При упоре позиция ограничивается первым препятствием,
но намерение бега сохраняет анимацию на месте. Отпускание удержания отправляет
надёжный Stop. Потеря отдельного cursor-пакета не означает отпускание.
Согласованная отмена M001–M003 сохраняется.

Физическое нажатие сразу начинает direct cursor movement. Если оно оказалось
коротким кликом, отпускание фиксирует Destination до выбранной поверхности;
после удержания отпускание останавливает. Короткий клик может отправить один
CursorStart перед Destination; очень быстрый клик коалесцируется в один
Destination. Прямой вызов MoveTo отправляет один Destination.
Порог распознавания короткого клика `CursorHoldThresholdSeconds=0.18` —
редактируемая настройка прототипа на PlayerController, не permanent gameplay rule.
При удержании направление проецируется на плоскость текущей высоты игрока,
поэтому курсор на стене тоже задаёт намерение, а проходимость проверяет solver.

`PlayerController.StopMovement()` отправляет Stop надёжно. Сервер останавливает
обычный маршрут в своей текущей позиции, а не идёт к присланной клиентом
точке остановки. Фазовые запреты и принудительные перемещения сохраняют
свои правила. Новая клавиша Stop и rebinding относятся к M4; текущие вызовы
Stop из игровых окон/действий используют обновлённую реализацию.

Сервер коалесцирует movement input до одного актуального намерения на fixed
tick, включая конфигурацию без persistence. Все боевые/lifecycle результаты
публикуются как прежде после каждого simulation tick; пропуск movement snapshot
не задерживает их. Bootstrap и новый AOI entry могут отправить внеочередной
snapshot, чтобы не откладывать начальное состояние.

- `Content.Server/Configuration/ServerOptions.cs`: `TickRate` (по D129 — 20),
  `MovementSnapshotRate` (20). CLI-переопределение, например
  `--Server:MovementSnapshotRate=5`, удобно для проверки независимости событий.
  Частота snapshot ограничена частотой симуляции; производственная база 20/20.
- `Content.Shared/Network/NetworkConstants.cs`: `MovementInputRate=20`.
  Клиент использует fixed delta из handshake, независимо от render FPS.
- `Content.Server/Networking/MovementInputWindow.cs`: burst 12 пакетов,
  refill 60/с, максимальное опережение client clock 2 с и возраст 10 с.
  Это технические пределы ingress, не дополнительные секунды боевого буфера.
  Проверяются возрастающий sequence, монотонный ClientTick и его относительный
  ход внутри сессии; поддерживается uint wraparound. Нулевой input order,
  невалидные виды/bytes, неправильная доставка и старый epoch отклоняются.
- `Content.Client/Scripts/PlayerController.cs`: `HardCorrectionDistance=0.5f`.
  Мягкое сглаживание остаётся с коэффициентом 20/с; внутренняя позиция сразу
  восстанавливается из authority. Недопустимый путь визуального сглаживания
  по-прежнему вызывает жёсткое восстановление независимо от расстояния.
- `Content.Shared/Navigation/CursorSteering.cs`: локальный horizon 0,25 м,
  ограниченный поиск первого блокирующего участка и collision skin 1 мм.
  Движение проходит через общие client/server mover и физическую поверхность,
  не через A*. Combat видит фактическую нулевую velocity при упоре.
  Флаги DirectCursor/RunningIntent в snapshots управляют режимом восстановления
  и видимой анимацией, включая удалённых игроков. Позиция остаётся серверной.
- `ProjectG.Movement` Meter: `movement.simulation.ms`, тег `server.port`.
  Измеряет только шаг всех world simulations, исключая serialization, БД и render.

При отклонённом разовом Destination сервер повторяет watermark в snapshots,
чтобы клиент не держал неподтверждённый маршрут в replay бесконечно. Существующий
допустимый серверный маршрут сохраняется, если запрет самой фазы не остановил его.
Временная метка не даёт клиенту изменить server time или получить rewind.

## Wire v76

Предыдущий первый срез использовал v75. Message IDs сохраняются; handshake
требует одинаковый v76 у клиента и сервера. Content/SQL schemas не меняются.

| ID | Layout после message ID | Доставка | Размер вместе с ID |
| --- | --- | --- | --- |
| 12 MoveCommand | uint Sequence, uint ClientTick, float X/Z, float TargetHeight, ulong GeometryHash, byte Kind, uint InputOrder | Cursor=Sequenced; Destination/Stop/CursorStart=ReliableOrdered | 35 Б |
| 13 WorldSnapshot | uint ServerTick, byte Count, записи EntitySnapshot | Unreliable | 7 + 73 × Count Б |

Kind: Cursor=0, Destination=1, Stop=2, CursorStart=3. InputOrder остаётся общим сессии с
боевыми командами; cursor refresh и Destination короткого клика сохраняют order
первоначального нажатия. Stop после удержания получает новый order.
Sequence увеличивается при сетевом input; ClientTick —
при каждом локальном simulation step. Они больше не означают одно и то же.

В записи EntitySnapshot после v75 `CancelledNavigationOrder` добавлен
`uint ProcessedClientTick`. В прежнем байте Control нижние 6 бит сохраняют enum,
бит 7 означает DirectCursor, бит 6 — RunningIntent; новые флаги не увеличивают
размер записи. RunningIntent без DirectCursor отвергается. Сервер вычисляет ProcessedClientTick из последнего принятого
ClientTick и числа уже выполненных server steps. ClientTick команды включает
её первый локальный шаг; первый серверный шаг подтверждает этот же tick,
последующие продвигают его без новых пакетов.

Replay сохраняет неподтверждённые новые команды; для подтверждённой команды
удаляет кадры до ProcessedClientTick включительно. Оставшиеся кадры этой
команды продолжают именно серверный Target, включая скорректированную Stop-точку.
Отменённые input orders не восстанавливаются. Максимум 16 записей в snapshot:
1175 + 10 Б regional envelope = 1185 Б, ниже лимита 1200.

Disconnect прекращает обычный маршрут по существующему правилу, сохраняя
боевое тело до принятого срока. Reconnect получает новый клиентский controller
и чистые sequence/history; сервер сбрасывает movement ingress и navigation
session state. Переход региона очищает буфер движения и ingress clock;
старые region epochs не исполняются в новой территории.

## Измерение до/после

Windows x64, Intel Core i5-13500H, .NET 10.0.3, Debug. Один парный прогон по
10 с после загрузки: 5 UDP-клиентов на реальном `linvel_beech`, отдельная
SQLite-БД, без искусственных потерь; все серверные регионы продолжают симуляцию.
Игроки стоят и посылают повторные намерения с 20 Hz. Этот стенд проверяет
расписание и стоимость шага, а не pathfinding большого боя или ёмкость сервера.

| Метрика | Baseline v75 | M2 v76 |
| --- | ---: | ---: |
| Simulation samples за ~10 с | 200 | 201 |
| Simulation P50 / P95 / P99, мс | 0,172 / 0,455 / 1,269 | 0,218 / 0,313 / 0,514 |
| Snapshot intervals | 199 | 199 |
| Интервал P50 / P95 / max, мс | 46,78 / 63,54 / 78,92 | 47,64 / 76,61 / 92,64 |
| Пакеты наблюдателя, /с | 73,85 | 73,85 |
| Payload наблюдателя, Б/с | 10 519 | 10 918 |

Рост payload ~400 Б/с соответствует +4 Б × 5 игроков × 20 Hz. Пакеты включают
прочие message families; заголовки транспорта и retransmission не посчитаны.
Интервалы измерены на получателе, включают poll/scheduling jitter. Один парный
прогон не доказывает ускорение CPU; max интервал кандидата хуже baseline.
Здесь нет обещания ровных 50 мс или нагрузочного KPI. Результаты сравнения
20/30 TPS для боя уже сохранены отдельно, и D129 выбирает 20 TPS.

## Проверки

Артефакты: `.artifacts/movement-controls-m2/`.

- `dotnet build Game.slnx --artifacts-path .artifacts/movement-controls-m2/build --no-restore -m:1 -p:NuGetAudit=false`
  — успешно, 0 warnings / 0 errors. Выход изолирован от DLL пользовательского сервера.
- Первый целевой прогон — 269 passed / 0 failed. Проверены Shared wire,
  malformed layouts/MTU, timestamp/flood/sequence wrap, scheduler при 20/30/60
  TPS, sparse ack, поздний Cursor после Stop, reconnect и независимость боевых
  результатов от частоты snapshots.
- После M004 целевой прогон — **327 passed / 0 failed**
  (`m2-cursor-complete.trx`). Добавлены flat/surface проверки: прямой Cursor
  упирается без обхода и без накопленного проникновения в границу, сохраняет
  running intent при нулевой физической velocity, меняет направление, правильно
  восстанавливается из snapshot и прекращается по Stop. Destination на той
  же геометрии по-прежнему строит обход. Устранены также zero-length route state
  после Reset/Stop и погрешность на границе surface через skin 1 мм.
- Baseline/candidate: `PROJECT_G_MOVEMENT_REPORT=<absolute .json path>` и
  `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m2/build --no-build --no-restore --filter FullyQualifiedName~MovementNetworkMeasurementTests`.
  Каждый прогон 1/1; JSON: `baseline.json`, `candidate-final.json`.
  Счётчик simulation samples может включить граничный шаг окна измерения.
- Godot headless `res://Tests/Navigation/NavigationCancellationSmoke.tscn`
  с отдельным сервером/портом/SQLite — успешно. Карта загружается без
  искусственных потерь; затем movement работает при 60–90 мс / 3% потерь.
  Проверены один пакет Destination, ограниченная история prediction,
  серверная отмена без replay-resurrection, подавление позднего Continuous,
  надёжный Stop, 0,4 м soft / 0,6 м hard correction на реальном рельефе.
  Успешный финальный лог после M004: `live-084198609ccc4663b89d003492ccbe29/client.log`.
  Дополнительно проверены CursorStart/refresh и Stop по отпусканию.
- `Tests/Actors/CapsuleActorSmoke.tscn` — успешно, включая run animation
  при неизменной позиции, направление капсулы и остановку анимации после
  снятия намерения движения.
- Финальная широкая последовательная регрессия после M004 —
  **1956 passed / 1 skipped / 0 failed**, 7 м 25 с
  (`m2-final-regression-serial.trx`):
  `dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/movement-controls-m2/build --no-build --no-restore --filter 'FullyQualifiedName!~Postgres&FullyQualifiedName!~WorkingDatabase' --logger 'trx;LogFileName=m2-final-regression-serial.trx' --results-directory .artifacts/movement-controls-m2 --blame-hang-timeout 120s`.
  В каталоге собранных тестов на время прогона использован runner config
  `parallelizeTestCollections=false`, `maxParallelThreads=1`; после прогона
  временный `xunit.runner.json` удалён. Opt-in Postgres/WorkingDatabase исключены;
  один skip требует отдельной development-БД. Рабочая БД не затрагивалась.
- Финальная валидация контента — успешно:
  `Content.Server.dll --validate-content`, Schema=24 / Balance=19,
  Weapons=8 / Abilities=18 / Creatures=5.

В первом Godot-прогоне synthetic hard correction использовал старую высоту
на склоне, и проверка не прошла. Стенд теперь выбирает точку/высоту на surface.
При проверке также исправлен Stop replay, чтобы подтверждённый input не
возвращал клиента к его старой точке остановки. Финальный smoke прошёл.
Предупреждение Terrain3D `instance_reset_physics_interpolation` остаётся прежним.
Во время общей сборки найден и удалён только дословный дубль блока `rework`
в `Scripts/UI/Assets/UiAssets.cs`, который давал CS0128; в
`ProgressionPresentation.cs` добавлено приведение byte Slot к int для
однозначного `Math.Clamp` (CS0121). Остальной UI не переписан.

Загрузка региона при потерях и пользовательская визуальная приёмка этим
headless smoke не подтверждаются.

## Проверка в игре и следующий этап

Пересобрать и перезапустить обе стороны v76. Для отдельной тестовой БД/порта:
`tools/Start-CombatTest.ps1`. Проверить клик и удерживаемую ПКМ, отпускание,
атаку/блок/навык во время бега, остановку через текущие игровые окна и reconnect.
При удержании направить курсор за стену: персонаж не должен обходить её,
позиция остаётся у препятствия, бег продолжается на месте. Отпустить ПКМ:
движение и намерение бега прекращаются. Короткий клик по другую сторону
доступного обхода, напротив, строит обычный путь.
Для ручной проверки коррекций включить сетевую симуляцию в NetworkClient.

Далее M3: общая политика перекрытий/LOS; M4: Follow и полный rebinding.
Mobile остаётся исключён. Падения/вода/динамические опоры не объявляются
реализованными этим сетевым этапом.
