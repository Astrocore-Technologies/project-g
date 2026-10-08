# Этап 16 — реализация и приёмка

2026-10-08: этап реализован как bounded прототип; визуальная приёмка ожидается. Правила: stage16-rules-proposal.md. Архитектура: ../architecture/stage16-pvp-death-plan.md. Этап 17 не начат.

## Реализовано

Серверные Peaceful/Voluntary/Criminal с явным предупреждением, tag 120 секунд, aggressor 600, rep/PK и самооборона. Safe city X −12…−7 / Z −4…4 допускает вход с тегом, запрещает PvP внутрь/наружу для melee/projectile/AoE/Echo. Урон Echo относится к хозяину. PvP не даёт EXP/practice.

Death ID/receipt: PvE 5% EXP, PvP/mixed 10%, без level/skills/profession loss. До одного случайного equipped unbound экземпляра, включая legendary без bound. Полные UUID/evolution/enhancement/current/maximum/revision сохраняются. Подбор сразу доступен всем живым с активным тегом, требует 5 секунд и повторной проверки тега при завершении. G не включает PvP. Движение/действие/урон/disconnect прерывает канал. Гонка даёт одного владельца; через 30 минут terminal destruction, без возврата.

Explicit respawn через 15/60 секунд в (-9,0): full HP/half mana, cooldown/Echo UUID сохраняются, защита 10 секунд до hostile action. Combat disconnect держит actor/session до 30 секунд; reconnect возвращает ту же сущность, expiry/shutdown сохраняет и освобождает session.

Protocol v22, balance 3, SQL schema 7: optional SavedPvp/DeathLoot JSON с existing unique registry; старые saves/ground seeds совместимы, reset не нужен. Лимиты: 8 death loot, 256 ground runtime items, 16 aggression episodes, 8192-byte world JSON. При полном бюджете смерть/EXP/PK применяются, вещь остаётся экипированной с DropSkipped receipt. Recovery и полный архив receipts отсутствуют. Production auth/anti-abuse, factions/bounties/искупление/parties/multiple regions отложены.

## Проверка в Codespace

~~~sh
dotnet build Game.slnx --artifacts-path .artifacts/stage16 -m:1 --no-restore
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage16 --no-restore --filter FullyQualifiedName~Pvp
dotnet test Content.Tests/Content.Tests.csproj --artifacts-path .artifacts/stage16 --no-build --no-restore
dotnet .artifacts/stage16/bin/Content.Server/debug/Content.Server.dll --validate-content
git diff --check
~~~

Полный набор 375/375, 24 новых PvP проверки. C# client/server/shared/database/tests: сборка без ошибок, существующие NU1903 SQLitePCLRaw.lib.e_sqlite3 2.1.11. Content schema3/balance3 validation успешно. Tracked/untracked whitespace проверены отдельно.

Покрыты policy/city/tag/самооборона, настоящие projectile/AoE без PvP EXP, Echo attribution/safe origin, PvE/mixed/death/legacy HP0, respawn/replay, legendary/bound/unequipped, 4999ms/5000ms/race/interrupt/tag expiry/capacity, malformed wire/documents. Real SQLite death→restart→pickup→restart/expiry terminal и stale revision rollback. Два UDP клиента с latency 100–150ms / loss10% проверяют публикацию после commit, same actor reconnect с reset session sequence и shutdown held actor.

Godot executable отсутствует в PATH: сцена/UI не запускались, C# сборка не заменяет runtime acceptance. Live PostgreSQL 16 проверен на отдельном временном Docker-контейнере: два persistence-сценария проходят, full suite включает оба провайдера без skips. Рабочая .data/пользовательские процессы не менялись, commit/push не выполнены.

## Домашняя проверка двух клиентов

1. Перезапустить свои клиент/сервер вместе на protocol v22. V — PvP/смерть, F1 — памятка. Мирные не повреждают друг друга. Вне города включить PvP: тег сразу120с, выключение после таймера.
2. Criminal через галочку/предупреждение, удар мирного: −10/агрессор, ответ и атака третьих лиц законны. Проверить LMB/Q/W/T и границу города: вход с тегом разрешён, PvP внутрь/наружу запрещён.
3. Экипировать enhanced unbound blade и погибнуть в PvP. V показывает EXP/drop. Мирный без тега не подбирает; tagged игрок нажимает G под курсором, стоит5с, получает тот же full instance без автоподхода.
4. Прервать G движением/атакой/уроном/экипировкой/interaction/disconnect; вещь остаётся. Два клиента не получают две копии. Через30мин вещь исчезает без возврата.
5. Через60с подтвердить respawn в V (PvE15с). Проверить city/HP/mana/cooldown/Echo/PK/профессию; повтор не лечит второй раз, reconnect не оживляет автоматически.
6. Отключить tagged клиент: второй видит actor до30с и может убить вне города. Быстрый reconnect возвращает того же actor. Restart сохраняет death/XP/PK/loot/TTL без повторения потерь.

Визуальную приёмку отмечает пользователь отдельно. Development «Оживить» в I — debug, не возвращает item/EXP и не заменяет gameplay respawn.

## Повтор live PostgreSQL проверки

LivePostgresPvpTests используют PROJECT_G_TEST_POSTGRES с connection string отдельной disposable test БД. Без переменной эти два теста явно skipped; успешный прогон этапа был 375 passed / 0 skipped. Тест создаёт персонажей/world/UUID и migrations; не направлять его в рабочую БД. В этом прогоне контейнер postgres:16 использовал tmpfs и только loopback port; после тестов штатно остановлен и автоматически удалён.
