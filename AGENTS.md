# Project G — правила репозитория для AI-агентов

## 1. Назначение

Project G — долгоживущая stylized 3D MMORPG на Godot и .NET.
Главное обещание игроку: **«Мир замечает то, что ты делаешь»**.

Этот файл — конституция репозитория: стабильные инженерные правила, границы ответственности, источники истины и требования к проверке изменений.

Не превращай `AGENTS.md` в полный GDD или roadmap. Детальный зафиксированный gameplay-контракт хранится в `docs/design/gameplay-contract.md`, порядок реализации — в `.codex/plan.md`, фундаментальные технические решения — в ADR внутри `docs/architecture/`.

## 2. Источники истины

### Gameplay / design
1. Явная инструкция пользователя в текущей задаче.
2. `docs/design/gameplay-contract.md`.
3. Принятые ADR / design docs, если относятся к вопросу.
4. `.codex/plan.md`.
5. Текущая реализация.

### Техническая архитектура
1. Явно одобренное пользователем архитектурное изменение.
2. Принятые ADR в `docs/architecture/`.
3. Этот `AGENTS.md`.
4. `.codex/plan.md`.
5. Текущая реализация.

`plan.md` не является источником геймдизайна. Если план конфликтует с принятым gameplay-решением или ADR, обнови план, а не реализуй устаревшее поведение.

Если для реализации требуется постоянное gameplay-правило, которого нет в gameplay contract, не придумывай его молча.

## 3. Текущий стек
- Клиент: Godot 4.7.1, C#, Godot.NET.Sdk.
- Runtime: .NET 10.
- Renderer: Godot Mobile renderer.
- Client physics: Jolt Physics.
- Сервер: отдельное headless .NET 10 приложение без Godot.
- Сеть: LiteNetLib.
- Общие сетевые контракты: `Content.Shared`.
- Сначала PC / Steam, Mobile позже.

Не заменяй Godot, LiteNetLib, .NET или фундаментальный split Client/Server/Shared без отдельного архитектурного решения пользователя.

## 4. Границы проектов

### `Content.Client`
Только клиентские задачи:
- Godot scenes/nodes/resources;
- input, camera;
- rendering, animation, VFX, sound, UI;
- prediction, reconciliation, interpolation;
- отображение подтверждённого сервером состояния.

Клиент не является источником истины для позиции, урона, HP, inventory, economy, progression, professions, loot, gacha RNG или world state.

### `Content.Server`
Сервер авторитетен и отвечает за:
- world/player simulation;
- validation клиентских команд;
- movement, combat, AI, skills/effects;
- hidden profession conditions;
- inventory/items/crafting/economy;
- region state, events, Live-DM;
- persistence;
- anti-cheat / rate limiting.

`Content.Server` не зависит от Godot или `Content.Client`.

### `Content.Shared`
Только реально общие контракты:
- message IDs;
- bounded DTO;
- protocol versions;
- compact IDs/enums;
- безопасные детерминированные helpers.

Запрещено класть в `Content.Shared`:
- Godot types;
- hidden profession logic;
- secret counters;
- loot formulas;
- gacha RNG;
- economy rules;
- unrevealed content definitions.

## 5. Жёсткие архитектурные инварианты
- Сервер всегда авторитетен.
- Клиент отправляет **намерение**, а не результат.
- Network polling и gameplay simulation — разные concerns.
- Simulation работает на fixed server tick.
- Runtime network IDs и persistent database IDs — разные сущности.
- Network protocol bounded, versioned и валидируется до чтения.
- Frequent snapshots не должны автоматически идти через ReliableOrdered.
- Interest management обязателен до серьёзного масштабирования.
- Один логический мир не означает один физический процесс.
- Не решать нагрузку через gameplay channels/layers/копии одной зоны.
- Persistence — versioned models + migrations.
- Экономические операции в зрелой системе должны быть transactional, auditable, idempotent.

## 6. Защита скрытой информации
Hidden professions и world secrets — ключевой столп игры.

Клиент не должен получать то, что игрок ещё не должен знать.

Не отправляй клиенту заранее:
- полный список hidden professions;
- unlock conditions;
- hidden counters;
- thresholds;
- secret event sequences;
- revealing internal names/descriptions;
- server-only checks.

Условия открытия проверяются сервером. Клиент получает результат только в момент, когда информация должна стать известна.

## 7. Gameplay contract
Перед реализацией gameplay обязательно проверь `docs/design/gameplay-contract.md`.

