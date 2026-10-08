# Этап 9.2 — предметы в мире и ручной подбор

## Prototype slice

Два одноразовых общедоступных тестовых предмета: меч X=-5/Z=1 и броня X=-5/Z=4.
Seed IDs, definitions, позиции и радиус подбора (2 м) находятся в `Content.Server/appsettings.json`, секция GroundItems.
Это не правила boss/PvP loot или распределения наград. Никакого autoloot, автоподхода, выбрасывания, respawn или trade.

Подойти ПКМ, навести курсор на предмет и нажать `G`. `I` — инвентарь, итог операции виден в его панели.
G — временная prototype interaction binding; основные атаки/skills сохраняют прежнее управление.
На клиенте коробка/название — только публичный визуал, не authoritative instance.

## Сервер

GroundItemSimulation получает только owner entity и PickupCommand(sequence, runtime handle).
На fixed tick проверяются monotonic sequence, rate limit, наличие предмета, живой персонаж/каст,
серверные distance/проходимость прямого сегмента и вместимость inventory.
Одна bounded pending команда на персонажа. Два запросивших один предмет не получают два экземпляра:
первый обработанный допустимый запрос переносит instance, остальные получают Missing.
Это техническое упорядочивание прототипа, не постоянное правило приоритета loot.

В inventory переносится тот же persistent GUID/definition, без автоматической экипировки.
AOI использует отдельный spatial index; ground runtime handles не являются actor/inventory IDs.
Spawn/despawn — дельты с AOI hysteresis, не полный список мира и не глобальный scan каждый tick.
Prototype budget — 256 active ground items; клиентский подбор под курсором сканирует только этот bounded cache на нажатие.
На Mobile renderer используется общий mesh/material, без постоянных VFX.

## Persistence

Additive migration 0003 создаёт ground_items / project_g_ground_items; database schema v3.
Character/inventory documents остаются v1. Существующие персонажи не переписываются/не сбрасываются.
Seed ID уникален: первый startup сохраняет GUID/definition/position, последующие не пересоздают экземпляр.
Изменение координат существующего seed в config не перемещает его в БД; изменение мира/repair — отдельная операция.

В одном fenced batch transaction записываются персонаж, inventory и claim (`claimed_by IS NULL → character UUID`).
Конфликт claim/revision откатывает весь batch. Claimed rows сохраняются как tombstones, поэтому pickup не respawn после рестарта.
Claims относятся к текущему durability barrier и очищаются после commit; финальный save не пытается повторить старый claim.
До commit клиентам не отправляются despawn, обновлённый inventory или Accepted.
Pending disk I/O не блокирует polling; следующая simulation ждёт. Ошибка записи останавливает сервер без подтверждения.
Немедленная ошибка adapter также превращается в failed barrier, не в обходную запись inventory при shutdown.

SQLite worker I/O и PostgreSQL adapter используют прежние ownership/revision правила. Live PostgreSQL здесь не проверялся.
Multi-process конфликт claim fail-closed останавливает проигравший процесс; полноценные region leases/handoff ещё не реализованы.
Перед обновлением — backup SQLite при остановленном сервере. Старый сервер не поддерживает schema v3.

## Protocol v11

Все четыре сообщения ReliableOrdered, прежние IDs неизменны; клиенты v10 требуют обновления.
Полезная нагрузка после uint16 message ID:

- 29 PickupCommand: uint32 sequence, uint64 handle (12 bytes).
- 30 PickupResult: uint32 sequence, uint32 serverTick, uint8 outcome (9 bytes), только владелец.
- 31 GroundItemSpawn: uint64 handle, uint32 serverTick, float32 X/Z, bounded string name, uint8 EquipmentSlot.
- 32 GroundItemDespawn: uint64 handle, uint32 serverTick (12 bytes).

Ненулевые handles/sequences, finite coordinates, валидные enums, name ≤24 chars/48 UTF-8 bytes,
полное чтение packet с отказом на truncation/trailing bytes. Persistent GUID/seed/definition catalog клиент не получает.
Movement snapshots остаются Unreliable.

## Приёмка

1. Перезапустить сервер и два клиента v11 с разными identity profiles.
2. Оба видят две золотые коробки. Издали G отклоняется; подойти ПКМ, курсор на коробку, G переносит предмет в I.
3. Два игрока пытаются подобрать один предмет: он достаётся одному, у обоих исчезает из мира.
4. Повторный запрос/полный inventory/HP=0 не создаёт экземпляры и не удаляет доступный предмет.
5. Уйти из AOI и вернуться: незабранные предметы появляются без дублей.
6. Reconnect и рестарт сервера сохраняют inventory; забранные коробки не появляются снова.

После приёмки этапа 9 — этап 10 (первое Эхо), не запускается автоматически.
