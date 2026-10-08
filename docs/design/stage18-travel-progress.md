# Этап18: регионы и путешествия

Реализован вертикальный срез двух регионов в одном процессе. Ждёт домашней
проверки пользователя; этап19 не начат. Маунт и обмен картами явно отложены.
Архитектура: [ADR0011](../architecture/0011-region-travel.md).

## Реализация

- Data/regions.json: основной prototype и соседняя outskirts, отдельные
  geometry/NPC/world-node/публичные места и reciprocal пешие границы. Баланс и
  часть визуальных заготовок общие для прототипа; это не gameplay layers.
- Один runtime entity allocator, bounded ownership registry с неповторяющимся
  epoch, одна social authority и один fixed-tick host. Live-DM console пока
  управляет первоначальным регионом; региональный выбор Live-DM не добавлен.
- Сервер выбирает arrival по фактической позиции. Клиент не назначает регион
  или координаты появления. Cast/dash/effects/trade должны завершиться до handoff.
- Immutable cargo всех четырёх документов персонажа. Lease удерживается;
  source actor/Эхо заморожены и удалены, destination активируется только после
  atomic checkpoint characters + world records + social + item owners + audit.
- Inventory, UUID/slots/cooldowns до3Эхо, progression, PvP deadlines сохраняются.
  Карты current/archived разделены, legacy marker knowledge не раскрывает другой
  регион. Travel не logout и не отменяет приглашения. Чужие региональные
  HP/координаты party остаются неизвестными.
- Network polling продолжается во время SQL barrier. Disconnect обрабатывается
  после barrier; боевой персонаж остаётся в своём фактическом регионе и может
  переподключиться. Shutdown дожидается записи и освобождает все leases.
- Неопределённый исход записи/activation останавливает realm без публикации и
  autosave. Recovery — перезапуск из durable state, без молчаливого reset.
- Клиент синхронно отсоединяет старую сцену/подписки, затем загружает geometry,
  AOI, player/prediction, Эхо и UI нового региона. Камера следует новому local actor.
  Голубой круг обозначает границу; на карте он виден только в исследованных клетках.

## Протокол и данные

Protocol **24**. Клиент и сервер нужно перезапустить вместе.

- ID81 RegionEnter: ushort type, ulong epoch, bounded string region key
  (1–64 lowercase ASCII/digits/underscore), float exitX, float exitZ, float radius.
  ReliableOrdered, после Welcome/commit и до bootstrap. Никаких данных следующей
  карты, секретов или destination coordinates.
- ID82 RegionPacket: ushort type, ulong epoch, исходный inner packet с его
  message ID. Overhead10 bytes, полный пакет ≤1200 bytes. Вложенные envelopes и
  handshake/control внутри запрещены. Inner parsers проверяют exact size.
- Все игровые сообщения regional host используют envelope; Move — Sequenced,
  snapshots/presence — Unreliable, critical events — ReliableOrdered.
  Snapshot budget учитывает overhead и MTU. Старый/ещё не активный epoch игнорируется.
- Однорегиональные fixtures могут запускать host без regional composition;
  это не совместимость с protocol23. Обычный executable всегда региональный.

SQLschema8, content schema3/balance3, SQLite Development — без reset/migration.
Optional OtherExplorations/Places совместимы с прежними сохранениями.
PostgreSQL multi-world путь собран, live PostgreSQL этой итерацией не проверен.

## Проверки 2026-10-08

~~~powershell
dotnet build Game.slnx --artifacts-path .artifacts/stage18 -m:1 -p:NuGetAudit=false
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage18 --no-build
dotnet .artifacts/stage18/bin/Content.Server/debug/Content.Server.dll --validate-content
git -c core.safecrlf=false diff --check
~~~

Первый domain checkpoint:420 passed/6 PostgreSQL skipped. Второй:434/6.
Transport/client checkpoint:441/6 до двух дополнительных shutdown/failure checks.
Финальная повторная регрессия: **443 passed,6 PostgreSQL skipped,0 failed из449**.
Build:0 warnings/errors. Content validation и tracked/untracked whitespace — успешно.
Targeted RegionalNetworkTests:6/6; RegionProtocolTests:3/3.

Покрытие: bounds/truncation/nesting/epoch, authoritative boundary, immutable cargo,
atomic rollback и уникальность item owners; два UDP-клиента при100–150ms/10%loss,
перестановка пакетов, stale command после перехода, round trip, party privacy,
disconnect во время записи, боевой reconnect, shutdown с незавершённой записью,
отказ БД без autosave, аварийное завершение настоящего server executable и restart.

Реальная Godot-сцена запускалась headless на отдельном сервере/SQLite. Smoke через
RMB input handler прошёл prototype → outskirts → prototype, без ошибок C#/Godot.
Финальные логи: .artifacts/stage18/godot-smoke-5e7465f74dd54d829a8b20e4ab0cd847/.
Первый smoke с off-screen input через viewport не двигал персонажа; повторный
передавал тот же RMB event напрямую в handler. Это не визуальная приёмка удобства UI.
Scripts smoke лежат в .artifacts/stage18/, профиль и БД изолированы.

VSTest внутри sandbox дважды не подключился к testhost; успешные запуски выполнены
с разрешением вне sandbox. Шесть PostgreSQL tests пропускаются без тестовой БД.
Полная регрессия выявила ошибку crash-теста: он отправлял прежнюю цель уже с новым
epoch после arrival. Исправлено прекращением ввода при смене региона; runtime
правильно принимал эту новую команду. После исправления full suite прошла.
NuGetAudit=false — проверка не подтверждает отсутствие security warnings зависимостей.
Рабочая .data, пользовательские процессы и AGENTS.md не менялись; commit/push нет.

## Домашняя приёмка

1. Перезапусти сервер и клиент protocol24; используй свои обычные identity profiles.
2. На карте «Берега Переправы» дойди до голубой границы **X14, Z10**. Стену можно
   обойти по южной стороне (Z10). Появишься в «Восточной окраине» на **X−12, Z10**.
3. Проверь inventory, HP/ману, Эхо, skills/progression и UI; старая карта/акторы исчезли.
4. Возврат — голубая граница **X−14, Z10**, появление на **X12, Z10**. Исследование
   первой карты не потерялось, fog второй не открыл первую заново.
5. Со вторым клиентом: в разных регионах игроки не видят друг друга, membership
   сохраняется без удалённых HP/координат; в одном регионе снова видны.
6. Перезапусти клиент/сервер на окраине: персонаж должен остаться там, без дублей.

Не реализованы: маунт, обмен/продажа карт, межпроцессный handoff, live recovery,
новые региональные художественные ассеты и production capacity200/400.
