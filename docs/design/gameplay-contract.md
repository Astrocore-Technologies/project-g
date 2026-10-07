# Project G — gameplay contract

Краткий implementation-facing снимок **принятых** gameplay-решений. Это не полный GDD.

## Мир
- Полноценная stylized 3D MMORPG, high-angle 3/4 camera.
- Один логический persistent single-shard world.
- Без gameplay channels/layers/копий одной локации.
- Мир может физически делиться на regions/cells/workers.
- Live-DM и community actions меняют world state.
- Секреты могут оставаться нераскрытыми годами.

## Сюжет
- Персонального main story нет.
- Сюжет принадлежит миру; одновременно идёт множество world storylines.
- Игрок влияет прямо или косвенно, даже на низком уровне.
- Модель: Nodes + Riverbeds, где outcome определяется совокупностью действий игроков.

## Старт
- Игрок призван в мир и сам выбирает, чем заниматься.
- В полной версии выбирает стартовый город; в разработке достаточно одного.
- Других игроков встречает сразу.
- Echo доступны с начала.
- Tutorial объясняет только управление, базовый бой, interaction/UI и идею игры.

## Combat
Формула: **high-angle click-to-move action combat + manual attacks + skillshot/AoE + soft targeting + tactical dodge**.

PC baseline:
- RMB ground — movement destination;
- cursor — aim/direction context;
- LMB — manual basic attack, hold может repeat;
- Q/W/E/R/A/S/D/F — main skills;
- Space — tactical Dodge/Dash;
- отдельная manual Signature команда для Echo.

Не допускается как фундамент:
- auto basic attack;
- full auto combat;
- auto-walk into range;
- target-based autoattack;
- WASD-primary movement.

Skill forms: projectile, directional/skillshot, cone, ground AoE, self AoE, dash/charge, targeted heal/support где уместно.

## Character level / EXP
- Обычный character level существует.
- Есть max level; cap повышается с крупными global updates.
- High-level zones не закрываются hard level gate.
- Level-up даёт **только stat points**.
- **Skill points отсутствуют**.
- EXP: quests, exploration, discoveries, crafting, combat, world events и другие meaningful activities.
- Normal death может забрать накопленный EXP, но не уже полученный level.
- Level loss только в специальных явно спроектированных событиях.

## Base stats
Strength, Agility, Vitality, Intelligence, Dexterity, Luck.

Все шесть базовых характеристик неотрицательные, в том числе после модификаторов.
Производные статы могут быть отрицательными через модификаторы; HP не может быть отрицательным.
Hard/soft caps не являются целью; formulas должны оставаться стабильными на больших положительных/отрицательных значениях.
Defense снижает damage; negative Defense может увеличивать incoming damage.

Принята Ragnarok-подобная основа влияний с собственной математикой:
- STR: ближняя физическая атака и усиление оружия; небольшой вклад в дальнюю атаку.
- AGI: основной вклад в скорость атак, небольшой вклад в физическую защиту. Скорость перемещения не меняет.
- VIT: HP, восстановление HP, эффективность лечебных предметов, физическая и немного магической защиты.
- INT: магическая атака, мана, восстановление маны, эффективность mana-предметов, магическая защита; вторичный вклад в скорость каста.
- DEX: дальняя физическая атака и усиление оружия; основной вклад в скорость каста; вторичные вклады в ближнюю/магическую атаку, магическую защиту и скорость атак.
- LUK: критический шанс и вторичные вклады в физическую/магическую атаку.

Геометрическое попадание не отменяется случайной Hit/Flee/Perfect Dodge-проверкой. Ручной Dodge остаётся самостоятельной механикой.
Нет порогового иммунитета от статов и порога мгновенного каста. Влияние статов непрерывное, без округления «каждые N единиц».
Знаковые производные статы отделены от допустимого результата действия: вероятность в [0, 1], ненулевые длительности положительные, конечная сила атаки не превращается в лечение. Правила расходования ресурса при отрицательном производном MaxMana появятся с механикой ресурсов; этап 4 его не расходует.
Коэффициенты этапа 4 — временный prototype balance в серверных данных, не окончательный баланс.
Вес, сопротивления конкретным статусам и влияние LUK на лут/заточку/секреты здесь не определяются.

## Skills
- Изученных skills может быть много.
- На active bar ориентир — **8 skills**.
- Sources: profession fixed set, skill books, special quests, world events.
- Новый skill сразу на **level 1**.
- Skill points нет.
- Skills прокачиваются использованием.
- Возможны neutral skills вне основной профессии, например нейтральная магия у мечника.

