# Этап 16 — PvP, смерть и репутация

Статус: реализован как bounded прототип, 2026-10-08; визуальная приёмка ожидается. Gameplay-пакет утверждён пользователем с изменениями в docs/design/stage16-rules-proposal.md. Protocol v22, content schema 3 / balance 3, SQL schema 7.

## Pipeline и границы

Сохраняются .NET authoritative fixed tick, Godot presentation, LiteNetLib, character/world leases и существующий durability barrier. Новый сервис и провайдер не добавляются. Серверный PvpDefinition задаёт геометрию и временные коэффициенты; ServerWorld.Pvp связывает policy, death, respawn и persistence. CombatSimulation принимает owner EntityId для всех источников, проверяет DamagePermission на каждом попадании и сообщает DamageApplied. Melee, projectile, AoE и Echo используют эту проверку. Echo дополнительно проверяет свою физическую позицию против города; ответственность относится к хозяину.

В город можно войти с тегом. PvP-урон запрещён, если источник или жертва внутри. Мирную жертву без тега может атаковать только явно включённый Criminal; добровольный PvP, активный тег и агрессор допускают законную атаку. Запомненный civilian эпизод сохраняет незаконность прежнего нападения после ответа жертвы. До 16 активных эпизодов на хозяина; при исчерпании новое преступное попадание отклоняется. Уже оплаченная способность без допустимой цели может промахнуться. За PvP-попадания нет combat EXP или practice.

Фазы: bounded mode/respawn intentions и таймеры → movement → melee/abilities/NPC/boss/Echo → смерть → inventory/pickup/crafting/trade/economy/progression → capture → fenced SaveWithWorldAsync → commit → publication. При HP=0 сразу останавливаются motion/cast и owner Echo; итог смерти формируется один раз после всех источников. Новая вещь доступна сразу, но перенос требует полного пятисекундного канала. Повтор не делает новый RNG, heal или transfer.

## Persistence и единственный владелец

Реализация уточняет первоначальный план: schema 8 и отдельные death SQL rows не понадобились. Optional SavedPvp v1 находится в SavedProgression v1; optional DeathLoot в SavedWorldNode v1 содержит полный SavedItem и координаты/UTC expiry. Старые документы без компонентов совместимы; HP=0 без SavedPvp превращается в legacy death без второго штрафа и дропа.

Death loot является world-owned item. Существующий schema 7 registry получает объединение market escrow UUID и DeathLoot UUID. GroundItemSimulation отображает эту world-owned вещь через прежний spatial index и runtime handle; legacy seed SQL ground_items/claims работают отдельно. SaveWithWorld атомарно сохраняет HP, EXP, PK, death receipt, full item, ownership registry и audit под character/world revision fences. При подборе UUID переходит из world в inventory; при expiry отсутствие в world переводит UUID в terminal. Возврата и recovery нет.

Сохраняются UUID, definition/evolution, enhancement, current/maximum/revision; Bound защищён от дропа, торговли и рыночного escrow. Legendary сам по себе не защищает. RNG равномерный серверный криптографический, только среди экипированных непривязанных экземпляров. Одно последнее durable receipt/watermark на персонажа, без полного архива запросов; повтор точного watermark отвечает AlreadyProcessed, устаревший/изменённый запрос отклоняется. Идентификатор смерти монотонный.

Лимиты: 8 death loot в регионе, 256 ground runtime items, 8 inventory slots, 16 aggression episodes, world JSON 8192 UTF-8 bytes. Дроп заранее проверяет общий JSON budget; при переполнении вещь остаётся экипированной, death/EXP/PK применяются, receipt/audit отмечает DropSkippedCapacity. Market проверяет совместный JSON budget до движения ownership. Только ограниченные active timers/channels/loot сканируются; AOI использует spatial index. Это не нагрузочная аттестация массового PvP.

## Подбор, смерть и возрождение

Любой живой игрок может начать G сразу при активном PvP-теге. Подбор не включает тег. Сервер повторно проверяет тег, дистанцию, LOS, capacity, alive/state на каждом tick и завершении. Движение, атака/каст, экипировка, другие взаимодействия, урон, смерть, trade и disconnect прерывают канал. Повтор G не сокращает и не перезапускает уже идущий канал. Эксклюзивной резервации нет; из двух завершений выигрывает одно. Expiry обрабатывается перед pickup: после 30 минут вещь уничтожается без возврата.

PvE теряет floor(5% текущего EXP), PvP/mixed floor(10%), без level/skills/profession loss. Mixed — NPC добил в течение 30 секунд после последнего разрешённого PvP урона. Civilian kill даёт последнему ответственному владельцу PK+1 и −100, первое незаконное нападение в эпизоде −10. Смерть отменяет trade и quotes; respawn по явной команде через 15/60 секунд в (-9,0), full HP/half mana, сохранённые cooldown и Echo UUID, защита 10 секунд до враждебного действия. Development Revive остаётся debug действием, не возвращает item/EXP и не удаляет receipt.

Clock внутри процесса: UTC origin + monotonic Stopwatch; persisted UTC deadlines продолжаются после рестарта. Остановка процесса не симулирует offline combat. Invalid/future version/null/duplicate/bound/world UUID/content mismatches отклоняются при восстановлении.

## Combat disconnect

Tagged actor остаётся до 30 секунд без новых команд и с удержанной CharacterSession. Transport connection заменяется внутренним detached handle; credential хранится для lookup только как SHA256 hash и не логируется. Reconnect после pending commit переносит ту же сущность/session на новый peer; session sequences сбрасываются, HP/mana/cooldown/death/PK не сбрасываются. Dead tagged actor также может удерживаться до конца окна, чтобы не потерять ответственность за mixed death другой жертвы. Срок окна не продлевается повторной заявкой; expiry/shutdown выполняет final checkpoint и release. Session budget включает detached actors. Production identity/anti-abuse не входят в этап.

## Protocol и клиент

ReliableOrdered IDs 69–75: PvpCommand, PvpResult, PvpPublicState, PvpState, PickupChannelState, PvpZoneState, PvpLootState. Команда объединяет Mode/Respawn, требует explicit acknowledgment Criminal и актуальный death ID. Строгие размеры/enums/booleans/finite values/trailing bytes/ownership; самый большой новый body 54 bytes, меньше существующего 1200-byte бюджета. Movement остаётся Unreliable.

Public mode/tag/aggressor/protection/dead — только AOI сущностей; private reputation/PK/death loss/timers/receipt и канал — только хозяину. Persistent UUID/эпизоды/секретные условия не отправляются. V открывает панель PvP/смерти, explicit Criminal checkbox, respawn button; countdown информационный, повторный запрос не назначает результат. Green boundary показывает safe city. G и подпись ground item показывают тег/5 секунд/TTL. Подписки и очистка симметричны при выходе/reconnect.

## Проверка и ограничения

Команды, результаты и домашние сценарии: docs/design/pvp-stage16-progress.md. Проверены policy, настоящие projectile/AoE, Echo attribution/safe origin, no PvP EXP, death/respawn/replay, full item/bound/legendary, channel/race/interrupt/tag expiry, capacity fallback, malformed wire/documents, real SQLite rollback/restart/terminal ownership и lossy UDP commit gate/reattach/shutdown.

Godot scene/UI не запускались: executable отсутствует в Codespace. Live PostgreSQL 16 проверен в отдельном Docker-контейнере: death/restart/full-instance pickup, stale revision atomic rollback и terminal expiry, вместе с полной suite 375/375. Factions/bounties/positive reputation/criminal settlements/party rules/assists/multiple regions/history retrieval/load certification не входят в этап. Этап 17 не начат.