Особенно важные уже принятые non-default правила:
- RMB click-to-move;
- LMB manual basic attack;
- no auto basic attack;
- action combat + soft targeting;
- ~8 active skills;
- до 3 Echo;
- no mandatory Holy Trinity;
- no fast travel;
- no personal main story;
- single logical shard без gameplay layers.

Если правило не определено — предложи варианты или используй явно помеченное временное prototype assumption. Не закрепляй его в архитектуре как постоянное решение.

## 8. Server simulation / networking
- Любой client packet считается недоверенным.
- Проверяй ownership, state, sequence, timing, cooldown, resource cost, distance и rate limits где применимо.
- Не связывай gameplay rules с client FPS или packet arrival frequency.
- Используй fixed-tick simulation.
- Local responsiveness: prediction + reconciliation где нужно.
- Remote entities: interpolation.
- Lag compensation — только bounded и без преимущества высокого ping.
- Не рассылай весь мир всем игрокам.
- Используй region/cell/AOI relevance.
- Предпочитай numeric stable IDs строкам в hot paths.
- Избегай лишних allocations/LINQ/boxing/closures в tick/network hot paths.
- Не превращай transport service в giant gameplay manager.

## 9. Godot client
- Scenes — композиция, а не глобальная база данных.
- Кэшируй node references; не ищи их по string path каждый frame.
- `_PhysicsProcess` — movement/prediction; render interpolation/UI держи отдельно.
- Subscribe/unsubscribe симметрично.
- Network resources корректно останавливаются в `_ExitTree`.
- Не передавай Godot objects между потоками.
- `.tscn` и `project.godot` меняй минимально.
- `.uid`/imported generated files не редактируй вручную без необходимости.
- Учитывай high-angle readability и Mobile renderer.

## 10. Data-driven content
Код задаёт rules/invariants, данные — конкретный content/balance.

Data-driven definitions нужны для:
- stats;
- skills;
- effects;
- items/weapons;
- NPC;
- professions;
- Echo;
- rewards;
- regions/events.

Нужны stable IDs, schema/reference validation, понятные load errors и server-side загрузка без Godot.

Base content definitions хранятся отдельно от mutable persistent world state.

## 11. Производительность
- Не делай global scans всех players/NPC каждый frame/tick.
- Используй spatial partitioning / broad-phase / regions / cells.
- Дальний AI может обновляться реже.
- Не делай тяжёлую работу в `_Process`, `_PhysicsProcess` или server tick, если можно event-driven/реже.
- Профилируй до сложной оптимизации.
- Mobile constraints учитываются даже при PC-first разработке.

## 12. Workflow
Перед нетривиальной реализацией:
1. Изучи затронутые файлы.
2. Проверь gameplay contract и relevant ADR.
3. Определи Client/Server/Shared boundaries.
4. Назови protocol/persistence/performance/mobile последствия.
5. Используй подходящий repo Skill.
6. Делай маленькие хирургические патчи.
7. Выполни соразмерную проверку.
8. Отчитайся, что реально запускалось.

Не переписывай несвязанный пользовательский код.

## 13. Repo-local Skills
Используй при необходимости:
- `$implementation-strategy` — нетривиальная feature/refactor planning.
- `$network-protocol-change` — изменение wire protocol/messages.
- `$server-simulation` — authoritative movement/combat/AI/world simulation.
- `$godot-client-change` — Godot scenes/input/prediction/UI.
- `$code-change-verification` — проверка перед handoff.
- `$architecture-decision` — фундаментальные архитектурные изменения.

## 14. Verification
Обычно для C# изменений:

`dotnet build Game.slnx`

Дополнительно запускай минимально релевантные tests:
- protocol round-trip;
- malformed packet tests;
- server validation tests;
- domain unit tests;
- integration tests для transport/handshake.

Для Godot изменений запускай затронутую сцену, если среда позволяет.

Не утверждай, что проверка пройдена, если она не запускалась.

## 15. Когда остановиться и спросить
Запрашивай решение пользователя перед:
- сменой engine/network/runtime foundation;
- изменением single-shard/no-layer architecture;
- выбором unresolved permanent gameplay rule;
- irreversible persistence migration;
- сменой authoritative ownership boundary;
- крупным новым service/process boundary;
- сменой database strategy;
- новым monetization/economy правилом без явного решения.

Локальные, обратимые и contract-compatible детали можно реализовывать без лишнего подтверждения.

## 16. Licensing
Не копируй сторонний код/ассеты без проверки лицензии.
Не выдумывай SPDX/license headers до появления утверждённого license file.
Сохраняй существующие third-party copyright/license headers.
