# Этап 9.1 — инвентарь и экипировка

Это временный prototype slice, не окончательные правила inventory/gear.

## Объём

- Definitions в серверном JSON schema 3; экземпляры имеют persistent GUID и принадлежат персонажу.
- Временная вместимость: 8 предметов. Два слота: Weapon и Armor, максимум один экземпляр в каждом.
- Два тренировочных предмета выдаются один раз: меч (+8 melee attack) и броня (+8 physical defense, +20 max HP).
- Без надетого меча сохраняется прежний тренировочный боевой профиль. Unarmed mechanics пока не проектировались.
- Пересчёт modifiers на серверном tick; клиент отправляет только Equip/Unequip и opaque runtime handle.
- Экипировка не лечит, не восстанавливает ману и не сбрасывает cooldown. Снижение maxima ограничивает текущие ресурсы.
- Смена запрещена при HP=0, касте или незавершённых собственных ability effects, чтобы не менять damage в полёте.
- Повторный sequence игнорируется; чужой handle отклоняется; несколько requests в tick отменяют pending action.
- Inventory, gear, stats/resources сохраняются согласованно; подтверждение отправляется только после commit.

## Сеть и клиент

Protocol v10: IDs 26 InventoryCommand, 27 InventoryResult, 28 InventoryState, ReliableOrdered для редких событий.
Frequent movement snapshots остаются Unreliable. Commands имеют sequence; responses/state — server tick.
InventoryState ограничен 8 записями, name ≤24 символов/48 UTF-8 bytes, finite modifiers, уникальными handles/slots,
валидируется полностью с отказом на trailing bytes. Постоянные GUID и server definition IDs не передаются.
Инвентарь и результат команды получает только владелец, публичный CombatState в AOI отражает обновлённый max HP.
При reconnect runtime handles могут измениться, persistent instances сохраняются.

Godot: панель справа, `I` скрывает/показывает. «Надеть»/«Снять» ждут подтверждённого состояния сервера,
без optimistic gear/resource updates. Scenes, project.godot и AGENTS.md не менялись.

## Приёмка

1. Перезапустить сервер и два клиента v10 с разными identity profiles.
2. Открыть inventory (`I`), надеть меч: атака сильнее; надеть броню: max HP больше, текущие HP не увеличиваются.
3. Снять/повторно надеть: HP/мана не восстанавливаются, cooldown нельзя обойти.
4. Disconnect/reconnect и перезапуск сервера сохраняют те же предметы и экипировку, не выдавая новый стартовый набор.
5. Второй игрок не получает содержимое чужого inventory; публичный HP/max HP синхронизируется.

После приёмки 9.1: 9.2 — предметы в мире и ручной подбор с защитой от двойного получения.
Ground items, loot rewards, PvP drop ownership, trade, durability и crafting здесь не реализованы.