## Professions
- Одна profession system.
- Одна active profession на character.
- Новая profession необратимо заменяет старую; switch назад нельзя.
- Hidden professions открываются actions/exploration/lore/events/world state, не class menu.
- Conditions/counters server-only.
- Некоторые professions могут быть one-of-one.
- Ownership/loss rules уникальной profession могут отличаться по конкретной профессии.

## Echoes of the Past
- Echo — gacha characters и active companions.
- Echo прежде всего персонажи мира, потом combat units.
- Echo = imprint прошлого, не буквальное resurrection.
- Дубликаты = дополнительные fragments/resonance.
- Stars/duplicates могут реально усиливать Echo.
- Игрок может вызвать **до 3 Echo одновременно**.
- Возможны Basic AI, passives, automatic abilities, manual Signature Ability.
- Personality/values/affinity/mood имеют gameplay consequences.
- Echo может временно отказаться разговаривать/сражаться/использовать Signature, если это соответствует личности.
- Echo могут быть полезны в exploration/lore/crafting/social world interaction.
- Weapon gacha не является частью принятого направления.

## Groups / roles
- Normal party: **6 players**.
- Extended group/raid: до **20**.
- Mandatory Holy Trinity нет.
- Tank/healer/DPS-like roles могут возникать, но composition не должна быть жёстко обязательной.
- Group-designed boss может быть soloable exceptional player'ом.
- Boss difficulty/rewards не autoscale по числу участников.

## PvP / PK
- Working model: PvP-tag позволяет open attack в большинстве world areas.
- Есть safe/protected areas.
- Faction wars, PK/criminal path, reputation, bounties.
- PK consequences systemic/social, а не только запретительные.
- Criminal life может открывать alternative settlements/black markets/smugglers/professions/connections.

## Death
PvE:
- loses EXP;
- default gear loss нет.

PvP with active tag:
- loses EXP;
- возможен gear drop.

No autoloot. Dropped items существуют в мире и потенциально могут быть подняты third parties.

Точные drop/ownership rules ещё unresolved.

## Travel / map / cartography
- **Fast travel отсутствует**.
- Walking + mounts; ships later; possible public transport later; air travel only distant future.
- Fog of war.
- Только basic map markers, без GPS-подсказок к секретам.
- Player знает exact position, имеет compass и coordinates.
- Можно практически заблудиться из-за незнания terrain/routes/caves.
- Hidden shortcuts могут сильно сокращать travel time и иметь strategic/economic value.
- Maps можно share и sell.
- Cartography может быть самостоятельной игровой деятельностью.

## Economy baseline
- Goods/services производят и players, и NPC.
- Local markets, regional prices/availability.
- Transport/logistics — gameplay.
- No player-built cities.
- Guilds потенциально владеют существующими world objects: ports/mines/fortresses/enterprises etc.
- Exact taxes/ownership rules unresolved.

## Weapons / enhancement
- Crafted weapons могут эволюционировать от materials/additives/catalysts/consumed items/crafter mastery/method.
- Enhancement имеет +levels и RNG.
- Failure снижает max durability и может уничтожить item.
- Safe reforge/reroll нет.
- Items tradable.
- Official game-mediated RMT — только distant possibility после legal/economic design.

## Legendary artifacts
- True one-of-one global artifacts допустимы.
- Имеют собственную историю/владельцев/evolution/transfer rules.
- Legendary не обязан быть best-in-slot для любого build.

## Exploration
- Огромный мир, long-lived secrets.
- Echo hints не превращаются в GPS.
- Hidden logic не отправляется клиенту заранее.

## Live-DM
- Neutral World Director/chronicler/arbiter, не enemy boss.
- Меняет prepared region state/routes/threats/NPC dialogue/rumors/events через безопасные инструменты.
- Никакого arbitrary production code execution.

## Competitive / esports
- No progression normalization.
- Real profession/stats/build/equipment/weapon/enhancement/crafted properties/skills сохраняются.
- Echo участвуют только если format позволяет.
- Major pro/LAN — отдельный Tournament Server со snapshot реального character.
- Equal technical conditions != identical characters.

## Intentionally unresolved — не решать молча
- exact PvP loot/drop ownership;
- legendary transfer through PvP death;
- boss loot/contribution rules;
- exact respawn rules;
- exact PK thresholds;
- exact guild ownership/taxes;
- exact crafting modifier counts / enhancement probabilities;
- official RMT model;
- dungeon instancing/replay;
- mass world-boss reward rules;
- detailed rumor/quest information system;
- long-term seasonal progression.
